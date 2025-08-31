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
open Jade.Domain.Projections.CustomerWithOrders

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
        
        // Set up Marten document store with async daemon enabled
        Log.Information("📦 Configuring Marten document store...")
        let documentStore = 
            DocumentStore.For(fun options ->
                options.Connection(connectionString)
                options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
                options.DatabaseSchemaName <- "jade_events"
                
                // Enable async daemon for projections
                // options.Projections.AsyncMode <- JasperFx.Events.Daemon.DaemonMode.Solo
                
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
        Log.Information("📝 Step 1: Customer.Create.V2")

        let createV2Data: C.CreateCustomerV2 = {
            CustomerId = customerId
            Name = "Alice F# User"
            Email = "alice@fsharp-demo.com"
            Phone = "+1-555-F#"
        }
        let createVersion: C.CreateCustomer = C.V2 createV2Data
        let createCommand = C.Command.Create createVersion
        
        Log.Information("📤 Sending Customer.Create.V2 through bus")
        let! createResult = commandBus.Send createCommand
        
        match createResult with
        | Ok () -> 
            Log.Information("✅ Customer.Create.V2 command succeeded")
            Log.Information("🔍 Verifying state was persisted in Marten...")
            let! stateResult = customerRepository.GetById customerId
            match stateResult with
            | Ok (state, version) ->
                Log.Information("✅ Retrieved persisted state: {state}, {version}", state, version)
                
                Log.Information("")
                Log.Information("📝 Step 2: Customer.Update.V1")
                let updateV1: C.UpdateV1 = {
                    CustomerId = customerId
                    Name = "Alice Updated via F#"
                    Email = "alice.updated@fsharp-demo.com"
                }
                let updateVersion: C.UpdateCustomer = C.UpdateCustomer.V1 updateV1
                let updateCommand = C.Command.Update updateVersion
                
                Log.Information("📤 Sending Customer.Update.V1 through bus: {UpdateCommand}", updateCommand)
                let! updateResult = commandBus.Send updateCommand
                
                match updateResult with
                | Ok () ->
                    Log.Information("✅ Customer.Update.V1 command succeeded")
                    
                    // Verify final state
                    Log.Information("")
                    Log.Information("🔍 Verifying final state after update...")
                    let! finalStateResult = customerRepository.GetById customerId
                    match finalStateResult with
                    | Ok (finalState, finalVersion) ->
                        Log.Information("✅ Final persisted state: {state}, {version}", finalState, finalVersion)
                        
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
                    Log.Error("❌ Customer.Update.V1 command failed: {ErrorMessage}", err)
                    
            | Error err ->
                Log.Error("❌ Failed to retrieve state after create: {ErrorMessage}", err)
        | Error err -> 
            Log.Error("❌ Customer.Create.V2 command failed: {ErrorMessage}", err)
        
        // Now test Order commands
        Log.Information("")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("PART 2: ORDER COMMANDS")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("")
        
        let orderId = Guid.NewGuid()
        
        Log.Information("📝 Step 3: Order.Create.V2")
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
        
        Log.Information("📤 Sending Order.Create.V2 through bus")
        let! orderCreateResult = commandBus.Send createOrderCommand
        
        match orderCreateResult with
        | Ok () -> 
            Log.Information("✅ Order.Create.V2 command succeeded")
            
            // Verify Order state was persisted
            Log.Information("")
            Log.Information("🔍 Verifying Order state was persisted in Marten...")
            let! orderStateResult = orderRepository.GetById orderId
            match orderStateResult with
            | Ok (orderState, orderVersion) ->
                Log.Information("✅ Retrieved persisted Order state: {orderState}, {orderVersion}", orderState, orderVersion)
                
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
                CustomerId = customerId
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
                    Log.Information("✅ Retrieved final Order state: {finalOrderState}, {finalOrderVersion}", finalOrderState, finalOrderVersion)
                    
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
            Log.Error("❌ Order.Create.V2 command failed: {ErrorMessage}", err)
        
        // Test the CustomerWithOrders projection
        Log.Information("")
        Log.Information("============================================================")
        Log.Information("PART 3: ASYNC PROJECTION TESTING")
        Log.Information("============================================================")
        Log.Information("")
        
        // Event projection should already be built because it is inline
        
        // Query the projection
        Log.Information("🔄 Querying CustomerWithOrders projection for customer {CustomerId}...", customerId)
        use session = documentStore.QuerySession()
        let! projection = session.LoadAsync<CustomerWithOrders>(customerId) |> Async.AwaitTask
        
        match box projection with
        | null ->
            Log.Warning("⚠️ CustomerWithOrders projection not found for customer {CustomerId}", customerId)
            
            let! allProjections = session.Query<CustomerWithOrders>().ToListAsync() |> Async.AwaitTask
            Log.Information("📋 Found {Count} CustomerWithOrders documents total", allProjections.Count)
            
        | _ ->
            Log.Information("✅ CustomerWithOrders projection found and built successfully:")
            Log.Information("   CustomerWithOrders: {cw}", projection)
        
        documentStore.Dispose()
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
        Log.Information("🧹 PostgreSQL container cleaned up")
    with
    | ex -> 
        Log.Error(ex, "❌ Error occurred: {ErrorMessage}", ex.Message)
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
    
    return 0
}

[<EntryPoint>]
let main argv = 
    async {
        let! result = demonstrateCompleteFlow ()
        return result
    }
    |> Async.RunSynchronously