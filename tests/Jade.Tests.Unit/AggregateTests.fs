module AggregateTests

open System
open Expecto
open Jade.Core.EventSourcing

type TestCommand = 
    | Create of string
    | Update of string

type TestEvent =
    | Created of string
    | Updated of string

type TestState = {
    Id: Guid
    Value: string
}

let testAggregate = {
    create = fun cmd ->
        match cmd with
        | Create value -> Ok [Created value]
        | _ -> Error "Invalid create command"
    
    decide = fun cmd state ->
        match cmd with
        | Update value -> Ok [Updated value]
        | _ -> Error "Invalid command for existing state"
    
    evolve = fun state event ->
        match event with
        | Created value -> { state with Value = value }
        | Updated value -> { state with Value = value }
    
    init = fun event ->
        match event with
        | Created value -> { Id = Guid.NewGuid(); Value = value }
        | Updated value -> { Id = Guid.NewGuid(); Value = value }
}

[<Tests>]
let aggregateTests = 
    testList "Aggregate Tests" [
        testCase "evolve function chain builds state from events" <| fun _ ->
            let events = [Created "initial"; Updated "modified"]
            let finalState = rehydrate<TestCommand, TestEvent, TestState> testAggregate events
            
            Expect.equal finalState.Value "modified" "State should reflect final event"

        testCase "create function produces events" <| fun _ ->
            let result = testAggregate.create (Create "test")
            
            match result with
            | Ok events -> 
                Expect.equal events.Length 1 "Should produce one event"
                match events.[0] with
                | Created value -> Expect.equal value "test" "Event value should match"
                | _ -> Tests.failtest "Wrong event type"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "decide function processes commands on existing state" <| fun _ ->
            let state = { Id = Guid.NewGuid(); Value = "existing" }
            let result = testAggregate.decide (Update "new") state
            
            match result with
            | Ok events -> 
                Expect.equal events.Length 1 "Should produce one event"
                match events.[0] with
                | Updated value -> Expect.equal value "new" "Event value should match"
                | _ -> Tests.failtest "Wrong event type"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "evolve function applies events to state" <| fun _ ->
            let state = { Id = Guid.NewGuid(); Value = "old" }
            let newState = testAggregate.evolve state (Updated "new")
            
            Expect.equal newState.Value "new" "State should be updated"
            Expect.equal newState.Id state.Id "ID should remain the same"
    ]