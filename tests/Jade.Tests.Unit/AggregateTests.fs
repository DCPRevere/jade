module AggregateTests

open System
open Expecto
open Jade.Core.EventSourcing

type TestCommand = 
    | Create of string
    | Update of string

type TestCreated = { Value: string } with interface IEvent
type TestUpdated = { Value: string } with interface IEvent

type TestState = {
    Id: Guid
    Value: string
}

let testAggregate : Aggregate<TestCommand, IEvent, TestState> = {
    prefix = "test"
    create = fun cmd ->
        match cmd with
        | Create value -> Ok [({ TestCreated.Value = value } : TestCreated) :> IEvent]
        | _ -> Error "Invalid create command"
    
    decide = fun cmd state ->
        match cmd with
        | Update value -> Ok [{ TestUpdated.Value = value } :> IEvent]
        | _ -> Error "Invalid command for existing state"
    
    evolve = fun state event ->
        match event with
        | :? TestCreated as e -> { state with Value = e.Value }
        | :? TestUpdated as e -> { state with Value = e.Value }
        | _ -> state
    
    init = fun event ->
        match event with
        | :? TestCreated as e -> { Id = Guid.NewGuid(); Value = e.Value }
        | :? TestUpdated as e -> { Id = Guid.NewGuid(); Value = e.Value }
        | _ -> { Id = Guid.NewGuid(); Value = "" }
}

[<Tests>]
let aggregateTests = 
    testList "Aggregate Tests" [
        testCase "evolve function chain builds state from events" <| fun _ ->
            let events : IEvent list = [{ TestCreated.Value = "initial" } :> IEvent; { TestUpdated.Value = "modified" } :> IEvent]
            let result = rehydrate<TestCommand, IEvent, TestState> testAggregate events
            
            match result with
            | Ok finalState -> Expect.equal finalState.Value "modified" "State should reflect final event"
            | Error err -> Tests.failtest $"Rehydrate failed: {err}"

        testCase "create function produces events" <| fun _ ->
            let result = testAggregate.create (Create "test")
            
            match result with
            | Ok events -> 
                Expect.equal events.Length 1 "Should produce one event"
                match events.[0] with
                | :? TestCreated as e -> Expect.equal e.Value "test" "Event value should match"
                | _ -> Tests.failtest "Wrong event type"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "decide function processes commands on existing state" <| fun _ ->
            let state = { Id = Guid.NewGuid(); Value = "existing" }
            let result = testAggregate.decide (Update "new") state
            
            match result with
            | Ok events -> 
                Expect.equal events.Length 1 "Should produce one event"
                match events.[0] with
                | :? TestUpdated as e -> Expect.equal e.Value "new" "Event value should match"
                | _ -> Tests.failtest "Wrong event type"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "evolve function applies events to state" <| fun _ ->
            let state = { Id = Guid.NewGuid(); Value = "old" }
            let newState = testAggregate.evolve state ({ TestUpdated.Value = "new" } :> IEvent)
            
            Expect.equal newState.Value "new" "State should be updated"
            Expect.equal newState.Id state.Id "ID should remain the same"
    ]