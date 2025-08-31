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

module Command =
    module Create =
        type V1 = {
            OrderId: Guid
            CustomerId: Guid
            Items: OrderItem list
        } with
            static member toSchema =
                $"urn:schema:jade:command:order:create:1"

        type V2 = {
            OrderId: Guid
            CustomerId: Guid
            Items: OrderItem list
            PromoCode: string
        } with
            static member toSchema =
                $"urn:schema:jade:command:order:create:2"

    module Cancel =
        type V1 = {
            OrderId: Guid
        } with
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

type Command =
    | Create of CreateOrder
    | Cancel of CancelOrder

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
            PromoCode: string
        } with
            interface IEvent
            static member toSchema =
                $"urn:schema:jade:event:order:created:2"
    
    module Cancelled =
        type V1 = {
            OrderId: Guid
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
let create command : Result<IEvent list, string> =
    match command with
    | Create version -> 
        match version with
        | CreateOrder.V1 cmd -> 
            Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items } : Event.Created.V1) :> IEvent ]
        | CreateOrder.V2 cmd -> 
            Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items; PromoCode = cmd.PromoCode } : Event.Created.V2) :> IEvent ]
    | Cancel _ -> Error "Cancel command cannot be used for creation"

let decide command state : Result<IEvent list, string> =
    match command with
    | Create _ -> Error "Create command cannot be used on existing aggregate"
    | Cancel version -> 
        match version with
        | CancelOrder.V1 cmd -> 
            Ok [ ({ OrderId = cmd.OrderId } : Event.Cancelled.V1) :> IEvent ]

let init (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
    | :? Event.Created.V2 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = Some e.PromoCode }
    | :? Event.Cancelled.V1 as e -> 
        { Id = e.OrderId; CustomerId = Guid.Empty; Items = []; Status = OrderStatus.Cancelled; PromoCode = None }
    | _ -> failwithf "Unknown event type: %A" event

let evolve state (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
    | :? Event.Created.V2 as e -> 
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = Some e.PromoCode }
    | :? Event.Cancelled.V1 as _ -> 
        { state with Status = OrderStatus.Cancelled }
    | _ -> failwithf "Unknown event type: %A" event

let getId command : Guid =
    match command with
    | Create version -> 
        match version with
        | CreateOrder.V1 cmd -> cmd.OrderId
        | CreateOrder.V2 cmd -> cmd.OrderId
    | Cancel version -> 
        match version with
        | CancelOrder.V1 cmd -> cmd.OrderId

let aggregate = {
    create = create
    decide = decide
    init = init
    evolve = evolve
}