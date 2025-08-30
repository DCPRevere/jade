module Jade.Core.Sagas

open System
open Jade.Core.EventSourcing
open Jade.Core.EventMetadata
open Jade.Core.CommandBus

/// Saga state
type SagaState =
    | NotStarted
    | InProgress
    | Completed
    | Failed of string

/// Base saga data
type SagaData = {
    Id: Guid
    State: SagaState
    CorrelationId: Guid
    StartedAt: DateTimeOffset
    CompletedAt: DateTimeOffset option
}

/// Saga handler for processing events and issuing commands
type ISagaHandler<'Event, 'Command, 'SagaData> =
    abstract member CanHandle: 'Event -> bool
    abstract member Handle: EventWithMetadata<'Event> -> 'SagaData -> Async<'SagaData * 'Command list>

/// Repository for saga data persistence
type ISagaRepository<'SagaData> =
    abstract member Save: string -> 'SagaData -> Async<Result<unit, string>>
    abstract member Get: string -> Async<Result<'SagaData option, string>>
    abstract member GetByCorrelationId: Guid -> Async<Result<'SagaData list, string>>
    abstract member Delete: string -> Async<Result<unit, string>>

/// Saga orchestrator that processes events and executes sagas
type SagaOrchestrator<'Event, 'Command, 'TSagaData>(
    sagaName: string,
    handler: ISagaHandler<'Event, 'Command, 'TSagaData>,
    sagaRepo: ISagaRepository<'TSagaData>,
    commandBus: ICommandBus,
    createInitialData: Guid -> 'TSagaData) =
    
    member this.ProcessEvent eventWithMetadata = async {
        if handler.CanHandle eventWithMetadata.Event then
            let correlationId = eventWithMetadata.Metadata.CorrelationId
            let sagaId = sagaName + "_" + string correlationId
            
            let! existingSagaResult = sagaRepo.Get sagaId
            let sagaData = 
                match existingSagaResult with
                | Ok (Some data) -> data
                | Ok None -> createInitialData correlationId
                | Error _ -> createInitialData correlationId
            
            let! (updatedSagaData, commands) = handler.Handle eventWithMetadata sagaData
            let! saveResult = sagaRepo.Save sagaId updatedSagaData
            
            match saveResult with
            | Ok () ->
                // Send all commands produced by the saga
                let sendCommands = commands |> List.map commandBus.Send
                let! commandResults = Async.Parallel sendCommands
                
                let failures = 
                    commandResults 
                    |> Array.choose (function Error err -> Some err | Ok _ -> None)
                
                if Array.isEmpty failures then
                    return Ok ()
                else
                    return Error ("Saga command failures: " + String.concat "; " failures)
            | Error err -> return Error err
        else
            return Ok ()
    }

/// Helper to create saga data
let createBaseSagaData id correlationId = {
    Id = id
    State = NotStarted
    CorrelationId = correlationId
    StartedAt = DateTimeOffset.UtcNow
    CompletedAt = None
}

/// Helper to mark saga as completed
let completeSaga sagaData = {
    sagaData with 
        State = Completed
        CompletedAt = Some DateTimeOffset.UtcNow
}

/// Helper to mark saga as failed
let failSaga sagaData reason = {
    sagaData with 
        State = Failed reason
        CompletedAt = Some DateTimeOffset.UtcNow
}