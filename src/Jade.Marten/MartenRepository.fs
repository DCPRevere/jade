module Jade.Marten.MartenRepository

open System
open Marten
open Microsoft.Extensions.Logging
open Jade.Core.EventSourcing

type MartenRepository<'Command, 'State, 'Event when 'State: not struct and 'Event :> IEvent> (logger: ILogger, store: IDocumentStore, aggregate: Aggregate<'Command, 'Event, 'State>) =
    
    let createStreamId (aggregateId: string) =
        $"{aggregate.prefix}-{aggregateId}"
    
    interface IRepository<'State, 'Event> with
        member this.Save aggregateId events expectedVersion = async {
            try
                logger.LogDebug("Starting Save operation for aggregate {AggregateId} with expected version {ExpectedVersion} and {EventCount} events", aggregateId, expectedVersion, events.Length)
                
                use session = store.LightweightSession()
                let streamId = createStreamId aggregateId
                logger.LogDebug("Created stream ID {StreamId} for aggregate {AggregateId}", streamId, aggregateId)
                
                let eventArray = events |> List.map box |> List.toArray
                logger.LogTrace("Converted {EventCount} events to array", eventArray.Length)
                for i, evt in eventArray |> Array.indexed do
                    let evtType = if isNull evt then "NULL" else evt.GetType().Name
                    logger.LogTrace("Event [{Index}] type: {EventType}", i, evtType)
                
                if expectedVersion = 0L then
                    logger.LogDebug("Starting new event stream {StreamId}", streamId)
                    
                    let streamAction = session.Events.StartStream (streamId, eventArray)
                    logger.LogDebug("Successfully started event stream {StreamId}", streamId)
                else
                    logger.LogDebug("Appending events to existing stream {StreamId}", streamId)
                    session.Events.Append (streamId, eventArray) |> ignore
                    logger.LogDebug("Successfully appended events to stream {StreamId}", streamId)
                    
                logger.LogDebug("Saving changes to event store")
                do! session.SaveChangesAsync() |> Async.AwaitTask
                logger.LogInformation("Successfully saved {EventCount} events for aggregate {AggregateId}", events.Length, aggregateId)
                return Ok ()
            with
            | ex -> 
                logger.LogError(ex, "Failed to save events for aggregate {AggregateId}", aggregateId)
                return Error ex.Message
        }
        
        member this.GetById aggregateId = async {
            try
                logger.LogDebug("Starting GetById operation for aggregate {AggregateId}", aggregateId)
                use session = store.LightweightSession()
                let streamId = createStreamId aggregateId
                logger.LogDebug("Created stream ID {StreamId} for GetById operation", streamId)
                
                let! events = session.Events.FetchStreamAsync streamId |> Async.AwaitTask
                logger.LogDebug("Fetched {EventCount} events from stream {StreamId}", events.Count, streamId)
                
                if events.Count = 0 then
                    logger.LogDebug("No events found for aggregate {AggregateId} in stream {StreamId}", aggregateId, streamId)
                    return Error $"Aggregate {aggregateId} not found in stream {streamId}"
                else
                    let typedEvents = 
                        events 
                        |> Seq.map (fun e -> 
                            try
                                e.Data :?> 'Event
                            with
                            | :? System.InvalidCastException ->
                                let eventType = if e.Data <> null then e.Data.GetType().Name else "null"
                                logger.LogError("Failed to cast event data to expected type. Event type: {EventType}, Expected: {ExpectedType}", eventType, typeof<'Event>.Name)
                                reraise()
                        ) 
                        |> Seq.toList
                    logger.LogDebug("Mapped {EventCount} events to typed events", typedEvents.Length)
                    
                    match rehydrate aggregate typedEvents with
                    | Ok finalState ->
                        let version = events |> Seq.last |> fun e -> e.Version
                        logger.LogInformation("Successfully rehydrated aggregate {AggregateId} to version {Version}", aggregateId, version)
                        return Ok (finalState, version)
                    | Error err -> 
                        logger.LogError("Failed to rehydrate aggregate {AggregateId}: {Error}", aggregateId, err)
                        return Error err
            with
            | ex -> 
                logger.LogError(ex, "Exception in GetById operation for aggregate {AggregateId}", aggregateId)
                return Error ex.Message
        }

/// Helper to create a MartenRepository from an Aggregate
let createRepository<'Command, 'Event, 'State when 'State: not struct and 'Event :> IEvent>
    (logger: ILogger)
    (store: IDocumentStore)
    (aggregate: Aggregate<'Command, 'Event, 'State>) : IRepository<'State, 'Event> =
    MartenRepository<'Command, 'State, 'Event> (logger, store, aggregate) :> IRepository<'State, 'Event>

