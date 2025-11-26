module Jade.Core.EventSourcing

open System
open Microsoft.Extensions.Logging

/// Base interface for all domain events
type IEvent =
    abstract member Metadata: Metadata option

/// Base interface for all domain commands
type ICommand =
    abstract member Metadata: Metadata

/// Unique identifier for an aggregate
type AggregateId = string

/// Repository for loading and saving aggregates
type IRepository<'State, 'Event when 'Event :> IEvent> =
    abstract member GetById: AggregateId -> Async<Result<'State * int64, string>>
    abstract member Save: AggregateId -> 'Event list -> int64 -> Async<Result<unit, string>>

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
        logger.LogDebug("Repository GetById result for {AggregateId} - IsError: {IsError}", aggregateId, existingResult |> Result.isError)

        match existingResult with
        | Error _ ->
            logger.LogDebug("Aggregate {AggregateId} doesn't exist, attempting to create", aggregateId)
            match aggregate.create command with
            | Ok events ->
                logger.LogDebug("Aggregate creation succeeded with {EventCount} events for {AggregateId}", events.Length, aggregateId)
                let! saveResult = repository.Save aggregateId events 0L
                match saveResult with
                | Ok () ->
                    logger.LogInformation("Successfully created aggregate {AggregateId} with {EventCount} events", aggregateId, events.Length)
                    return Ok ()
                | Error err ->
                    logger.LogError("Failed to save new aggregate {AggregateId}: {Error}", aggregateId, err)
                    return Error $"Failed to save new aggregate: {err}"
            | Error err ->
                logger.LogError("Aggregate creation failed for {AggregateId}: {Error}", aggregateId, err)
                return Error err

        | Ok (state, currentVersion) ->
            logger.LogDebug("Aggregate {AggregateId} exists at version {Version}, applying decision", aggregateId, currentVersion)
            match aggregate.decide command state with
            | Ok events when events.IsEmpty ->
                logger.LogDebug("No events produced for aggregate {AggregateId}", aggregateId)
                return Ok ()
            | Ok events ->
                logger.LogDebug("Decision produced {EventCount} events for aggregate {AggregateId}", events.Length, aggregateId)
                let! saveResult = repository.Save aggregateId events currentVersion
                return saveResult |> Result.mapError (fun err -> $"Failed to save aggregate: {err}")
            | Error err -> return Error err
}
