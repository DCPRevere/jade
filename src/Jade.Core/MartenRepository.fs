module Jade.Core.MartenRepository

open System
open Marten
open Serilog
open Jade.Core.EventSourcing

type MartenAggregateRepository<'Command, 'State, 'Event when 'State: not struct and 'Event :> IEvent> (store: IDocumentStore, aggregate: Aggregate<'Command, 'Event, 'State>) =
    interface IAggregateRepository<'State, 'Event> with
        member this.Save aggregateId events expectedVersion = async {
            try
                use session = store.LightweightSession()
                
                let eventArray = events |> List.map box |> List.toArray
                Log.Debug("REPOSITORY: Storing {EventCount} events", eventArray.Length)
                
                if expectedVersion = 0L then
                    session.Events.StartStream<'State> (aggregateId, events |> List.map box |> List.toArray) |> ignore
                else
                    session.Events.Append (aggregateId, events |> List.map box |> List.toArray) |> ignore
                    
                do! session.SaveChangesAsync() |> Async.AwaitTask
                return Ok ()
            with
            | ex -> 
                return Error ex.Message
        }
        
        member this.GetById aggregateId = async {
            try
                use session = store.LightweightSession()
                let! events = session.Events.FetchStreamAsync aggregateId |> Async.AwaitTask
                
                if events.Count = 0 then
                    return Error $"Aggregate {aggregateId} not found"
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

/// Helper to create a MartenAggregateRepository from an Aggregate
let createRepository<'Command, 'Event, 'State when 'State: not struct and 'Event :> IEvent> 
    store 
    (aggregate: Aggregate<'Command, 'Event, 'State>) : IAggregateRepository<'State, 'Event> =
    MartenAggregateRepository<'Command, 'State, 'Event> (store, aggregate) :> IAggregateRepository<'State, 'Event>