module EventMetadataTests

open System
open Expecto
open Jade.Core.EventMetadata

type TestEvent = { Message: string }

[<Tests>]
let eventMetadataTests = 
    testList "Event Metadata Tests" [
        testCase "createEventMetadata generates valid metadata" <| fun _ ->
            let correlationId = Some (Guid.NewGuid())
            let causationId = Some (Guid.NewGuid())
            let userId = Some "test-user"
            let schemaVersion = Some 2
            let metadata = Some (Map.ofList [("key", "value")])
            
            let result = createEventMetadata correlationId causationId userId schemaVersion metadata
            
            Expect.equal result.CorrelationId correlationId.Value "CorrelationId should match"
            Expect.equal result.CausationId causationId.Value "CausationId should match"
            Expect.equal result.UserId userId "UserId should match"
            Expect.equal result.SchemaVersion 2 "SchemaVersion should match"
            Expect.equal result.Metadata metadata.Value "Metadata should match"
            Expect.notEqual result.EventId Guid.Empty "EventId should be generated"

        testCase "createEventMetadata uses defaults for None values" <| fun _ ->
            let result = createEventMetadata None None None None None
            
            Expect.notEqual result.EventId Guid.Empty "EventId should be generated"
            Expect.notEqual result.CorrelationId Guid.Empty "CorrelationId should be generated"
            Expect.notEqual result.CausationId Guid.Empty "CausationId should be generated"
            Expect.equal result.SchemaVersion 1 "SchemaVersion should default to 1"
            Expect.isNone result.UserId "UserId should be None"
            Expect.equal result.Metadata Map.empty "Metadata should be empty"

        testCase "wrapEventWithMetadata creates proper wrapper" <| fun _ ->
            let event = { Message = "test" }
            let metadata = createEventMetadata None None None None None
            
            let wrapper = wrapEventWithMetadata event metadata
            
            Expect.equal wrapper.Event event "Event should match"
            Expect.equal wrapper.Metadata metadata "Metadata should match"
    ]