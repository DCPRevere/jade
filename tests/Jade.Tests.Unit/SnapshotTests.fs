module SnapshotTests

open System
open Expecto
open Jade.Core.Snapshots

type TestState = { Value: int }

[<Tests>]
let snapshotTests = 
    testList "Snapshot Tests" [
        testCase "createSnapshot creates valid snapshot" <| fun _ ->
            let aggregateId = Guid.NewGuid()
            let state = { Value = 42 }
            let version = 10L
            
            let snapshot = createSnapshot aggregateId state version
            
            Expect.equal snapshot.AggregateId aggregateId "AggregateId should match"
            Expect.equal snapshot.State state "State should match"
            Expect.equal snapshot.Version version "Version should match"
            Expect.isTrue (snapshot.Timestamp <= DateTimeOffset.UtcNow) "Timestamp should be recent"

        testCase "shouldTakeSnapshot EveryNEvents works correctly" <| fun _ ->
            let strategy = EveryNEvents 5
            let shouldSnapshot1 = shouldTakeSnapshot strategy 5L (DateTimeOffset.UtcNow.AddMinutes(-1.0))
            let shouldSnapshot2 = shouldTakeSnapshot strategy 7L (DateTimeOffset.UtcNow.AddMinutes(-1.0))
            
            Expect.isTrue shouldSnapshot1 "Should snapshot at version 5"
            Expect.isFalse shouldSnapshot2 "Should not snapshot at version 7"

        testCase "shouldTakeSnapshot EveryNMinutes works correctly" <| fun _ ->
            let strategy = EveryNMinutes 30
            let oldTime = DateTimeOffset.UtcNow.AddMinutes(-45.0)
            let recentTime = DateTimeOffset.UtcNow.AddMinutes(-15.0)
            
            let shouldSnapshot1 = shouldTakeSnapshot strategy 10L oldTime
            let shouldSnapshot2 = shouldTakeSnapshot strategy 10L recentTime
            
            Expect.isTrue shouldSnapshot1 "Should snapshot when time threshold exceeded"
            Expect.isFalse shouldSnapshot2 "Should not snapshot when time threshold not exceeded"

        testCase "shouldTakeSnapshot Custom predicate works correctly" <| fun _ ->
            let customPredicate version lastTime = version > 100L
            let strategy = Custom customPredicate
            
            let shouldSnapshot1 = shouldTakeSnapshot strategy 150L DateTimeOffset.UtcNow
            let shouldSnapshot2 = shouldTakeSnapshot strategy 50L DateTimeOffset.UtcNow
            
            Expect.isTrue shouldSnapshot1 "Should snapshot when custom predicate returns true"
            Expect.isFalse shouldSnapshot2 "Should not snapshot when custom predicate returns false"
    ]