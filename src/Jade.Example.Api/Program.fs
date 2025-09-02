namespace Jade.Example.Api
#nowarn "20"
open System
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Marten
open Microsoft.OpenApi.Models
open System.Text.Json
open Jade.Core.CommandRegistry
open Jade.Core.PostgreSqlContainer
open Jade.Core.CommandBus
open Jade.Core.MartenRepository
open Jade.Example.Domain.MartenConfiguration

module Program =
    let exitCode = 0

    [<EntryPoint>]
    let main args =

        let builder = WebApplication.CreateBuilder(args)
        
        // Configure JSON serialization for F# types
        builder.Services.AddControllers(fun options -> 
            options.ReturnHttpNotAcceptable <- true
        ).AddJsonOptions(fun options ->
            options.JsonSerializerOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        ) |> ignore
        
        // Start PostgreSQL container if in development
        let tempLogger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore).CreateLogger<obj>()
        let useContainer = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") = "Development"
        
        let connectionString = 
            if useContainer then
                containerManager.StartAsync(tempLogger) |> Async.RunSynchronously
            else
                builder.Configuration.GetConnectionString("PostgreSQL")
                |> Option.ofObj 
                |> Option.defaultValue "Host=localhost;Port=5432;Database=jade_api;Username=postgres;Password=postgres"
        
        // Configure Marten with the connection string
        builder.Services.AddMarten(fun (options: StoreOptions) ->
            options.Connection(connectionString)
            options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.CreateOrUpdate
            options.DatabaseSchemaName <- "jade_events"
            configureDomainMarten options
        ).UseLightweightSessions() |> ignore
        
        // Register Command Schema Registry first
        builder.Services.AddSingleton<Registry>(fun serviceProvider ->
            let documentStore = serviceProvider.GetRequiredService<IDocumentStore>()
            let registry = Registry()
            
            // Register Customer handler with commands
            let customerRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, Customer.State> documentStore Customer.aggregate
            let customerHandler = Jade.Core.CommandBus.createHandler customerRepository Customer.aggregate Customer.getId
            registry.register([
                typeof<Customer.Command.Create.V1>
                typeof<Customer.Command.Create.V2>
                typeof<Customer.Command.Update.V1>
            ], customerHandler)
            
            // Register Order handler with commands
            let orderRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, Order.State> documentStore Order.aggregate
            let orderHandler = Jade.Core.CommandBus.createHandler orderRepository Order.aggregate Order.getId
            registry.register([
                typeof<Order.Command.Create.V1>
                typeof<Order.Command.Create.V2>
                typeof<Order.Command.Cancel.V1>
            ], orderHandler)
            
            registry
        ) |> ignore
        
        // Register CommandBus that uses registry
        builder.Services.AddSingleton<ICommandBus>(fun serviceProvider ->
            let registry = serviceProvider.GetRequiredService<Registry>()
            CommandBus(registry.GetHandler) :> ICommandBus
        ) |> ignore
        
        // Add Swagger/OpenAPI
        builder.Services.AddEndpointsApiExplorer() |> ignore
        builder.Services.AddSwaggerGen(fun c ->
            c.SwaggerDoc("v1", OpenApiInfo(
                Title = "Jade CloudEvents API",
                Version = "v1",
                Description = "API for processing commands via CloudEvents"
            ))
        ) |> ignore

        let app = builder.Build()
        
        // Configure the HTTP request pipeline
        if app.Environment.IsDevelopment() then
            app.UseSwagger() |> ignore
            app.UseSwaggerUI(fun c ->
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Jade CloudEvents API v1")
            ) |> ignore
        
        app.UseHttpsRedirection() |> ignore
        app.UseAuthorization() |> ignore
        app.MapControllers() |> ignore
        
        // Log startup information
        let logger = app.Services.GetRequiredService<ILogger<obj>>()
        let registry = app.Services.GetRequiredService<Registry>()
        logger.LogInformation("Jade CloudEvents API started")
        logger.LogInformation("Registered {Count} command schemas", registry.RegisteredSchemas.Length)
        for schema in registry.RegisteredSchemas do
            logger.LogInformation("  - {Schema}", schema)

        // Handle application shutdown to cleanup container
        let applicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>()
        applicationLifetime.ApplicationStopping.Register(fun () ->
            if useContainer then
                containerManager.StopAsync(tempLogger) |> Async.RunSynchronously
        ) |> ignore

        app.Run()

        exitCode