module Jade.Marten.CloudEventsControllerBase

open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Jade.Core.CloudEvents
open Jade.Core.CommandRegistry
open Jade.Core.CommandBus
open Jade.Core.CommandQueue
open System

[<ApiController>]
[<Route("api/cloudevents")>]
type CloudEventsControllerBase<'T when 'T :> ControllerBase>(logger: ILogger<'T>,
                                                            commandBus: ICommandBus,
                                                            registry: Registry) =
    inherit ControllerBase()

    [<NonAction>]
    member this.CreateResponse (cloudEvent: CloudEvent) (status: string) (httpStatus: int) (message: string option) : IActionResult =
        let response = {
            Id = cloudEvent.Id
            Status = status
            Message = message
        }
        match httpStatus with
        | 202 -> this.Accepted(response) :> IActionResult
        | 400 -> this.BadRequest(response) :> IActionResult
        | 422 -> this.UnprocessableEntity(response) :> IActionResult
        | _ -> this.StatusCode(httpStatus, response) :> IActionResult
    
    [<HttpPost>]
    [<Consumes("application/cloudevents+json")>]
    [<Produces("application/json")>]
    member this.ProcessCloudEvent([<FromBody>] cloudEvent: CloudEvent) : Async<IActionResult> = async {
        logger.LogInformation("Received CloudEvent: {Id} of type {Type}", cloudEvent.Id, cloudEvent.Type)

        match validateCloudEvent cloudEvent with
        | Error validationError ->
            return this.CreateResponse cloudEvent "rejected" 400 (Some validationError)
        | Ok validatedEvent ->
            let! result = processCloudEvent registry logger validatedEvent
            match result with
            | Ok () ->
                return this.CreateResponse validatedEvent "accepted" 202 (Some "Command processed successfully")
            | Error err ->
                return this.CreateResponse validatedEvent "failed" 500 (Some err)
    }
    
    [<HttpGet("schemas")>]
    [<Produces("application/json")>]
    member _.GetRegisteredSchemas() =
        {| 
            schemas = registry.RegisteredSchemas 
            count = registry.RegisteredSchemas.Length
        |}

type StandardCloudEventsController(logger: ILogger<StandardCloudEventsController>, 
                                 commandBus: ICommandBus,
                                 registry: Registry) =
    inherit CloudEventsControllerBase<StandardCloudEventsController>(logger, commandBus, registry)