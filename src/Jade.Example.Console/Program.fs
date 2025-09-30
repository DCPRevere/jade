open System
open Marten
open Testcontainers.PostgreSql
open Serilog
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Jade.Core.CommandBus
open Jade.Core.CommandRegistry
open Jade.Core.EventSourcing
open Jade.Marten.MartenRepository
module C = Customer
module O = Order
open Jade.Example.Domain.MartenConfiguration
open Jade.Example.Domain.Projections.CustomerView

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
        
        // Set up command registry and bus
        Log.Information("🚌 Setting up command registry and bus...")
        
        let logger = NullLogger<Registry>.Instance
        let jsonOptions = System.Text.Json.JsonSerializerOptions()
        jsonOptions.PropertyNamingPolicy <- System.Text.Json.JsonNamingPolicy.CamelCase
        jsonOptions.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())
        let registry = Registry(logger, jsonOptions)
        
        // Register Customer handler with commands
        let loggerFactory = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
        let customerLogger = loggerFactory.CreateLogger("Customer.Repository")
        let customerRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, C.State> customerLogger documentStore C.aggregate
        let handlerLogger = loggerFactory.CreateLogger("Customer.Handler")
        let customerHandler = createHandler handlerLogger customerRepository C.aggregate C.getId
        registry.register([
            typeof<C.Command.Create.V1>
            typeof<C.Command.Create.V2>
            typeof<C.Command.Update.V1>
        ], customerHandler)
        Log.Information("✅ Registered CUSTOMER command handler")
        
        // Register Order handler with commands
        let orderLogger = loggerFactory.CreateLogger("Order.Repository")
        let orderRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, O.State> orderLogger documentStore O.aggregate
        let orderHandlerLogger = loggerFactory.CreateLogger("Order.Handler")
        let orderHandler = createHandler orderHandlerLogger orderRepository O.aggregate O.getId
        registry.register([
            typeof<O.Command.Create.V1>
            typeof<O.Command.Create.V2>
            typeof<O.Command.Cancel.V1>
        ], orderHandler)
        Log.Information("✅ Registered ORDER command handler")
        
        let busLogger = NullLogger<CommandBus>.Instance
        let commandBus = CommandBus(registry.GetHandler, busLogger)
        Log.Information("✅ Command bus configured with 2 handlers")
        
        // Create and send commands
        let customerId = Guid.NewGuid()
        
        Log.Information("")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("PART 1: CUSTOMER COMMANDS")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("")
        Log.Information("📝 Step 1: Customer.Create.V1 (should produce Created.V2 event)")

        let createCommand = 
            { 
                CustomerId = customerId
                Name = "Alice F# User"
                Email = "alice@fsharp-demo.com"
            } : C.Command.Create.V1
        
        Log.Information("📤 Sending Customer.Create.V1 through bus (expecting Created.V2 event)")
        let! createResult = commandBus.Send createCommand
        
        match createResult with
        | Ok () -> 
            Log.Information("✅ Customer.Create.V1 command succeeded (produced V2 event)")
            Log.Information("🔍 Verifying state was persisted in Marten...")
            let! stateResult = customerRepository.GetById (customerId.ToString())
            match stateResult with
            | Ok (state, version) ->
                Log.Information("✅ Retrieved persisted state: {state}, {version}", state, version)
                
                Log.Information("")
                Log.Information("📝 Step 2: Customer.Update.V1")
                let updateCommand = 
                    {
                        CustomerId = customerId
                        Name = "Alice Updated via F#"
                        Email = "alice.updated@fsharp-demo.com"
                    } : C.Command.Update.V1
                
                Log.Information("📤 Sending Customer.Update.V1 through bus: {UpdateCommand}", updateCommand)
                let! updateResult = commandBus.Send updateCommand
                
                match updateResult with
                | Ok () ->
                    Log.Information("✅ Customer.Update.V1 command succeeded")
                    
                    // Verify final state
                    Log.Information("")
                    Log.Information("🔍 Verifying final state after update...")
                    let! finalStateResult = customerRepository.GetById (customerId.ToString())
                    match finalStateResult with
                    | Ok (finalState, finalVersion) ->
                        Log.Information("✅ Final persisted state: {state}, {version}", finalState, finalVersion)
                        
                        // Verify events in database
                        Log.Information("")
                        Log.Information("🗃️ Verifying events in PostgreSQL database...")
                        use session = documentStore.LightweightSession()
                        let customerStreamId = $"customer-{customerId}"
                        let! streamEvents = session.Events.FetchStreamAsync(customerStreamId) |> Async.AwaitTask
                        Log.Information("✅ Found {EventCount} events in stream {StreamId}:", streamEvents.Count, customerStreamId)
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
            Log.Error("❌ Customer.Create.V1 command failed: {ErrorMessage}", err)
        
        // Now test Order commands
        Log.Information("")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("PART 2: ORDER COMMANDS")
        Log.Information("{Separator}", String.replicate 60 "=")
        Log.Information("")
        
        let orderId = Guid.NewGuid()
        
        Log.Information("📝 Step 3: Order.Create.V2 (with optional promo code)")
        let orderItems: O.OrderItem list = [
            { ProductId = Guid.NewGuid(); Quantity = 2; Price = 29.99m }
            { ProductId = Guid.NewGuid(); Quantity = 1; Price = 49.99m }
        ]
        let createOrderCommand = 
            {
                OrderId = orderId
                CustomerId = customerId
                Items = orderItems
                PromoCode = Some "NESTED10"
            } : O.Command.Create.V2
        
        Log.Information("📤 Sending Order.Create.V2 through bus")
        let! orderCreateResult = commandBus.Send createOrderCommand
        
        match orderCreateResult with
        | Ok () -> 
            Log.Information("✅ Order.Create.V2 command succeeded")
            
            // Verify Order state was persisted
            Log.Information("")
            Log.Information("🔍 Verifying Order state was persisted in Marten...")
            let! orderStateResult = orderRepository.GetById (orderId.ToString())
            match orderStateResult with
            | Ok (orderState, orderVersion) ->
                Log.Information("✅ Retrieved persisted Order state: {orderState}, {orderVersion}", orderState, orderVersion)
                
                // Verify Order events in database
                Log.Information("")
                Log.Information("🗃️ Verifying Order events in PostgreSQL database...")
                use session = documentStore.LightweightSession()
                let orderStreamId = $"order-{orderId}"
                let! orderStreamEvents = session.Events.FetchStreamAsync(orderStreamId) |> Async.AwaitTask
                Log.Information("✅ Found {EventCount} Order events in stream {StreamId}:", orderStreamEvents.Count, orderStreamId)
                orderStreamEvents |> Seq.iteri (fun i event ->
                    Log.Information("   Event {EventNumber}: {EventType} (Version {EventVersion})", (i+1), event.EventTypeName, event.Version)
                )
            | Error err ->
                Log.Error("❌ Failed to retrieve Order state: {ErrorMessage}", err)
            
            // Now cancel the order
            Log.Information("")
            Log.Information("📝 Step 4: Cancelling the Order")
            let cancelOrderCommand = 
                {
                    OrderId = orderId
                    CustomerId = customerId
                } : O.Command.Cancel.V1
            
            Log.Information("📤 Sending Order CANCEL command through bus")
            let! cancelResult = commandBus.Send cancelOrderCommand
            
            match cancelResult with
            | Ok () ->
                Log.Information("✅ Order CANCEL command succeeded")
                
                // Verify the order state after cancellation
                Log.Information("")
                Log.Information("🔍 Verifying Order state after cancellation...")
                let! finalOrderStateResult = orderRepository.GetById (orderId.ToString())
                match finalOrderStateResult with
                | Ok (finalOrderState, finalOrderVersion) ->
                    Log.Information("✅ Retrieved final Order state: {finalOrderState}, {finalOrderVersion}", finalOrderState, finalOrderVersion)
                    
                    // Verify all Order events in database
                    Log.Information("")
                    Log.Information("🗃️ Verifying all Order events in PostgreSQL database...")
                    use session = documentStore.LightweightSession()
                    let orderStreamId = $"order-{orderId}"
                    let! finalOrderStreamEvents = session.Events.FetchStreamAsync(orderStreamId) |> Async.AwaitTask
                    Log.Information("✅ Found {EventCount} Order events in stream {StreamId}:", finalOrderStreamEvents.Count, orderStreamId)
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
        Log.Information("🔄 Querying CustomerView projection for customer {CustomerId}...", customerId)
        use session = documentStore.QuerySession()
        let! projection = session.LoadAsync<CustomerView>(customerId) |> Async.AwaitTask
        
        match box projection with
        | null ->
            Log.Warning("⚠️ CustomerView projection not found for customer {CustomerId}", customerId)
            
            let! allProjections = session.Query<CustomerView>().ToListAsync() |> Async.AwaitTask
            Log.Information("📋 Found {Count} CustomerView documents total", allProjections.Count)
            
        | _ ->
            Log.Information("✅ CustomerView projection found and built successfully:")
            Log.Information("   CustomerView: {cw}", projection)
        
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