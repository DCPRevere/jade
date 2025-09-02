namespace Jade.Example.Api.Controllers

open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Jade.Core.CloudEvents
open Jade.Core.CommandRegistry
open Jade.Core.CommandBus
open System

[<ApiController>]
[<Route("api/cloudevents")>]
type CloudEventsController(logger: ILogger<CloudEventsController>, 
                          commandBus: ICommandBus,
                          registry: Registry) =
    inherit ControllerBase()
    
    [<HttpPost>]
    [<Consumes("application/cloudevents+json")>]
    [<Produces("application/json")>]
    member this.ProcessCloudEvent([<FromBody>] cloudEvent: CloudEvent) : Async<IActionResult> = async {
        logger.LogInformation("Received CloudEvent: {Id} of type {Type}", cloudEvent.Id, cloudEvent.Type)
        
        // Validate CloudEvent structure
        match validateCloudEvent cloudEvent with
        | Error validationError ->
            logger.LogWarning("Invalid CloudEvent: {Error}", validationError)
            return this.BadRequest({ 
                Id = cloudEvent.Id
                Status = "rejected"
                Message = Some validationError 
            }) :> IActionResult
            
        | Ok validatedEvent ->
            // Check for required dataschema
            match validatedEvent.DataSchema with
            | None ->
                logger.LogWarning("CloudEvent missing dataschema")
                return this.UnprocessableEntity({ 
                    Id = validatedEvent.Id
                    Status = "rejected"
                    Message = Some "CloudEvent 'dataschema' is required for command processing" 
                }) :> IActionResult
                
            | Some schema ->
                // Check for data field
                match validatedEvent.Data with
                | None ->
                    logger.LogWarning("CloudEvent missing data")
                    return this.UnprocessableEntity({ 
                        Id = validatedEvent.Id
                        Status = "rejected"
                        Message = Some "CloudEvent 'data' is required" 
                    }) :> IActionResult
                    
                | Some data ->
                    // Deserialize command based on schema
                    match registry.DeserializeCommand(schema, data) with
                    | Error deserializationError ->
                        logger.LogError("Failed to deserialize command: {Error}", deserializationError)
                        return this.UnprocessableEntity({ 
                            Id = validatedEvent.Id
                            Status = "rejected"
                            Message = Some deserializationError 
                        }) :> IActionResult
                        
                    | Ok command ->
                        logger.LogInformation("Successfully deserialized command of type {Type} from schema {Schema}", 
                                             command.GetType().Name, schema)
                        
                        // Send command through CommandBus
                        try
                            let! result = commandBus.Send command
                            
                            match result with
                            | Ok () ->
                                logger.LogInformation("Command processed successfully for CloudEvent {Id}", validatedEvent.Id)
                                return this.Accepted({ 
                                    Id = validatedEvent.Id
                                    Status = "accepted"
                                    Message = Some "Command processed successfully" 
                                }) :> IActionResult
                                
                            | Error commandError ->
                                logger.LogError("Command processing failed: {Error}", commandError)
                                return this.StatusCode(500, { 
                                    Id = validatedEvent.Id
                                    Status = "failed"
                                    Message = Some $"Command processing failed: {commandError}" 
                                }) :> IActionResult
                        with
                        | ex ->
                            logger.LogError(ex, "Unexpected error processing command")
                            return this.StatusCode(500, { 
                                Id = validatedEvent.Id
                                Status = "failed"
                                Message = Some $"Unexpected error: {ex.Message}" 
                            }) :> IActionResult
    }
    
    [<HttpGet("schemas")>]
    [<Produces("application/json")>]
    member _.GetRegisteredSchemas() =
        {| 
            schemas = registry.RegisteredSchemas 
            count = registry.RegisteredSchemas.Length
        |}