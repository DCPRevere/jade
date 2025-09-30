module Jade.Core.EventSourcing

open System
open Microsoft.Extensions.Logging

/// Base interface for all domain events
type IEvent = interface end

/// Base interface for all domain commands  
type ICommand = interface end

/// Unique identifier for an aggregate
type AggregateId = string

/// Result of processing a command
type CommandResult<'Event> = {
    AggregateId: AggregateId
    Events: 'Event list
    Version: int64
}

/// Repository for loading and saving aggregates
type IRepository<'State, 'Event when 'Event :> IEvent> =
    abstract member GetById: AggregateId -> Async<Result<'State * int64, string>>
    abstract member Save: AggregateId -> 'Event list -> int64 -> Async<Result<unit, string>>

/// Handler for processing commands
type ICommandHandler<'Command> =
    abstract member Handle: 'Command -> Async<Result<unit, string>>

/// Idiomatic F# aggregate pattern using record of functions
type Aggregate<'Command, 'Event, 'State when 'Event :> IEvent> = {
    /// Stream prefix to distinguish aggregate types (e.g., "customer", "order")
    prefix: string
    
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
let rehydrate<'Command, 'Event, 'State when 'Event :> IEvent> (aggregate: Aggregate<'Command, 'Event, 'State>) (events: 'Event list) : Result<'State, string> =
    match events with
    | [] -> Error "Cannot rehydrate from empty event list"
    | firstEvent :: remainingEvents -> 
        let initialState = aggregate.init firstEvent
        Ok (remainingEvents |> List.fold aggregate.evolve initialState)

/// Helper to process commands using aggregate pattern
let processCommand<'Command, 'Event, 'State when 'Event :> IEvent> 
    (logger: ILogger)
    (repository: IRepository<'State, 'Event>)
    (aggregate: Aggregate<'Command, 'Event, 'State>)
    (getId: 'Command -> AggregateId)
    (command: 'Command) = async {
    
    logger.LogDebug("Processing command of type {CommandType}", command.GetType().FullName)
    let aggregateId =
        try
            let id = getId command
            logger.LogDebug("Extracted aggregate ID: {AggregateId}", id)
            id
        with ex ->
            logger.LogError(ex, "Failed to extract aggregate ID from command")
            ""

    if System.String.IsNullOrEmpty(aggregateId) then
        return Error "Failed to extract aggregate ID from command"
    else
        let! existingResult = repository.GetById aggregateId
        logger.LogDebug("Repository GetById result - IsError: {IsError}", existingResult |> Result.isError)

        match existingResult with
        | Error _ ->
            logger.LogDebug("Aggregate {AggregateId} doesn't exist, attempting to create", aggregateId)
            // Aggregate doesn't exist, try to create
            match aggregate.create command with
            | Ok events ->
                logger.LogDebug("Aggregate creation succeeded with {EventCount} events for {AggregateId}", events.Length, aggregateId)
                for i, evt in events |> List.indexed do
                    let evtType = if isNull (box evt) then "NULL" else evt.GetType().FullName
                    logger.LogTrace("Event [{Index}] type: {EventType}", i, evtType)

                logger.LogDebug("Saving new aggregate {AggregateId} at version 0", aggregateId)
                let! saveResult = repository.Save aggregateId events 0L
                logger.LogDebug("Repository save completed - IsOk: {IsOk}", saveResult |> Result.isOk)

                match saveResult with
                | Ok () ->
                    logger.LogInformation("Successfully created aggregate {AggregateId}", aggregateId)
                    return Ok ()
                | Error err ->
                    logger.LogError("Failed to save new aggregate {AggregateId}: {Error}", aggregateId, err)
                    return Error ("Failed to save new aggregate: " + err)
            | Error err ->
                logger.LogError("Aggregate creation failed for {AggregateId}: {Error}", aggregateId, err)
                return Error err

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
type AggregateHandler<'Command, 'Event, 'State when 'Event :> IEvent>
    (logger: ILogger,
     repository: IRepository<'State, 'Event>, 
     aggregate: Aggregate<'Command, 'Event, 'State>,
     getId: 'Command -> AggregateId) =
    
    interface ICommandHandler<'Command> with
        member this.Handle command = async {
            return! processCommand logger repository aggregate getId command
        }

