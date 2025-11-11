module Jade.Core.CommandQueue

open System
open Microsoft.Extensions.Logging
open Jade.Core.CloudEvents
open Jade.Core.CommandRegistry

/// Publisher interface - publishes CloudEvents to queue
type ICommandPublisher =
    abstract member Publish: CloudEvent -> Async<Result<unit, string>>

/// Receiver interface - receives CloudEvents from queue and processes them
/// Takes a message handler function that processes each received CloudEvent
type ICommandReceiver =
    abstract member StartReceiving: (CloudEvent -> Async<Result<unit, string>>) -> Async<unit>
    abstract member StopReceiving: unit -> Async<unit>

/// Helper to extract aggregate type from dataschema URN
/// Pattern: urn:schema:jade:command:{aggregate-type}:{action}:{version}
/// Example: "urn:schema:jade:command:orders:create:1" -> "orders"
let extractAggregateType (dataschema: string) : Result<string, string> =
    match dataschema.Split(':') with
    | [| "urn"; "schema"; "jade"; "command"; aggregateType; _; _ |] -> Ok aggregateType
    | _ -> Error $"Invalid dataschema URN format: {dataschema}. Expected pattern: urn:schema:jade:command:{{aggregate-type}}:{{action}}:{{version}}"

/// Helper to process a CloudEvent using Registry
/// This is used by both HTTP controllers and queue receivers
let processCloudEvent (registry: Registry) (logger: ILogger) (cloudEvent: CloudEvent) : Async<Result<unit, string>> = async {
    match cloudEvent.DataSchema, cloudEvent.Data with
    | None, _ ->
        logger.LogError("CloudEvent missing dataschema")
        return Error "CloudEvent 'dataschema' is required"
    | _, None ->
        logger.LogError("CloudEvent missing data")
        return Error "CloudEvent 'data' is required"
    | Some schema, Some data ->
        logger.LogDebug("Processing CloudEvent {Id} with schema {Schema}", cloudEvent.Id, schema)

        match registry.DeserializeCommand(schema, data) with
        | Error deserializationError ->
            logger.LogError("Failed to deserialize command from CloudEvent {Id}: {Error}", cloudEvent.Id, deserializationError)
            return Error deserializationError
        | Ok command ->
            logger.LogDebug("Deserialized command of type {Type} from CloudEvent {Id}", command.GetType().Name, cloudEvent.Id)

            match registry.GetHandler(command.GetType()) with
            | None ->
                let errorMsg = $"No handler registered for command type {command.GetType().Name}"
                logger.LogError("No handler for command type {Type} from CloudEvent {Id}", command.GetType().Name, cloudEvent.Id)
                return Error errorMsg
            | Some handler ->
                logger.LogDebug("Processing command {Type} from CloudEvent {Id}", command.GetType().Name, cloudEvent.Id)
                let! result = handler.Handle command

                match result with
                | Ok () ->
                    logger.LogInformation("Successfully processed CloudEvent {Id}", cloudEvent.Id)
                    return Ok ()
                | Error err ->
                    logger.LogError("Failed to process CloudEvent {Id}: {Error}", cloudEvent.Id, err)
                    return Error err
}
