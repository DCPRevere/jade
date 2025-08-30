module MartenRepositoryTests

open Expecto
open Jade.Core.MartenRepository
open Jade.Core.MartenConfiguration
open Jade.Core.EventSourcing
open Marten
open System
open Testcontainers.PostgreSql

type TestCreated = { Id: Guid; Name: string }
type TestUpdated = { Id: Guid; NewName: string }

type TestEvent =
    | Created of TestCreated
    | Updated of TestUpdated

type TestState = {
    Id: Guid
    Name: string
    UpdateCount: int
}

let init = function
    | Created e -> { Id = e.Id; Name = e.Name; UpdateCount = 0 }
    | Updated e -> { Id = e.Id; Name = e.NewName; UpdateCount = 1 }

let evolve state = function
    | Created e -> { Id = e.Id; Name = e.Name; UpdateCount = 0 }
    | Updated e -> { state with Name = e.NewName; UpdateCount = state.UpdateCount + 1 }

let createTestStore () = async {
    let builder = PostgreSqlBuilder()
    builder.WithImage("postgres:15") |> ignore
    builder.WithDatabase("test_db") |> ignore
    builder.WithUsername("postgres") |> ignore
    builder.WithPassword("postgres") |> ignore
    builder.WithCleanUp(true) |> ignore
    let container = builder.Build()
        
    do! container.StartAsync() |> Async.AwaitTask
    let connectionString = container.GetConnectionString()
    
    let store = Marten.DocumentStore.For(fun options ->
        options.Connection(connectionString)
        options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
        options.DatabaseSchemaName <- "test_events"
        configureMartenBase options
    )
    
    return (store, container)
}

[<Tests>]
let martenRepositoryTests =
    testList "MartenRepository Tests" [
        
        testCaseAsync "GetById returns error when aggregate doesn't exist" <| async {
            let! (store, container) = createTestStore()
            try
                let repository = MartenAggregateRepository(store, init, evolve) :> IAggregateRepository<TestState, TestEvent>
                let aggregateId = Guid.NewGuid()
                
                let! result = repository.GetById aggregateId
                
                Expect.isError result "Should return error when aggregate not found"
                match result with
                | Error msg -> Expect.stringContains msg "not found" "Error should mention not found"
                | Ok _ -> failwith "Should not succeed"
            finally
                store.Dispose()
                container.DisposeAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously
        }
        
        testCaseAsync "Save new aggregate (version 0) creates stream" <| async {
            let! (store, container) = createTestStore()
            try
                let repository = MartenAggregateRepository(store, init, evolve) :> IAggregateRepository<TestState, TestEvent>
                let aggregateId = Guid.NewGuid()
                let events = [ Created { Id = aggregateId; Name = "Test Name" } ]
                
                let! saveResult = repository.Save aggregateId events 0L
                
                Expect.isOk saveResult "Should successfully save new aggregate"
            finally
                store.Dispose()
                container.DisposeAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously
        }
    ]