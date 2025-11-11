module Jade.Marten.PgmqCommandReceiver

open System
open System.Text.Json
open System.Threading
open Microsoft.Extensions.Logging
open Npgsql
open Jade.Core.CloudEvents
open Jade.Core.CommandQueue

type PgmqCommandReceiver(connectionString: string, queueName: string, jsonOptions: JsonSerializerOptions, logger: ILogger<PgmqCommandReceiver>) =

    let mutable cancellationTokenSource: CancellationTokenSource option = None

    member _.StartReceiving(handleMessage: CloudEvent -> Async<Result<unit, string>>) = async {
        let cts = new CancellationTokenSource()
        cancellationTokenSource <- Some cts

        logger.LogInformation("Starting PGMQ receiver for queue {Queue}", queueName)

        while not cts.Token.IsCancellationRequested do
            try
                use connection = new NpgsqlConnection(connectionString)
                do! connection.OpenAsync() |> Async.AwaitTask

                // PGMQ read message with visibility timeout
                // pgmq.read(queue_name text, vt integer, qty integer)
                // vt = visibility timeout in seconds
                // qty = number of messages to read
                let sql = "SELECT * FROM pgmq.read(@queueName, @vt, @qty)"

                use cmd = new NpgsqlCommand(sql, connection)
                cmd.Parameters.AddWithValue("queueName", queueName) |> ignore
                cmd.Parameters.AddWithValue("vt", 30) |> ignore  // 30 second visibility timeout
                cmd.Parameters.AddWithValue("qty", 1) |> ignore  // Read one message at a time

                use! reader = cmd.ExecuteReaderAsync() |> Async.AwaitTask

                let! hasMessage = reader.ReadAsync() |> Async.AwaitTask

                if hasMessage then
                    let messageId = reader.GetInt64(reader.GetOrdinal("msg_id"))
                    let messageJson = reader.GetString(reader.GetOrdinal("message"))

                    do! reader.CloseAsync() |> Async.AwaitTask

                    logger.LogDebug("Received message {MessageId} from queue {Queue}", messageId, queueName)

                    try
                        // Deserialize CloudEvent from message
                        let cloudEvent = JsonSerializer.Deserialize<CloudEvent>(messageJson, jsonOptions)

                        // Process using provided handler function
                        let! result = handleMessage cloudEvent

                        match result with
                        | Ok () ->
                            // Delete message from queue (acknowledge)
                            use deleteConnection = new NpgsqlConnection(connectionString)
                            do! deleteConnection.OpenAsync() |> Async.AwaitTask

                            let deleteSql = "SELECT pgmq.delete(@queueName, @messageId)"
                            use deleteCmd = new NpgsqlCommand(deleteSql, deleteConnection)
                            deleteCmd.Parameters.AddWithValue("queueName", queueName) |> ignore
                            deleteCmd.Parameters.AddWithValue("messageId", messageId) |> ignore

                            do! deleteCmd.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                            logger.LogInformation("Successfully processed and deleted message {MessageId} from queue {Queue}", messageId, queueName)

                        | Error err ->
                            // Message will become visible again after timeout expires
                            // PGMQ will automatically make it available for retry
                            logger.LogError("Failed to process message {MessageId} from queue {Queue}: {Error}. Message will retry after visibility timeout.", messageId, queueName, err)

                    with
                    | ex ->
                        logger.LogError(ex, "Error processing message {MessageId} from queue {Queue}", messageId, queueName)
                else
                    // No messages available, wait before polling again
                    do! Async.Sleep 1000
            with
            | ex ->
                logger.LogError(ex, "Error in receiver loop for queue {Queue}", queueName)
                do! Async.Sleep 5000  // Back off on errors
    }

    member _.StopReceiving() = async {
        logger.LogInformation("Stopping PGMQ receiver for queue {Queue}", queueName)

        match cancellationTokenSource with
        | Some cts ->
            cts.Cancel()
            cts.Dispose()
            cancellationTokenSource <- None
        | None -> ()

        return ()
    }

    interface ICommandReceiver with
        member this.StartReceiving handleMessage = this.StartReceiving handleMessage
        member this.StopReceiving() = this.StopReceiving()
