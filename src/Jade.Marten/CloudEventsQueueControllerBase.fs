module Jade.Marten.CloudEventsQueueControllerBase

open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Jade.Core.CloudEvents
open Jade.Core.CommandQueue

[<ApiController>]
[<Route("api/cloudevents")>]
type CloudEventsQueueControllerBase<'T when 'T :> ControllerBase>(
    logger: ILogger<'T>,
    publisher: ICommandPublisher) =
    inherit ControllerBase()

    [<NonAction>]
    member this.HandlePublishSuccess (cloudEvent: CloudEvent) = async {
        logger.LogInformation("CloudEvent {Id} queued successfully", cloudEvent.Id)
        return this.Accepted({|
            Id = cloudEvent.Id
            Status = "accepted"
            Message = "Command queued for processing"
        |}) :> IActionResult
    }

    [<NonAction>]
    member this.HandlePublishError (cloudEvent: CloudEvent) (error: string) = async {
        logger.LogError("Failed to queue CloudEvent {Id}: {Error}", cloudEvent.Id, error)
        return this.StatusCode(500, {|
            Id = cloudEvent.Id
            Status = "failed"
            Message = $"Failed to queue command: {error}"
        |}) :> IActionResult
    }

    [<NonAction>]
    member this.HandleValidationError (cloudEvent: CloudEvent) (error: string) = async {
        logger.LogWarning("Invalid CloudEvent: {Error}", error)
        return this.BadRequest({|
            Id = cloudEvent.Id
            Status = "rejected"
            Message = error
        |}) :> IActionResult
    }

    [<HttpPost>]
    [<Consumes("application/cloudevents+json")>]
    [<Produces("application/json")>]
    member this.ProcessCloudEvent([<FromBody>] cloudEvent: CloudEvent) : Async<IActionResult> = async {
        logger.LogInformation("Received CloudEvent {Id} of type {Type}", cloudEvent.Id, cloudEvent.Type)

        match validateCloudEvent cloudEvent with
        | Error validationError ->
            return! this.HandleValidationError cloudEvent validationError

        | Ok validatedEvent ->
            match validatedEvent.DataSchema with
            | None ->
                logger.LogWarning("CloudEvent {Id} missing dataschema", validatedEvent.Id)
                return this.UnprocessableEntity({|
                    Id = validatedEvent.Id
                    Status = "rejected"
                    Message = "CloudEvent 'dataschema' is required"
                |}) :> IActionResult

            | Some schema ->
                match extractAggregateType schema with
                | Error err ->
                    return! this.HandleValidationError validatedEvent err

                | Ok _ ->
                    let! result = publisher.Publish validatedEvent

                    match result with
                    | Ok () ->
                        return! this.HandlePublishSuccess validatedEvent
                    | Error err ->
                        return! this.HandlePublishError validatedEvent err
    }

type StandardCloudEventsQueueController(
    logger: ILogger<StandardCloudEventsQueueController>,
    publisher: ICommandPublisher) =
    inherit CloudEventsQueueControllerBase<StandardCloudEventsQueueController>(logger, publisher)
