module Jade.Core.MartenRepository

open System
open Marten
open Serilog
open Jade.Core.EventSourcing

type MartenRepository<'Command, 'State, 'Event when 'State: not struct and 'Event :> IEvent> (store: IDocumentStore, aggregate: Aggregate<'Command, 'Event, 'State>) =
    
    let createStreamId (aggregateId: Guid) =
        $"{aggregate.prefix}-{aggregateId}"
    
    interface IRepository<'State, 'Event> with
        member this.Save aggregateId events expectedVersion = async {
            try
                use session = store.LightweightSession()
                let streamId = createStreamId aggregateId
                
                let eventArray = events |> List.map box |> List.toArray
                Log.Debug("REPOSITORY: Storing {EventCount} events to stream {StreamId}", eventArray.Length, streamId)
                
                if expectedVersion = 0L then
                    session.Events.StartStream<'State> (streamId, eventArray) |> ignore
                else
                    session.Events.Append (streamId, eventArray) |> ignore
                    
                do! session.SaveChangesAsync() |> Async.AwaitTask
                return Ok ()
            with
            | ex -> 
                return Error ex.Message
        }
        
        member this.GetById aggregateId = async {
            try
                use session = store.LightweightSession()
                let streamId = createStreamId aggregateId
                let! events = session.Events.FetchStreamAsync streamId |> Async.AwaitTask
                
                if events.Count = 0 then
                    return Error $"Aggregate {aggregateId} not found in stream {streamId}"
                else
                    let typedEvents = events |> Seq.map (fun e -> e.Data :?> 'Event) |> Seq.toList
                    
                    match rehydrate aggregate typedEvents with
                    | Ok finalState ->
                        let version = events |> Seq.last |> fun e -> e.Version
                        return Ok (finalState, version)
                    | Error err -> return Error err
            with
            | ex -> return Error ex.Message
        }

/// Helper to create a MartenRepository from an Aggregate
let createRepository<'Command, 'Event, 'State when 'State: not struct and 'Event :> IEvent> 
    store 
    (aggregate: Aggregate<'Command, 'Event, 'State>) : IRepository<'State, 'Event> =
    MartenRepository<'Command, 'State, 'Event> (store, aggregate) :> IRepository<'State, 'Event>