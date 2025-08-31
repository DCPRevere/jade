module Customer

open System
open Jade.Core.EventSourcing

module Command =
    module Create =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            static member toSchema =
                $"urn:schema:jade:command:customer:create:1"

        type V2 = {
            CustomerId: Guid
            Name: string
            Email: string
            Phone: string
        } with
            static member toSchema =
                $"urn:schema:jade:command:customer:create:2"

    module Update =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            static member toSchema =
                $"urn:schema:jade:command:customer:update:1"

// Keep old aliases for compatibility during transition
type CreateCustomerV1 = Command.Create.V1
type CreateCustomerV2 = Command.Create.V2
type CreateCustomer =
    | V1 of CreateCustomerV1
    | V2 of CreateCustomerV2
type UpdateV1 = Command.Update.V1
type UpdateCustomer =
    | V1 of UpdateV1

type Command =
    | Create of CreateCustomer
    | Update of UpdateCustomer

// Events implementing IEvent interface
module Event =
    module Created =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            interface IEvent
            static member toSchema =
                $"urn:schema:jade:event:customer:created:1"

        type V2 = {
            CustomerId: Guid
            Name: string
            Email: string
            Phone: string
        } with
            interface IEvent
            static member toSchema =
                $"urn:schema:jade:event:customer:created:2"
    
    module Updated =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            interface IEvent
            static member toSchema =
                $"urn:schema:jade:event:customer:updated:1"

// Keep old aliases for compatibility during transition
type CreatedCustomerV1 = Event.Created.V1
type CreatedCustomerV2 = Event.Created.V2
type UpdatedCustomerV1 = Event.Updated.V1

// Use IEvent as the event type
type Event = IEvent

type State = {
    Id: Guid
    Name: string
    Email: string
    Phone: string option
}

// Clean domain functions using pattern matching
let create command : Result<IEvent list, string> =
    match command with
    | Create version -> 
        match version with
        | CreateCustomer.V1 cmd -> 
            Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email } : Event.Created.V1) :> IEvent ]
        | CreateCustomer.V2 cmd -> 
            Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email; Phone = cmd.Phone } : Event.Created.V2) :> IEvent ]
    | Update _ -> Error "Update command cannot be used for creation"

let decide command state : Result<IEvent list, string> =
    match command with
    | Create _ -> Error "Create command cannot be used on existing aggregate"
    | Update version -> 
        match version with
        | UpdateCustomer.V1 cmd -> 
            Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email } : Event.Updated.V1) :> IEvent ]

let init (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | :? Event.Created.V2 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = Some e.Phone }
    | :? Event.Updated.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | _ -> failwithf "Unknown event type: %A" event

let evolve state (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | :? Event.Created.V2 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = Some e.Phone }
    | :? Event.Updated.V1 as e -> { state with Name = e.Name; Email = e.Email }
    | _ -> failwithf "Unknown event type: %A" event

let getId command : Guid =
    match command with
    | Create version -> 
        match version with
        | CreateCustomer.V1 cmd -> cmd.CustomerId
        | CreateCustomer.V2 cmd -> cmd.CustomerId
    | Update version -> 
        match version with
        | UpdateCustomer.V1 cmd -> cmd.CustomerId

let aggregate = {
    create = create
    decide = decide
    init = init
    evolve = evolve
}