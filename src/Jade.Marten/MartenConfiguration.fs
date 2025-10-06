module Jade.Marten.MartenConfiguration

open Marten
open JasperFx.Events
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.SystemTextJson

let configureMartenBase (jsonOptions: JsonSerializerOptions) (options: StoreOptions) =
    let serializer = Marten.Services.SystemTextJsonSerializer jsonOptions
    options.Serializer serializer |> ignore

    // Configure event store to use string stream identifiers
    options.Events.StreamIdentity <- StreamIdentity.AsString