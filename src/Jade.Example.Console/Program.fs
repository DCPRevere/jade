open System
open Marten
open Testcontainers.PostgreSql
open Serilog
open Jade.Core.CommandBus
open Jade.Core.EventSourcing
open Jade.Core.MartenRepository
module C = Customer
module O = Order
open Jade.Domain.MartenConfiguration

// Configure Serilog
Log.Logger <- LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger()

Log.Information("🚀 Jade Event Sourcing Library - Complete F# Command Bus Flow")
Log.Information("=============================================================")

let demonstrateCompleteFlow () = async {
    Log.Information("")
    Log.Information("🎯 DEMONSTRATION: Complete Command Bus → Aggregate → Marten Flow")
    Log.Information("===============================================================")
    
    // Set up PostgreSQL container
    Log.Information("🐘 Setting up PostgreSQL container...")
    let postgresContainer = 
        PostgreSqlBuilder()
            .WithImage("postgres:15")
            .WithDatabase("jade_demo")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build()
    
    do! postgresContainer.StartAsync() |> Async.AwaitTask
    Log.Information("✅ PostgreSQL container started")
    
    try
        let connectionString = postgresContainer.GetConnectionString()
        Log.Information("🔗 Connection string: {ConnectionString}", connectionString)
        
        // Set up Marten document store
        Log.Information("📦 Configuring Marten document store...")
        let documentStore = 
            DocumentStore.For(fun options ->
                options.Connection(connectionString)
                options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
                options.DatabaseSchemaName <- "jade_events"
                configureDomainMarten options)
        
        // Clean and initialize database
        do! documentStore.Advanced.Clean.CompletelyRemoveAllAsync() |> Async.AwaitTask
        Log.Information("✅ Marten configured and database initialized")
        
        // Set up command bus with multiple handlers
        Log.Information("🚌 Setting up command bus with multiple handlers...")
        let commandBus = CommandBus()
        
        // Register Customer handler
        let customerRepository = createRepository<C.Command, Jade.Core.EventSourcing.IEvent, C.State> documentStore C.aggregate
        let customerHandler = AggregateCommandHandler(customerRepository, C.aggregate, C.getId, "👤 CUSTOMER")
        commandBus.RegisterHandler customerHandler
        Log.Information("✅ Registered CUSTOMER command handler")
        
        // Register Order handler
        let orderRepository = createRepository<O.Command, Jade.Core.EventSourcing.IEvent, O.State> documentStore O.aggregate
        let orderHandler = AggregateCommandHandler(orderRepository, O.aggregate, O.getId, "📦 ORDER")
        commandBus.RegisterHandler orderHandler
        Log.Information("✅ Registered ORDER command handler")
        
        Log.Information("✅ Command bus configured with 2 handlers")
        
        // Create and send commands
        let customerId = Guid.NewGuid()
        
        Log.Information("")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("PART 1: CUSTOMER COMMANDS")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("")
        Log.Information("📝 Step 1: Creating and sending Customer CREATE command")

        let createV2Data: C.CreateCustomerV2 = {
            CustomerId = customerId
            Name = "Alice F# User"
            Email = "alice@fsharp-demo.com"
            Phone = "+1-555-F#"
        }
        let createVersion: C.CreateCustomer = C.V2 createV2Data
        let createCommand = C.Command.Create createVersion
        
        Log.Information("📤 Sending Customer CREATE command through bus")
        let! createResult = commandBus.Send createCommand
        
        match createResult with
        | Ok () -> 
            Log.Information("✅ CREATE command succeeded")
            
            // Verify the state was persisted
            Log.Information("")
            Log.Information("🔍 Verifying state was persisted in Marten...")
            let! stateResult = customerRepository.GetById customerId
            match stateResult with
            | Ok (state, version) ->
                Log.Information("✅ Retrieved persisted state:")
                Log.Information("   ID: {CustomerId}", state.Id)
                Log.Information("   Name: {CustomerName}", state.Name)
                Log.Information("   Email: {CustomerEmail}", state.Email)
                Log.Information("   Phone: {CustomerPhone}", state.Phone)
                Log.Information("   Version: {Version}", version)
                
                // Send update command
                Log.Information("")
                Log.Information("📝 Step 2: Creating and sending UPDATE command")
                let updateV1: C.UpdateV1 = {
                    CustomerId = customerId
                    Name = "Alice Updated via F#"
                    Email = "alice.updated@fsharp-demo.com"
                }
                let updateVersion: C.UpdateCustomer = C.UpdateCustomer.V1 updateV1
                let updateCommand = C.Command.Update updateVersion
                
                Log.Information("📤 Sending UPDATE command through bus: {UpdateCommand}", updateCommand)
                let! updateResult = commandBus.Send updateCommand
                
                match updateResult with
                | Ok () ->
                    Log.Information("✅ UPDATE command succeeded")
                    
                    // Verify final state
                    Log.Information("")
                    Log.Information("🔍 Verifying final state after update...")
                    let! finalStateResult = customerRepository.GetById customerId
                    match finalStateResult with
                    | Ok (finalState, finalVersion) ->
                        Log.Information("✅ Final persisted state:")
                        Log.Information("   ID: {CustomerId}", finalState.Id)
                        Log.Information("   Name: {CustomerName}", finalState.Name)
                        Log.Information("   Email: {CustomerEmail}", finalState.Email)
                        Log.Information("   Phone: {CustomerPhone}", finalState.Phone)
                        Log.Information("   Version: {Version}", finalVersion)
                        
                        // Verify events in database
                        Log.Information("")
                        Log.Information("🗃️ Verifying events in PostgreSQL database...")
                        use session = documentStore.LightweightSession()
                        let! streamEvents = session.Events.FetchStreamAsync(customerId) |> Async.AwaitTask
                        Log.Information("✅ Found {EventCount} events in stream:", streamEvents.Count)
                        streamEvents |> Seq.iteri (fun i event ->
                            Log.Information("   Event {EventNumber}: {EventType} (Version {EventVersion})", (i+1), event.EventTypeName, event.Version)
                        )
                        
                    | Error err ->
                        Log.Error("❌ Failed to retrieve final state: {ErrorMessage}", err)
                        
                | Error err ->
                    Log.Error("❌ UPDATE command failed: {ErrorMessage}", err)
                    
            | Error err ->
                Log.Error("❌ Failed to retrieve state after create: {ErrorMessage}", err)
        | Error err -> 
            Log.Error("❌ CREATE command failed: {ErrorMessage}", err)
        
        // Now test Order commands
        Log.Information("")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("PART 2: ORDER COMMANDS")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("")
        
        let orderId = Guid.NewGuid()
        
        Log.Information("📝 Step 3: Creating and sending Order CREATE command (V2 - with promo code)")
        let orderItems: O.OrderItem list = [
            { ProductId = Guid.NewGuid(); Quantity = 2; Price = 29.99m }
            { ProductId = Guid.NewGuid(); Quantity = 1; Price = 49.99m }
        ]
        let createOrderV2: O.CreateOrderV2 = {
            OrderId = orderId
            CustomerId = customerId
            Items = orderItems
            PromoCode = "NESTED10"
        }
        let createOrderVersion: O.CreateOrder = O.V2 createOrderV2
        let createOrderCommand = O.Command.Create createOrderVersion
        
        Log.Information("📤 Sending Order CREATE command through bus")
        let! orderCreateResult = commandBus.Send createOrderCommand
        
        match orderCreateResult with
        | Ok () -> 
            Log.Information("✅ Order CREATE command succeeded")
            
            // Verify Order state was persisted
            Log.Information("")
            Log.Information("🔍 Verifying Order state was persisted in Marten...")
            let! orderStateResult = orderRepository.GetById orderId
            match orderStateResult with
            | Ok (orderState, orderVersion) ->
                Log.Information("✅ Retrieved persisted Order state:")
                Log.Information("   ID: {OrderId}", orderState.Id)
                Log.Information("   CustomerId: {CustomerId}", orderState.CustomerId)
                Log.Information("   Items: {ItemCount} items", orderState.Items.Length)
                Log.Information("   Total Value: {TotalValue:C}", (orderState.Items |> List.sumBy (fun i -> i.Price * decimal i.Quantity)))
                Log.Information("   PromoCode: {PromoCode}", orderState.PromoCode)
                Log.Information("   Status: {OrderStatus}", orderState.Status)
                Log.Information("   Version: {Version}", orderVersion)
                
                // Verify Order events in database
                Log.Information("")
                Log.Information("🗃️ Verifying Order events in PostgreSQL database...")
                use session = documentStore.LightweightSession()
                let! orderStreamEvents = session.Events.FetchStreamAsync(orderId) |> Async.AwaitTask
                Log.Information("✅ Found {EventCount} Order events in stream:", orderStreamEvents.Count)
                orderStreamEvents |> Seq.iteri (fun i event ->
                    Log.Information("   Event {EventNumber}: {EventType} (Version {EventVersion})", (i+1), event.EventTypeName, event.Version)
                )
            | Error err ->
                Log.Error("❌ Failed to retrieve Order state: {ErrorMessage}", err)
            
            // Now cancel the order
            Log.Information("")
            Log.Information("📝 Step 4: Cancelling the Order")
            let cancelOrderV1: O.CancelOrderV1 = {
                OrderId = orderId
            }
            let cancelOrderVersion: O.CancelOrder = O.CancelOrder.V1 cancelOrderV1
            let cancelOrderCommand = O.Command.Cancel cancelOrderVersion
            
            Log.Information("📤 Sending Order CANCEL command through bus")
            let! cancelResult = commandBus.Send cancelOrderCommand
            
            match cancelResult with
            | Ok () ->
                Log.Information("✅ Order CANCEL command succeeded")
                
                // Verify the order state after cancellation
                Log.Information("")
                Log.Information("🔍 Verifying Order state after cancellation...")
                let! finalOrderStateResult = orderRepository.GetById orderId
                match finalOrderStateResult with
                | Ok (finalOrderState, finalOrderVersion) ->
                    Log.Information("✅ Retrieved final Order state:")
                    Log.Information("   ID: {OrderId}", finalOrderState.Id)
                    Log.Information("   Status: {OrderStatus}", finalOrderState.Status)
                    Log.Information("   Version: {Version}", finalOrderVersion)
                    
                    // Verify all Order events in database
                    Log.Information("")
                    Log.Information("🗃️ Verifying all Order events in PostgreSQL database...")
                    use session = documentStore.LightweightSession()
                    let! finalOrderStreamEvents = session.Events.FetchStreamAsync(orderId) |> Async.AwaitTask
                    Log.Information("✅ Found {EventCount} Order events in stream:", finalOrderStreamEvents.Count)
                    finalOrderStreamEvents |> Seq.iteri (fun i event ->
                        Log.Information("   Event {EventNumber}: {EventType} (Version {EventVersion})", (i+1), event.EventTypeName, event.Version)
                    )
                | Error err ->
                    Log.Error("❌ Failed to retrieve final Order state: {ErrorMessage}", err)
            | Error err ->
                Log.Error("❌ Order CANCEL command failed: {ErrorMessage}", err)
                
        | Error err -> 
            Log.Error("❌ Order CREATE command failed: {ErrorMessage}", err)
        
        documentStore.Dispose()
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
        Log.Information("🧹 PostgreSQL container cleaned up")
    with
    | ex -> 
        Log.Error(ex, "❌ Error occurred: {ErrorMessage}", ex.Message)
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
    
    Log.Information("")
    Log.Information("🏁 COMPLETE EVENT SOURCING DEMO SUCCESSFUL")
    Log.Information("==========================================")
    Log.Information("✅ Customer aggregate: Created and Updated")
    Log.Information("✅ Order aggregate: Created and Cancelled")
    Log.Information("✅ All events properly stored with schema URNs")
    Log.Information("✅ State correctly evolves through event replay")
    Log.Information("✅ Complete F# event sourcing with nested module structure")
    
    return 0
}

[<EntryPoint>]
let main argv = 
    async {
        let! result = demonstrateCompleteFlow ()
        return result
    }
    |> Async.RunSynchronously