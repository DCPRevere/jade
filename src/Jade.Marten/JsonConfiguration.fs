module Jade.Marten.JsonConfiguration

open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http.Json
open FSharp.SystemTextJson

/// Configures JSON serialization for all contexts in the application using a configuration function
let configureJsonSerialization (configureOptions: JsonSerializerOptions -> unit) (services: IServiceCollection) =
    
    // Configure controller JSON options
    services.AddControllers(fun options -> 
        options.ReturnHttpNotAcceptable <- true
    ).AddJsonOptions(fun options ->
        configureOptions options.JsonSerializerOptions
    ) |> ignore
    
    // Configure HTTP JSON options (for minimal APIs)
    services.ConfigureHttpJsonOptions(fun (options: JsonOptions) ->
        configureOptions options.SerializerOptions
    ) |> ignore
    
    // Create and register a configured instance for dependency injection
    let singletonOptions = JsonSerializerOptions()
    configureOptions singletonOptions
    services.AddSingleton<JsonSerializerOptions>(singletonOptions) |> ignore
    
    services