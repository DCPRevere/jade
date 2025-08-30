module Jade.Core.EventSourcing

open System

/// Unique identifier for an aggregate
type AggregateId = Guid

/// Result of processing a command
type CommandResult<'Event> = {
    AggregateId: AggregateId
    Events: 'Event list
    Version: int64
}

/// Repository for loading and saving aggregates
type IAggregateRepository<'State, 'Event> =
    abstract member GetById: AggregateId -> Async<Result<'State * int64, string>>
    abstract member Save: AggregateId -> 'Event list -> int64 -> Async<Result<unit, string>>

/// Handler for processing commands
type ICommandHandler<'Command> =
    abstract member Handle: 'Command -> Async<Result<unit, string>>

/// Idiomatic F# aggregate pattern using record of functions
type Aggregate<'Command, 'Event, 'State> = {
    /// Create a new aggregate from a command (when aggregate doesn't exist)
    create: 'Command -> Result<'Event list, string>
    
    /// Decide what events to produce from a command on existing state
    decide: 'Command -> 'State -> Result<'Event list, string>
    
    /// Evolve state by applying an event
    evolve: 'State -> 'Event -> 'State
    
    /// Initialize state from the first event
    init: 'Event -> 'State
}

/// Helper to build aggregate state from events  
let rehydrate<'Command, 'Event, 'State> (aggregate: Aggregate<'Command, 'Event, 'State>) (events: 'Event list) =
    match events with
    | [] -> failwith "Cannot rehydrate from empty event list"
    | firstEvent :: remainingEvents -> 
        let initialState = aggregate.init firstEvent
        remainingEvents |> List.fold aggregate.evolve initialState

/// Helper to process commands using aggregate pattern
let processCommand<'Command, 'Event, 'State> 
    (repository: IAggregateRepository<'State, 'Event>)
    (aggregate: Aggregate<'Command, 'Event, 'State>)
    (getId: 'Command -> AggregateId)
    (command: 'Command) = async {
    
    let aggregateId = getId command
    let! existingResult = repository.GetById aggregateId
    
    match existingResult with
    | Error _ ->
        // Aggregate doesn't exist, try to create
        match aggregate.create command with
        | Ok events ->
            let! saveResult = repository.Save aggregateId events 0L
            match saveResult with
            | Ok () -> return Ok ()
            | Error err -> return Error ("Failed to save new aggregate: " + err)
        | Error err -> return Error err
        
    | Ok (state, currentVersion) ->
        // Aggregate exists, decide on existing state
        match aggregate.decide command state with
        | Ok events ->
            let! saveResult = repository.Save aggregateId events currentVersion
            match saveResult with
            | Ok () -> return Ok ()
            | Error err -> return Error ("Failed to save aggregate: " + err)
        | Error err -> return Error err
}

/// Generic aggregate command handler
type AggregateCommandHandler<'Command, 'Event, 'State>
    (repository: IAggregateRepository<'State, 'Event>, 
     aggregate: Aggregate<'Command, 'Event, 'State>,
     getId: 'Command -> AggregateId,
     handlerName: string) =
    
    interface ICommandHandler<'Command> with
        member this.Handle command = async {
            printfn "%s Handler received command: %A" handlerName command
            let! result = processCommand repository aggregate getId command
            
            match result with
            | Ok () -> printfn "✅ %s Handler processed command successfully" handlerName
            | Error err -> printfn "❌ %s Handler failed: %s" handlerName err
            
            return result
        }