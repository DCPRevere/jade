module Jade.Marten.PgmqCommandPublisher

open System
open System.Text.Json
open Microsoft.Extensions.Logging
open Npgsql
open Jade.Core.CloudEvents
open Jade.Core.CommandQueue

type PgmqCommandPublisher(connectionString: string, jsonOptions: JsonSerializerOptions, logger: ILogger<PgmqCommandPublisher>) =

    member _.Publish(cloudEvent: CloudEvent) = async {
        try
            // Extract aggregate type from dataschema to determine queue name
            match cloudEvent.DataSchema with
            | None -> return Error "CloudEvent.dataschema is required"
            | Some schema ->
                match extractAggregateType schema with
                | Error err -> return Error err
                | Ok aggregateType ->

                    // Serialize CloudEvent to JSON
                    let cloudEventJson = JsonSerializer.Serialize(cloudEvent, jsonOptions)

                    // Queue name is the aggregate type
                    let queueName = aggregateType

                    use connection = new NpgsqlConnection(connectionString)
                    do! connection.OpenAsync() |> Async.AwaitTask

                    // Create queue if it doesn't exist
                    let createQueueSql = "SELECT pgmq.create(@queueName)"
                    use createCmd = new NpgsqlCommand(createQueueSql, connection)
                    createCmd.Parameters.AddWithValue("queueName", queueName) |> ignore

                    try
                        do! createCmd.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                        logger.LogDebug("Queue {Queue} created or already exists", queueName)
                    with
                    | ex when ex.Message.Contains("already exists") ->
                        logger.LogDebug("Queue {Queue} already exists", queueName)

                    // PGMQ send message
                    // pgmq.send(queue_name text, message jsonb, delay integer DEFAULT 0)
                    let sql = "SELECT pgmq.send(@queueName, @message::jsonb)"

                    use cmd = new NpgsqlCommand(sql, connection)
                    cmd.Parameters.AddWithValue("queueName", queueName) |> ignore
                    cmd.Parameters.AddWithValue("message", cloudEventJson) |> ignore

                    let! messageId = cmd.ExecuteScalarAsync() |> Async.AwaitTask

                    logger.LogInformation("Published CloudEvent {CloudEventId} to queue {Queue} with message ID {MessageId}",
                                         cloudEvent.Id, queueName, messageId)

                    return Ok ()
        with
        | ex ->
            logger.LogError(ex, "Failed to publish CloudEvent {CloudEventId}", cloudEvent.Id)
            return Error ex.Message
    }

    interface ICommandPublisher with
        member this.Publish cloudEvent = this.Publish cloudEvent
