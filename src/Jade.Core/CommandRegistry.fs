module Jade.Core.CommandRegistry

open System
open System.Text.Json
open System.Collections.Generic
open System.Text.Json.Serialization
open System.Reflection
open Microsoft.Extensions.Logging
open Jade.Core.CommandBus

// Registry for mapping dataschema URIs to command types and handlers
type Registry(logger: ILogger<Registry>, jsonOptions: JsonSerializerOptions) =
    let schemaToType = Dictionary<string, Type>()
    let typeToHandler = Dictionary<Type, IHandler>()
    
    // Extract schema from command type using reflection
    let extractSchemaFromType (commandType: Type) : Result<string, string> =
        let toSchemaProperty = commandType.GetProperty("toSchema", BindingFlags.Static ||| BindingFlags.Public)
        if toSchemaProperty = null then
            logger.LogError("Command type {CommandType} does not have a static toSchema property", commandType.Name)
            Error $"Command type {commandType.Name} does not have a static toSchema property"
        else
            try
                match toSchemaProperty.GetValue(null) with
                | :? string as schema -> Ok schema
                | _ ->
                    logger.LogError("toSchema property of {CommandType} must return a string", commandType.Name)
                    Error $"toSchema property of {commandType.Name} must return a string"
            with
            | ex ->
                logger.LogError("Error accessing toSchema property of command type {CommandType}: {Error}", commandType.Name, ex.Message)
                Error $"Error accessing toSchema property: {ex.Message}"
    
    member _.register(commandTypes: Type list, handler: IHandler) =
        for commandType in commandTypes do
            match extractSchemaFromType commandType with
            | Ok schema ->
                logger.LogInformation("Registering command type {CommandType} with schema {Schema}", commandType.Name, schema)
                schemaToType.[schema] <- commandType
                typeToHandler.[commandType] <- handler
            | Error err ->
                logger.LogError("Failed to register command type {CommandType}: {Error}", commandType.Name, err)
                eprintfn "Failed to register command type %s: %s" commandType.Name err
    
    member _.TryGetType(schema: string) =
        match schemaToType.TryGetValue(schema) with
        | true, commandType -> 
            logger.LogDebug("Found command type {CommandType} for schema {Schema}", commandType.Name, schema)
            Some commandType
        | false, _ -> 
            logger.LogWarning("No command type found for schema {Schema}", schema)
            None
        
    member _.GetHandler(commandType: Type) =
        match typeToHandler.TryGetValue(commandType) with
        | true, handler -> 
            logger.LogDebug("Found handler for command type {CommandType}", commandType.Name)
            Some handler
        | false, _ -> 
            let registeredTypes = typeToHandler.Keys |> Seq.map (fun t -> t.Name) |> String.concat ", "
            logger.LogDebug("No handler found for command type {CommandType}. Registered handlers: [{RegisteredTypes}]", commandType.Name, registeredTypes)
            None
    
    member _.DeserializeCommand(schema: string, json: JsonElement) : Result<obj, string> =
        match schemaToType.TryGetValue(schema) with
        | true, commandType -> 
            try
                logger.LogDebug("Deserializing command with schema {Schema} to type {CommandType}", schema, commandType.Name)
                let command = JsonSerializer.Deserialize(json.GetRawText(), commandType, jsonOptions)
                logger.LogDebug("Successfully deserialized command with schema {Schema}", schema)
                Ok command
            with ex ->
                logger.LogError(ex, "Failed to deserialize command with schema {Schema}: {Error}", schema, ex.Message)
                Error $"Failed to deserialize command: {ex.Message}"
        | false, _ -> 
            logger.LogError("Unknown command schema: {Schema}", schema)
            Error $"Unknown command schema: {schema}"
    
    member _.RegisteredSchemas = 
        schemaToType.Keys |> Seq.toList

