module MartenRepositoryTests

open Expecto
open Jade.Core
open Jade.Marten.MartenRepository
open Jade.Marten.MartenConfiguration
open Jade.Core.EventSourcing
open Marten
open System
open Testcontainers.PostgreSql

type TestCreated = {
    Id: Guid
    Name: string
    Metadata: Metadata option
} with
    interface IEvent with
        member this.Metadata = this.Metadata

type TestUpdated = {
    Id: Guid
    NewName: string
    Metadata: Metadata option
} with
    interface IEvent with
        member this.Metadata = this.Metadata

type TestState = {
    Id: Guid
    Name: string
    UpdateCount: int
}

let init (event: IEvent) =
    match event with
    | :? TestCreated as e -> { Id = e.Id; Name = e.Name; UpdateCount = 0 }
    | :? TestUpdated as e -> { Id = e.Id; Name = e.NewName; UpdateCount = 1 }
    | _ -> failwithf "Unknown event type: %A" event

let evolve state (event: IEvent) =
    match event with
    | :? TestCreated as e -> { Id = e.Id; Name = e.Name; UpdateCount = 0 }
    | :? TestUpdated as e -> { state with Name = e.NewName; UpdateCount = state.UpdateCount + 1 }
    | _ -> failwithf "Unknown event type: %A" event

type TestCommand = 
    | Create of TestCreated
    | Update of TestUpdated

let testAggregate : Aggregate<TestCommand, IEvent, TestState> = {
    prefix = "test"
    create = fun cmd ->
        match cmd with
        | Create e -> Ok [e :> IEvent]
        | Update _ -> Error "Cannot update non-existing aggregate"
    
    decide = fun cmd state ->
        match cmd with
        | Create _ -> Error "Cannot create existing aggregate"
        | Update e -> Ok [e :> IEvent]
    
    evolve = evolve
    init = init
}

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
    
    let jsonOptions = System.Text.Json.JsonSerializerOptions()
    jsonOptions.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())

    let store = Marten.DocumentStore.For(fun options ->
        options.Connection(connectionString)
        options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
        options.DatabaseSchemaName <- "test_events"
        configureMartenBase jsonOptions options
    )
    
    return (store, container)
}

[<Tests>]
let martenRepositoryTests =
    testList "MartenRepository Tests" [
        
        testCaseAsync "GetById returns error when aggregate doesn't exist" <| async {
            let! (store, container) = createTestStore()
            try
                let logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<MartenRepository<TestCommand, TestState, IEvent>>.Instance
                let repository = MartenRepository(logger, store, testAggregate) :> IRepository<TestState, IEvent>
                let aggregateId = Guid.NewGuid().ToString()
                
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
                let logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<MartenRepository<TestCommand, TestState, IEvent>>.Instance
                let repository = MartenRepository(logger, store, testAggregate) :> IRepository<TestState, IEvent>
                let aggregateId = Guid.NewGuid().ToString()
                let events : IEvent list = [ { Id = Guid.Parse(aggregateId); Name = "Test Name"; Metadata = None } :> IEvent ]
                
                let! saveResult = repository.Save aggregateId events 0L
                
                Expect.isOk saveResult "Should successfully save new aggregate"
            finally
                store.Dispose()
                container.DisposeAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously
        }
    ]