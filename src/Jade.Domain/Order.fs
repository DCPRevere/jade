module Order

open System
open Jade.Core.EventSourcing

type OrderItem = {
    ProductId: Guid
    Quantity: int
    Price: decimal
}

type OrderStatus =
    | Created
    | Cancelled

// Domain-specific command interface
type IOrderCommand = 
    inherit ICommand

module Command =
    module Create =
        type V1 = {
            OrderId: Guid
            CustomerId: Guid
            Items: OrderItem list
        } with
            interface IOrderCommand
            static member toSchema =
                $"urn:schema:jade:command:order:create:1"

        type V2 = {
            OrderId: Guid
            CustomerId: Guid
            Items: OrderItem list
            PromoCode: string option
        } with
            interface IOrderCommand
            static member toSchema =
                $"urn:schema:jade:command:order:create:2"

    module Cancel =
        type V1 = {
            OrderId: Guid
            CustomerId: Guid
        } with
            interface IOrderCommand
            static member toSchema =
                $"urn:schema:jade:command:order:cancel:1"

// Keep old aliases for compatibility during transition
type CreateOrderV1 = Command.Create.V1
type CreateOrderV2 = Command.Create.V2
type CreateOrder =
    | V1 of CreateOrderV1
    | V2 of CreateOrderV2
type CancelOrderV1 = Command.Cancel.V1
type CancelOrder =
    | V1 of CancelOrderV1

// Use domain-specific interface as the command type
type Command = IOrderCommand

// Events implementing IEvent interface
module Event =
    module Created =
        type V1 = {
            OrderId: Guid
            CustomerId: Guid
            Items: OrderItem list
        } with
            interface IEvent
            static member toSchema =
                $"urn:schema:jade:event:order:created:1"
        
        type V2 = {
            OrderId: Guid
            CustomerId: Guid
            Items: OrderItem list
            PromoCode: string option
        } with
            interface IEvent
            static member toSchema =
                $"urn:schema:jade:event:order:created:2"
    
    module Cancelled =
        type V1 = {
            OrderId: Guid
            CustomerId: Guid
        } with
            interface IEvent
            static member toSchema =
                $"urn:schema:jade:event:order:cancelled:1"

// Keep old aliases for compatibility during transition
type CreatedOrderV1 = Event.Created.V1
type CreatedOrderV2 = Event.Created.V2
type CancelledOrderV1 = Event.Cancelled.V1

// Use IEvent as the event type
type Event = IEvent

type State = {
    Id: Guid
    CustomerId: Guid
    Items: OrderItem list
    Status: OrderStatus
    PromoCode: string option
}

// Clean domain functions using pattern matching
let create (command: ICommand) : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 as cmd -> 
        Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items } : Event.Created.V1) :> IEvent ]
    | :? Command.Create.V2 as cmd -> 
        Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items; PromoCode = cmd.PromoCode } : Event.Created.V2) :> IEvent ]
    | _ -> Error "Cancel command cannot be used for creation"

let decide (command: ICommand) state : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 | :? Command.Create.V2 -> Error "Create command cannot be used on existing aggregate"
    | :? Command.Cancel.V1 as cmd -> 
        Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId } : Event.Cancelled.V1) :> IEvent ]
    | _ -> Error "Unknown command type"

let init (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
    | :? Event.Created.V2 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = e.PromoCode }
    | :? Event.Cancelled.V1 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = []; Status = OrderStatus.Cancelled; PromoCode = None }
    | _ -> failwithf "Unknown event type: %A" event

let evolve state (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
    | :? Event.Created.V2 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = e.PromoCode }
    | :? Event.Cancelled.V1 as _ -> 
        { state with Status = OrderStatus.Cancelled }
    | _ -> failwithf "Unknown event type: %A" event

let getId (command: ICommand) : Guid =
    match command with
    | :? Command.Create.V1 as cmd -> cmd.OrderId
    | :? Command.Create.V2 as cmd -> cmd.OrderId
    | :? Command.Cancel.V1 as cmd -> cmd.OrderId
    | _ -> failwithf "Unknown command type: %A" command

let aggregate = {
    create = create
    decide = decide
    init = init
    evolve = evolve
}

// Command handler with knowledge of which command types it handles
type OrderCommandHandler(repository: IAggregateRepository<State, IEvent>) =
    let handler = AggregateCommandHandler(repository, aggregate, getId, "📦 ORDER")
    
    // Map of command types this handler can process
    let commandTypes = [
        typeof<Command.Create.V1>
        typeof<Command.Create.V2>
        typeof<Command.Cancel.V1>
    ]
    
    interface Jade.Core.CommandBus.IDomainCommandHandler with
        member _.CommandTypes = commandTypes
        member _.Handle command = 
            (handler :> ICommandHandler<ICommand>).Handle (command :?> ICommand)