module Jade.Core.CloudEvents

open System
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.SystemTextJson

// Jade extension metadata
[<CLIMutable>]
type JadeExtension = {
    [<JsonPropertyName("correlationId")>]
    CorrelationId: string option
    
    [<JsonPropertyName("causationId")>]
    CausationId: string option
    
    [<JsonPropertyName("userId")>]
    UserId: string option
    
    [<JsonPropertyName("tenantId")>]
    TenantId: string option
}

// CloudEvents specification v1.0 compliant model
// https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md
[<CLIMutable>]
type CloudEvent = {
    // Required fields
    [<JsonPropertyName("id")>]
    Id: string
    
    [<JsonPropertyName("source")>]
    Source: string
    
    [<JsonPropertyName("specversion")>]
    SpecVersion: string
    
    [<JsonPropertyName("type")>]
    Type: string
    
    // Optional fields
    [<JsonPropertyName("datacontenttype")>]
    DataContentType: string option
    
    [<JsonPropertyName("dataschema")>]
    DataSchema: string option
    
    [<JsonPropertyName("subject")>]
    Subject: string option
    
    [<JsonPropertyName("time")>]
    Time: DateTimeOffset option
    
    // The actual event data
    [<JsonPropertyName("data")>]
    Data: JsonElement option
    
    // Jade extension
    [<JsonPropertyName("jade")>]
    Jade: JadeExtension option
}

// Validation
let validateCloudEvent (ce: CloudEvent) : Result<CloudEvent, string> =
    match ce.Id, ce.Source, ce.SpecVersion, ce.Type with
    | null, _, _, _ | "", _, _, _ -> Error "CloudEvent 'id' is required and cannot be empty"
    | _, null, _, _ | _, "", _, _ -> Error "CloudEvent 'source' is required and cannot be empty"
    | _, _, null, _ | _, _, "", _ -> Error "CloudEvent 'specversion' is required and cannot be empty"
    | _, _, _, null | _, _, _, "" -> Error "CloudEvent 'type' is required and cannot be empty"
    | _ when ce.SpecVersion <> "1.0" -> Error $"Unsupported CloudEvents spec version: {ce.SpecVersion}"
    | _ -> Ok ce


// CloudEvent response model
type CloudEventResponse = {
    [<JsonPropertyName("id")>]
    Id: string
    
    [<JsonPropertyName("status")>]
    Status: string
    
    [<JsonPropertyName("message")>]
    Message: string option
}