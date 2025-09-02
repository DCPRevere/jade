module Jade.Core.PostgreSqlContainer

open System
open Microsoft.Extensions.Logging
open Testcontainers.PostgreSql

type PostgreSqlContainerManager() =
    let mutable container: PostgreSqlContainer option = None
    let mutable connectionString: string option = None
    
    member this.StartAsync(logger: ILogger) = 
        async {
            try
                logger.LogInformation("🐘 Starting PostgreSQL container...")
                
                let postgresContainer = 
                    PostgreSqlBuilder()
                        .WithImage("postgres:15")
                        .WithDatabase("jade_api")
                        .WithUsername("postgres")
                        .WithPassword("postgres")
                        .WithCleanUp(true)
                        .Build()
                
                do! postgresContainer.StartAsync() |> Async.AwaitTask
                container <- Some postgresContainer
                connectionString <- Some (postgresContainer.GetConnectionString())
                
                logger.LogInformation("✅ PostgreSQL container started")
                logger.LogInformation("🔗 Connection string: {ConnectionString}", connectionString.Value)
                
                return connectionString.Value
            with ex ->
                logger.LogError(ex, "❌ Failed to start PostgreSQL container")
                return raise ex
        }
    
    member this.StopAsync(logger: ILogger) = 
        async {
            match container with
            | Some c ->
                try
                    logger.LogInformation("🧹 Stopping PostgreSQL container...")
                    do! c.DisposeAsync().AsTask() |> Async.AwaitTask
                    logger.LogInformation("✅ PostgreSQL container stopped")
                with ex ->
                    logger.LogError(ex, "❌ Error stopping PostgreSQL container")
            | None -> ()
        }
    
    member this.ConnectionString = connectionString

// Global singleton instance
let containerManager = PostgreSqlContainerManager()