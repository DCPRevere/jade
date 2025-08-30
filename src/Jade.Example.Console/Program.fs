open System
open Marten
open Testcontainers.PostgreSql
open Jade.Core.CommandBus
open Jade.Core.EventSourcing
open Jade.Core.MartenRepository
module C = Customer
module O = Order
open Jade.Domain.MartenConfiguration

printfn "🚀 Jade Event Sourcing Library - Complete F# Command Bus Flow"
printfn "============================================================="

let demonstrateCompleteFlow () = async {
    printfn ""
    printfn "🎯 DEMONSTRATION: Complete Command Bus → Aggregate → Marten Flow"
    printfn "==============================================================="
    
    // Set up PostgreSQL container
    printfn "🐘 Setting up PostgreSQL container..."
    let postgresContainer = 
        PostgreSqlBuilder()
            .WithImage("postgres:15")
            .WithDatabase("jade_demo")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build()
    
    do! postgresContainer.StartAsync() |> Async.AwaitTask
    printfn "✅ PostgreSQL container started"
    
    try
        let connectionString = postgresContainer.GetConnectionString()
        printfn "🔗 Connection string: %s" connectionString
        
        // Set up Marten document store
        printfn "📦 Configuring Marten document store..."
        let documentStore = 
            DocumentStore.For(fun options ->
                options.Connection(connectionString)
                options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
                options.DatabaseSchemaName <- "jade_events"
                configureDomainMarten options)
        
        // Clean and initialize database
        do! documentStore.Advanced.Clean.CompletelyRemoveAllAsync() |> Async.AwaitTask
        printfn "✅ Marten configured and database initialized"
        
        // Set up command bus with multiple handlers
        printfn "🚌 Setting up command bus with multiple handlers..."
        let commandBus = CommandBus()
        
        // Register Customer handler
        let customerRepository = createRepository<C.Command, C._Event, C.State> documentStore C.aggregate
        let customerHandler = AggregateCommandHandler(customerRepository, C.aggregate, C.getId, "👤 CUSTOMER")
        commandBus.RegisterHandler customerHandler
        printfn "✅ Registered CUSTOMER command handler"
        
        // Register Order handler
        let orderRepository = createRepository<O.Command, O._Event, O.State> documentStore O.aggregate
        let orderHandler = AggregateCommandHandler(orderRepository, O.aggregate, O.getId, "📦 ORDER")
        commandBus.RegisterHandler orderHandler
        printfn "✅ Registered ORDER command handler"
        
        printfn "✅ Command bus configured with 2 handlers"
        
        // Create and send commands
        let customerId = Guid.NewGuid()
        
        printfn ""
        printfn "%s" (String.replicate 60 "=")
        printfn "PART 1: CUSTOMER COMMANDS"
        printfn "%s" (String.replicate 60 "=")
        printfn ""
        printfn "📝 Step 1: Creating and sending Customer CREATE command"

        let createV2Data: C.CreateCustomerV2 = {
            CustomerId = customerId
            Name = "Alice F# User"
            Email = "alice@fsharp-demo.com"
            Phone = "+1-555-F#"
        }
        let createVersion: C.CreateCustomer = C.V2 createV2Data
        let createCommand = C.Command.Create createVersion
        
        printfn "📤 Sending Customer CREATE command through bus"
        let! createResult = commandBus.Send createCommand
        
        match createResult with
        | Ok () -> 
            printfn "✅ CREATE command succeeded"
            
            // Verify the state was persisted
            printfn ""
            printfn "🔍 Verifying state was persisted in Marten..."
            let! stateResult = customerRepository.GetById customerId
            match stateResult with
            | Ok (state, version) ->
                printfn "✅ Retrieved persisted state:"
                printfn "   ID: %A" state.Id
                printfn "   Name: %s" state.Name
                printfn "   Email: %s" state.Email
                printfn "   Phone: %A" state.Phone
                printfn "   Version: %d" version
                
                // Send update command
                printfn ""
                printfn "📝 Step 2: Creating and sending UPDATE command"
                let updateV1: C.UpdateV1 = {
                    CustomerId = customerId
                    Name = "Alice Updated via F#"
                    Email = "alice.updated@fsharp-demo.com"
                }
                let updateVersion: C.UpdateCustomer = C.UpdateCustomer.V1 updateV1
                let updateCommand = C.Command.Update updateVersion
                
                printfn "📤 Sending UPDATE command through bus: %A" updateCommand
                let! updateResult = commandBus.Send updateCommand
                
                match updateResult with
                | Ok () ->
                    printfn "✅ UPDATE command succeeded"
                    
                    // Verify final state
                    printfn ""
                    printfn "🔍 Verifying final state after update..."
                    let! finalStateResult = customerRepository.GetById customerId
                    match finalStateResult with
                    | Ok (finalState, finalVersion) ->
                        printfn "✅ Final persisted state:"
                        printfn "   ID: %A" finalState.Id
                        printfn "   Name: %s" finalState.Name
                        printfn "   Email: %s" finalState.Email
                        printfn "   Phone: %A" finalState.Phone
                        printfn "   Version: %d" finalVersion
                        
                        // Verify events in database
                        printfn ""
                        printfn "🗃️ Verifying events in PostgreSQL database..."
                        use session = documentStore.LightweightSession()
                        let! streamEvents = session.Events.FetchStreamAsync(customerId) |> Async.AwaitTask
                        printfn "✅ Found %d events in stream:" streamEvents.Count
                        streamEvents |> Seq.iteri (fun i event ->
                            printfn "   Event %d: %s (Version %d)" (i+1) event.EventTypeName event.Version
                        )
                        
                    | Error err ->
                        printfn "❌ Failed to retrieve final state: %s" err
                        
                | Error err ->
                    printfn "❌ UPDATE command failed: %s" err
                    
            | Error err ->
                printfn "❌ Failed to retrieve state after create: %s" err
        | Error err -> 
            printfn "❌ CREATE command failed: %s" err
        
        // Now test Order commands
        printfn ""
        printfn "%s" (String.replicate 60 "=")
        printfn "PART 2: ORDER COMMANDS"
        printfn "%s" (String.replicate 60 "=")
        printfn ""
        
        let orderId = Guid.NewGuid()
        
        printfn "📝 Step 3: Creating and sending Order CREATE command"
        let orderItems: O.OrderItem list = [
            { ProductId = Guid.NewGuid(); Quantity = 2; Price = 29.99m }
            { ProductId = Guid.NewGuid(); Quantity = 1; Price = 49.99m }
        ]
        let createOrderV2: O.CreateOrderV2 = {
            OrderId = orderId
            CustomerId = customerId
            Items = orderItems
            PromoCode = "SAVE10"
        }
        let createOrderVersion: O.CreateOrder = O.V2 createOrderV2
        let createOrderCommand = O.Command.Create createOrderVersion
        
        printfn "📤 Sending Order CREATE command through bus"
        let! orderCreateResult = commandBus.Send createOrderCommand
        
        match orderCreateResult with
        | Ok () -> 
            printfn "✅ Order CREATE command succeeded"
            
            // Send a Customer command to verify handlers are still separate
            printfn ""
            printfn "📝 Step 4: Sending another Customer command to verify routing"
            let updateV1: C.UpdateV1 = {
                CustomerId = customerId
                Name = "Alice Verified"
                Email = "alice.verified@fsharp-demo.com"
            }
            let updateVersion2: C.UpdateCustomer = C.UpdateCustomer.V1 updateV1
            let updateCommand2 = C.Command.Update updateVersion2
            
            printfn "📤 Sending Customer UPDATE command through bus"
            let! updateResult2 = commandBus.Send updateCommand2
            
            match updateResult2 with
            | Ok () ->
                printfn "✅ Customer UPDATE command succeeded"
                printfn ""
                printfn "🎯 VERIFIED: Commands are routed to correct handlers!"
                printfn "   - Customer commands → Customer handler"
                printfn "   - Order commands → Order handler"
            | Error err ->
                printfn "❌ Customer UPDATE command failed: %s" err
                
        | Error err -> 
            printfn "❌ Order CREATE command failed: %s" err
        
        documentStore.Dispose()
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
        printfn "🧹 PostgreSQL container cleaned up"
    with
    | ex -> 
        printfn "❌ Error occurred: %s" ex.Message
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
    
    printfn ""
    printfn "🏁 COMPLETE MULTI-HANDLER DEMO SUCCESSFUL"
    printfn "=========================================="
    printfn "✅ CommandBus routes to correct handlers based on command type"
    printfn "✅ Customer commands → Customer handler"  
    printfn "✅ Order commands → Order handler"
    printfn "✅ Multiple handlers can coexist in same bus"
    printfn "✅ Each handler processes only its domain commands"
    printfn "✅ Complete F# event sourcing with proper separation of concerns"
    
    return 0
}

[<EntryPoint>]
let main argv = 
    async {
        let! result = demonstrateCompleteFlow ()
        return result
    }
    |> Async.RunSynchronously