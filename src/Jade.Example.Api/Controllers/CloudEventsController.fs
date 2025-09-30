namespace Jade.Example.Api.Controllers

open Microsoft.Extensions.Logging
open Jade.Marten.CloudEventsControllerBase
open Jade.Core.CommandRegistry
open Jade.Core.CommandBus

type CloudEventsController(logger: ILogger<StandardCloudEventsController>, 
                          commandBus: ICommandBus,
                          registry: Registry) =
    inherit StandardCloudEventsController(logger, commandBus, registry)