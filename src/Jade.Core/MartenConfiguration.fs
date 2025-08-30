module Jade.Core.MartenConfiguration

open Marten
open System.Text.Json
open System.Text.Json.Serialization

let configureMartenBase (options: StoreOptions) =
    let jsonOptions = JsonSerializerOptions()
    jsonOptions.Converters.Add(JsonFSharpConverter())
    
    let serializer = Marten.Services.SystemTextJsonSerializer jsonOptions
    options.Serializer serializer |> ignore