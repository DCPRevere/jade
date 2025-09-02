module Jade.Core.CommandRegistry

open System
open System.Text.Json
open System.Collections.Generic
open System.Text.Json.Serialization
open System.Reflection
open Jade.Core.CommandBus

// Registry for mapping dataschema URIs to command types and handlers
type Registry() =
    let schemaToType = Dictionary<string, Type>()
    let typeToHandler = Dictionary<Type, IHandler>()
    let jsonOptions = 
        let opts = JsonSerializerOptions()
        opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        opts
    
    // Extract schema from command type using reflection
    let extractSchemaFromType (commandType: Type) : string =
        let toSchemaProperty = commandType.GetProperty("toSchema", BindingFlags.Static ||| BindingFlags.Public)
        if toSchemaProperty = null then
            failwithf "Command type %s does not have a static toSchema property" commandType.Name
        toSchemaProperty.GetValue(null) :?> string
    
    member _.register(commandTypes: Type list, handler: IHandler) =
        for commandType in commandTypes do
            let schema = extractSchemaFromType commandType
            schemaToType.[schema] <- commandType
            typeToHandler.[commandType] <- handler
    
    member _.TryGetType(schema: string) =
        match schemaToType.TryGetValue(schema) with
        | true, commandType -> Some commandType
        | false, _ -> None
        
    member _.GetHandler(commandType: Type) =
        match typeToHandler.TryGetValue(commandType) with
        | true, handler -> Some handler
        | false, _ -> None
    
    member _.DeserializeCommand(schema: string, json: JsonElement) : Result<obj, string> =
        match schemaToType.TryGetValue(schema) with
        | true, commandType -> 
            try
                let command = JsonSerializer.Deserialize(json.GetRawText(), commandType, jsonOptions)
                Ok command
            with ex ->
                Error $"Failed to deserialize command: {ex.Message}"
        | false, _ -> 
            Error $"Unknown command schema: {schema}"
    
    member _.RegisteredSchemas = 
        schemaToType.Keys |> Seq.toList

