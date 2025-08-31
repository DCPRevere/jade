module Customer

open System
open Jade.Core.EventSourcing

// Domain-specific command interface
type ICustomerCommand = 
    inherit ICommand

module Command =
    module Create =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            interface ICustomerCommand
            static member toSchema =
                $"urn:schema:jade:command:customer:create:1"

        type V2 = {
            CustomerId: Guid
            Name: string
            Email: string
            Phone: string option
        } with
            interface ICustomerCommand
            static member toSchema =
                $"urn:schema:jade:command:customer:create:2"

    module Update =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            interface ICustomerCommand
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

// Use domain-specific interface as the command type
type Command = ICustomerCommand

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
            Phone: string option
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
let create (command: ICommand) : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 as cmd -> 
        // V1 commands now produce V2 events with Phone = None for forward compatibility
        Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email; Phone = None } : Event.Created.V2) :> IEvent ]
    | :? Command.Create.V2 as cmd -> 
        Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email; Phone = cmd.Phone } : Event.Created.V2) :> IEvent ]
    | _ -> Error "Update command cannot be used for creation"

let decide (command: ICommand) state : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 | :? Command.Create.V2 -> Error "Create command cannot be used on existing aggregate"
    | :? Command.Update.V1 as cmd -> 
        Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email } : Event.Updated.V1) :> IEvent ]
    | _ -> Error "Unknown command type"

let init (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | :? Event.Created.V2 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = e.Phone }
    | :? Event.Updated.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | _ -> failwithf "Unknown event type: %A" event

let evolve state (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
    | :? Event.Created.V2 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = e.Phone }
    | :? Event.Updated.V1 as e -> { state with Name = e.Name; Email = e.Email }
    | _ -> failwithf "Unknown event type: %A" event

let getId (command: ICommand) : Guid =
    match command with
    | :? Command.Create.V1 as cmd -> cmd.CustomerId
    | :? Command.Create.V2 as cmd -> cmd.CustomerId
    | :? Command.Update.V1 as cmd -> cmd.CustomerId
    | _ -> failwithf "Unknown command type: %A" command

let aggregate = {
    create = create
    decide = decide
    init = init
    evolve = evolve
}

// Command handler with knowledge of which command types it handles
type CustomerCommandHandler(repository: IAggregateRepository<State, IEvent>) =
    let handler = AggregateCommandHandler(repository, aggregate, getId, "👤 CUSTOMER")
    
    // Map of command types this handler can process
    let commandTypes = [
        typeof<Command.Create.V1>
        typeof<Command.Create.V2>
        typeof<Command.Update.V1>
    ]
    
    interface Jade.Core.CommandBus.IDomainCommandHandler with
        member _.CommandTypes = commandTypes
        member _.Handle command = 
            (handler :> ICommandHandler<ICommand>).Handle (command :?> ICommand)