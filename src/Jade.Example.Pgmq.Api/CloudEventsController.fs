namespace Jade.Example.Pgmq.Api

open Microsoft.Extensions.Logging
open Jade.Core.CommandQueue
open Jade.Marten.CloudEventsQueueControllerBase

type CloudEventsController(logger: ILogger<CloudEventsController>, publisher: ICommandPublisher) =
    inherit CloudEventsQueueControllerBase<CloudEventsController>(logger, publisher)
