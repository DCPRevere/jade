module Jade.Core.MartenRepository

open System
open Marten
open Jade.Core.EventSourcing

type MartenAggregateRepository<'State, 'Event when 'State: not struct> (store: IDocumentStore, init: 'Event -> 'State, evolve: 'State -> 'Event -> 'State) =
    interface IAggregateRepository<'State, 'Event> with
        member this.Save aggregateId events expectedVersion = async {
            try
                printfn "💾 Marten saving %d event(s) for aggregate %A at version %d" events.Length aggregateId expectedVersion
                use session = store.LightweightSession()
                
                if expectedVersion = 0L then
                    session.Events.StartStream<'State> (aggregateId, events |> List.map box |> List.toArray) |> ignore
                else
                    session.Events.Append (aggregateId, events |> List.map box |> List.toArray) |> ignore
                    
                do! session.SaveChangesAsync() |> Async.AwaitTask
                printfn "✅ Events persisted successfully to Marten"
                return Ok ()
            with
            | ex -> 
                printfn "❌ Failed to save events: %s" ex.Message
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
                    let initialState = init typedEvents.Head
                    let finalState = typedEvents.Tail |> List.fold evolve initialState
                    let version = events |> Seq.last |> fun e -> e.Version
                    return Ok (finalState, version)
            with
            | ex -> return Error ex.Message
        }

/// Helper to create a MartenAggregateRepository from an Aggregate
let createRepository<'Command, 'Event, 'State when 'State: not struct> 
    store 
    (aggregate: Aggregate<'Command, 'Event, 'State>) : IAggregateRepository<'State, 'Event> =
    MartenAggregateRepository<'State, 'Event> (store, aggregate.init, aggregate.evolve) :> IAggregateRepository<'State, 'Event>