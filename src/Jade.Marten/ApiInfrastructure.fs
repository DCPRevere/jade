module Jade.Marten.ApiInfrastructure

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.OpenApi.Models
open System.Text.Json
open Marten
open Jade.Core.CommandRegistry
open Jade.Marten.PostgreSqlContainer
open Jade.Core.CommandBus
open Jade.Marten.MartenRepository
open Jade.Marten.JsonConfiguration
open Jade.Marten.MartenConfiguration

type JadeApiConfiguration = {
    ApiTitle: string
    ApiVersion: string
    ApiDescription: string
    DatabaseSchemaName: string
    UseContainerInDevelopment: bool
}

let defaultConfiguration = {
    ApiTitle = "Jade CloudEvents API"
    ApiVersion = "v1"
    ApiDescription = "API for processing commands via CloudEvents"
    DatabaseSchemaName = "jade_events"
    UseContainerInDevelopment = false
}

module ServiceConfiguration =
    
    
    let configureDatabaseWithContainer (configuration: IConfiguration) (config: JadeApiConfiguration) (jsonOptions: JsonSerializerOptions) (martenConfig: StoreOptions -> unit) (services: IServiceCollection) =
        let tempLogger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore).CreateLogger<obj>()
        let isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") = "Development"
        let useContainer = config.UseContainerInDevelopment && isDevelopment
        
        let connectionString = 
            if useContainer then
                tempLogger.LogInformation("🐘 Starting PostgreSQL TestContainer for development...")
                containerManager.StartAsync(tempLogger) |> Async.RunSynchronously
            else
                match configuration.GetConnectionString("PostgreSQL") with
                | null | "" -> 
                    let defaultConn = "Host=localhost;Port=5432;Database=jade_api;Username=postgres;Password=postgres"
                    if isDevelopment then
                        tempLogger.LogWarning("⚠️  No PostgreSQL connection string found. Using default: {ConnectionString}", defaultConn)
                        tempLogger.LogInformation("💡 To use TestContainers, set UseContainerInDevelopment=true in configuration")
                    else
                        tempLogger.LogError("❌ No PostgreSQL connection string configured for production. Set 'ConnectionStrings:PostgreSQL' in appsettings.json")
                        failwith "PostgreSQL connection string is required in production environments"
                    defaultConn
                | connStr -> 
                    tempLogger.LogInformation("🔗 Using configured PostgreSQL connection")
                    connStr
        
        services.AddMarten(fun (options: StoreOptions) ->
            options.Connection(connectionString)
            options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.CreateOrUpdate
            options.DatabaseSchemaName <- config.DatabaseSchemaName

            // Configure base Marten settings with user's JSON options
            configureMartenBase jsonOptions options

            martenConfig options
        ).UseLightweightSessions() |> ignore
        
        services
    
    let configureCommandBus (registryConfiguration: Marten.IDocumentStore -> Registry -> unit) (services: IServiceCollection) =
        services.AddSingleton<Registry>(fun serviceProvider ->
            try
                let documentStore = serviceProvider.GetRequiredService<Marten.IDocumentStore>()
                let logger = serviceProvider.GetRequiredService<ILogger<Registry>>()
                let jsonOptions = serviceProvider.GetRequiredService<JsonSerializerOptions>()
                let registry = Registry(logger, jsonOptions)
                logger.LogInformation("Configuring command registry with handlers")
                registryConfiguration documentStore registry
                logger.LogInformation("Command registry configured with {HandlerCount} schemas", registry.RegisteredSchemas.Length)
                registry
            with
            | ex -> 
                let tempLogger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore).CreateLogger<Registry>()
                tempLogger.LogError(ex, "Failed to create Registry instance during DI configuration")
                reraise()
        ) |> ignore
        
        services.AddSingleton<ICommandBus>(fun serviceProvider ->
            try
                let registry = serviceProvider.GetRequiredService<Registry>()
                let logger = serviceProvider.GetRequiredService<ILogger<CommandBus>>()
                logger.LogInformation("Creating CommandBus with registry containing {SchemaCount} schemas", registry.RegisteredSchemas.Length)
                CommandBus(registry.GetHandler, logger) :> ICommandBus
            with
            | ex ->
                let tempLogger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore).CreateLogger<CommandBus>()
                tempLogger.LogError(ex, "Failed to create CommandBus instance during DI configuration")
                reraise()
        ) |> ignore
        
        services
    
    let configureSwagger (config: JadeApiConfiguration) (services: IServiceCollection) =
        services.AddEndpointsApiExplorer() |> ignore
        services.AddSwaggerGen(fun c ->
            c.SwaggerDoc("v1", OpenApiInfo(
                Title = config.ApiTitle,
                Version = config.ApiVersion,
                Description = config.ApiDescription
            ))
        ) |> ignore
        services

module AppConfiguration =
    
    let configureMiddleware (app: WebApplication) =
        if app.Environment.IsDevelopment() then
            app.UseSwagger() |> ignore
            app.UseSwaggerUI(fun c ->
                c.SwaggerEndpoint("/swagger/v1/swagger.json", sprintf "%s %s" (app.Configuration.GetValue<string>("ApiTitle") |> Option.ofObj |> Option.defaultValue "API") (app.Configuration.GetValue<string>("ApiVersion") |> Option.ofObj |> Option.defaultValue "v1"))
            ) |> ignore
        
        app.UseHttpsRedirection() |> ignore
        app.UseAuthorization() |> ignore
        app.MapControllers() |> ignore
        app
    
    let logStartupInfo (config: JadeApiConfiguration) (app: WebApplication) =
        let logger = app.Services.GetRequiredService<ILogger<obj>>()
        let registry = app.Services.GetRequiredService<Registry>()
        logger.LogInformation("{Title} started", config.ApiTitle)
        logger.LogInformation("Registered {Count} command schemas", registry.RegisteredSchemas.Length)
        for schema in registry.RegisteredSchemas do
            logger.LogInformation("  - {Schema}", schema)
        app
    
    let configureShutdown (app: WebApplication) =
        let tempLogger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore).CreateLogger<obj>()
        let useContainer = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") = "Development"
        
        let applicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>()
        applicationLifetime.ApplicationStopping.Register(fun () ->
            if useContainer then
                containerManager.StopAsync(tempLogger) |> Async.RunSynchronously
        ) |> ignore
        app

type JadeApiBuilder(builder: WebApplicationBuilder) =
    let mutable config = defaultConfiguration
    
    member _.WithConfiguration(newConfig: JadeApiConfiguration) =
        config <- newConfig
        JadeApiBuilder(builder)
    
    member _.ConfigureServices(jsonConfig: JsonSerializerOptions -> unit, martenConfig: StoreOptions -> unit, registryConfig: Marten.IDocumentStore -> Registry -> unit) =
        // Create the JsonSerializerOptions first
        let jsonOptions = JsonSerializerOptions()
        jsonConfig jsonOptions

        builder.Services
        |> configureJsonSerialization jsonConfig
        |> ServiceConfiguration.configureDatabaseWithContainer builder.Configuration config jsonOptions martenConfig
        |> ServiceConfiguration.configureCommandBus registryConfig
        |> ServiceConfiguration.configureSwagger config
        |> ignore

        JadeApiBuilder(builder)
    
    member _.Build() =
        let app = builder.Build()
        app
        |> AppConfiguration.configureMiddleware
        |> AppConfiguration.logStartupInfo config
        |> AppConfiguration.configureShutdown