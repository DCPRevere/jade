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
            interface ICommand
            static member toSchema =
                $"urn:schema:jade:command:order:create:1"

        type V2 = {
            OrderId: Guid
            CustomerId: Guid
            Items: OrderItem list
            PromoCode: string option
        } with
            interface ICommand
            static member toSchema =
                $"urn:schema:jade:command:order:create:2"

    module Cancel =
        type V1 = {
            OrderId: Guid
            CustomerId: Guid
        } with
            interface ICommand
            static member toSchema =
                $"urn:schema:jade:command:order:cancel:1"

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


type State = {
    Id: Guid
    CustomerId: Guid
    Items: OrderItem list
    Status: OrderStatus
    PromoCode: string option
}

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
    prefix = "order"
    create = create
    decide = decide
    init = init
    evolve = evolve
}

