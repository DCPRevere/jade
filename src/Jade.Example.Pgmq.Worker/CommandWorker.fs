namespace Jade.Example.Pgmq.Worker

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Jade.Core.CommandQueue
open Jade.Core.CommandRegistry

type CommandWorker(
    logger: ILogger<CommandWorker>,
    receivers: ICommandReceiver list,
    registry: Registry) =
    inherit BackgroundService()

    override _.ExecuteAsync(stoppingToken: CancellationToken) =
        task {
            logger.LogInformation("CommandWorker starting {ReceiverCount} receivers at: {Time}", receivers.Length, DateTimeOffset.Now)

            let handleMessage = processCloudEvent registry logger

            try
                let receiverTasks =
                    receivers
                    |> List.map (fun receiver -> receiver.StartReceiving handleMessage)
                    |> Async.Parallel
                    |> Async.Ignore

                do! receiverTasks |> Async.StartAsTask :> Task
            with
            | :? OperationCanceledException ->
                logger.LogInformation("CommandWorker is stopping")
            | ex ->
                logger.LogError(ex, "Unhandled exception in CommandWorker")
        }

    override _.StopAsync(cancellationToken: CancellationToken) =
        task {
            logger.LogInformation("CommandWorker stopping at: {Time}", DateTimeOffset.Now)

            let stopTasks =
                receivers
                |> List.map (fun receiver -> receiver.StopReceiving())
                |> Async.Parallel
                |> Async.Ignore

            do! stopTasks |> Async.StartAsTask :> Task
        }
