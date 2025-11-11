module Customer

open System
open Jade.Core
open Jade.Core.EventSourcing

module Command =
    module Create =
        type V1 = {
            CustomerId: string
            Name: string
            Email: string
            Metadata: Metadata
        } with
            interface ICommand with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:command:customer:create:1"

        type V2 = {
            CustomerId: string
            Name: string
            Email: string
            Phone: string option
            Metadata: Metadata
        } with
            interface ICommand with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:command:customer:create:2"

    module Update =
        type V1 = {
            CustomerId: string
            Name: string
            Email: string
            Metadata: Metadata
        } with
            interface ICommand with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:command:customer:update:1"

// Events implementing IEvent interface
module Event =
    module Created =
        type V1 = {
            CustomerId: string
            Name: string
            Email: string
            Metadata: Metadata option
        } with
            interface IEvent with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:event:customer:created:1"

        type V2 = {
            CustomerId: string
            Name: string
            Email: string
            Phone: string option
            Metadata: Metadata option
        } with
            interface IEvent with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:event:customer:created:2"

    module Updated =
        type V1 = {
            CustomerId: string
            Name: string
            Email: string
            Metadata: Metadata option
        } with
            interface IEvent with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:event:customer:updated:1"


type State = {
    Id: string
    Name: string
    Email: string
    Phone: string option
}

// Clean domain functions using pattern matching
let create (command: ICommand) : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 as cmd ->
        // V1 commands now produce V2 events with Phone = None for forward compatibility
        Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email; Phone = None; Metadata = Some cmd.Metadata } : Event.Created.V2) :> IEvent ]
    | :? Command.Create.V2 as cmd ->
        Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email; Phone = cmd.Phone; Metadata = Some cmd.Metadata } : Event.Created.V2) :> IEvent ]
    | _ -> Error "Update command cannot be used for creation"

let decide (command: ICommand) state : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 | :? Command.Create.V2 -> Error "Create command cannot be used on existing aggregate"
    | :? Command.Update.V1 as cmd ->
        Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email; Metadata = Some cmd.Metadata } : Event.Updated.V1) :> IEvent ]
    | _ -> Error "Unknown command type"

let init (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | :? Event.Created.V2 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = e.Phone }
    | :? Event.Updated.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | _ ->
        eprintfn "Unknown event type: %s" (event.GetType().Name)
        { Id = ""; Name = ""; Email = ""; Phone = None }

let evolve state (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | :? Event.Created.V2 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = e.Phone }
    | :? Event.Updated.V1 as e -> { state with Name = e.Name; Email = e.Email }
    | _ ->
        eprintfn "Unknown event type: %s" (event.GetType().Name)
        state

let getId (command: ICommand) : string =
    match command with
    | :? Command.Create.V1 as cmd -> cmd.CustomerId
    | :? Command.Create.V2 as cmd -> cmd.CustomerId
    | :? Command.Update.V1 as cmd -> cmd.CustomerId
    | _ ->
        eprintfn "Unknown command type: %s" (command.GetType().Name)
        ""

let aggregate = {
    prefix = "customer"
    create = create
    decide = decide
    init = init
    evolve = evolve
}

