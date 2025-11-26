module Jade.Core.CommandBus

open System
open Microsoft.Extensions.Logging
open Jade.Core.EventSourcing

type IHandler =
    abstract member Handle: obj -> Async<Result<unit, string>>

type ICommandBus =
    abstract member Send: 'Command -> Async<Result<unit, string>>

type CommandBus(getHandler: Type -> IHandler option, logger: ILogger<CommandBus>) =
    
    member _.Send command = async {
        let commandType = command.GetType()
        logger.LogInformation("Sending command of type {CommandType}", commandType.Name)

        match getHandler commandType with
        | Some handler ->
            logger.LogDebug("Found handler for command type {CommandType}", commandType.Name)
            let! result = handler.Handle command
            match result with
            | Ok () -> logger.LogInformation("Successfully handled command of type {CommandType}", commandType.Name)
            | Error err -> logger.LogError("Failed to handle command of type {CommandType}: {Error}", commandType.Name, err)
            return result
        | None ->
            logger.LogError("No handler registered for command type {CommandType}. Available handlers should be registered in the command registry during application startup", commandType.Name)
            return Error $"No handler registered for command type {commandType.Name}"
    }
    
    interface ICommandBus with
        member this.Send command = this.Send command

let createHandler<'Command, 'Event, 'State when 'Event :> IEvent>
    (logger: ILogger)
    (repository: IRepository<'State, 'Event>)
    (aggregate: Aggregate<'Command, 'Event, 'State>)
    (getId: 'Command -> AggregateId) : IHandler =

    { new IHandler with
        member _.Handle command = async {
            try
                let typedCommand = command :?> 'Command
                return! processCommand logger repository aggregate getId typedCommand
            with
            | :? System.InvalidCastException as ex ->
                let expectedType = typeof<'Command>.Name
                let actualType = if command <> null then command.GetType().Name else "null"
                return Error $"Invalid cast: expected {expectedType}, got {actualType}. {ex.Message}"
            | ex ->
                return Error $"Handler execution failed: {ex.Message}"
        } }

