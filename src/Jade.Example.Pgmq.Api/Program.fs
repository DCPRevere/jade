namespace Jade.Example.Pgmq.Api

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open System.Text.Json
open Jade.Core.CommandQueue
open Jade.Marten.PgmqCommandPublisher

module Program =
    [<EntryPoint>]
    let main args =

        let builder = WebApplication.CreateBuilder(args)

        // Configure JSON options
        let jsonOptions = JsonSerializerOptions()
        jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        jsonOptions.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())

        // Add services
        builder.Services.AddControllers() |> ignore

        // Register PGMQ publisher
        let connectionString = "Host=localhost;Port=5433;Database=jade_pgmq;Username=postgres;Password=postgres"
        builder.Services.AddSingleton<ICommandPublisher>(System.Func<System.IServiceProvider, ICommandPublisher>(fun sp ->
            let logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PgmqCommandPublisher>>()
            PgmqCommandPublisher(connectionString, jsonOptions, logger) :> ICommandPublisher
        )) |> ignore

        builder.Services.AddEndpointsApiExplorer() |> ignore
        builder.Services.AddSwaggerGen() |> ignore

        let app = builder.Build()

        if app.Environment.IsDevelopment() then
            app.UseSwagger() |> ignore
            app.UseSwaggerUI() |> ignore

        app.UseHttpsRedirection() |> ignore
        app.UseAuthorization() |> ignore
        app.MapControllers() |> ignore

        app.Run()

        0
