module Order

open System
open Jade.Core
open Jade.Core.EventSourcing

type OrderItem = {
    ProductId: string
    Quantity: int
    Price: decimal
}

type OrderStatus =
    | Created
    | Cancelled

module Command =
    module Create =
        type V1 = {
            OrderId: string
            CustomerId: string
            Items: OrderItem list
            Metadata: Metadata
        } with
            interface ICommand with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:command:order:create:1"

        type V2 = {
            OrderId: string
            CustomerId: string
            Items: OrderItem list
            PromoCode: string option
            Metadata: Metadata
        } with
            interface ICommand with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:command:order:create:2"

    module Cancel =
        type V1 = {
            OrderId: string
            CustomerId: string
            Metadata: Metadata
        } with
            interface ICommand with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:command:order:cancel:1"

    module SendConfirmation =
        type V1 = {
            OrderId: string
            Metadata: Metadata
        } with
            interface ICommand with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:command:order:send-confirmation:1"

module Event =
    module Created =
        type V1 = {
            OrderId: string
            CustomerId: string
            Items: OrderItem list
            Metadata: Metadata option
        } with
            interface IEvent with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:event:order:created:1"

        type V2 = {
            OrderId: string
            CustomerId: string
            Items: OrderItem list
            PromoCode: string option
            Metadata: Metadata option
        } with
            interface IEvent with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:event:order:created:2"

    module Cancelled =
        type V1 = {
            OrderId: string
            CustomerId: string
            Metadata: Metadata option
        } with
            interface IEvent with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:event:order:cancelled:1"

    module ConfirmationSent =
        type V1 = {
            OrderId: string
            CustomerId: string
            SentAt: System.DateTimeOffset
            Metadata: Metadata option
        } with
            interface IEvent with
                member this.Metadata = this.Metadata
            static member toSchema =
                $"urn:schema:jade:event:order:confirmation-sent:1"


type State = {
    Id: string
    CustomerId: string
    Items: OrderItem list
    Status: OrderStatus
    PromoCode: string option
}

let create (command: ICommand) : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 as cmd ->
        Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items; Metadata = Some cmd.Metadata } : Event.Created.V1) :> IEvent ]
    | :? Command.Create.V2 as cmd ->
        Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items; PromoCode = cmd.PromoCode; Metadata = Some cmd.Metadata } : Event.Created.V2) :> IEvent ]
    | _ -> Error "Cancel command cannot be used for creation"

let decide (command: ICommand) state : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 | :? Command.Create.V2 -> Error "Create command cannot be used on existing aggregate"
    | :? Command.Cancel.V1 as cmd ->
        Ok [ ({ OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Metadata = Some cmd.Metadata } : Event.Cancelled.V1) :> IEvent ]
    | _ -> Error "Unknown command type"

let init (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e ->
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
    | :? Event.Created.V2 as e ->
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = e.PromoCode }
    | _ ->
        eprintfn "Unknown event type: %s" (event.GetType().Name)
        { Id = ""; CustomerId = ""; Items = []; Status = OrderStatus.Created; PromoCode = None }

let evolve state (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e ->
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
    | :? Event.Created.V2 as e ->
        { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = e.PromoCode }
    | :? Event.Cancelled.V1 as _ ->
        { state with Status = OrderStatus.Cancelled }
    | _ ->
        eprintfn "Unknown event type: %s" (event.GetType().Name)
        state

let getId (command: ICommand) : string =
    match command with
    | :? Command.Create.V1 as cmd -> cmd.OrderId
    | :? Command.Create.V2 as cmd -> cmd.OrderId
    | :? Command.Cancel.V1 as cmd -> cmd.OrderId
    | _ ->
        eprintfn "Unknown command type: %s" (command.GetType().Name)
        ""

let canSendConfirmation (state: State) : Result<unit, string> =
    match state.Status with
    | OrderStatus.Created -> Ok ()
    | OrderStatus.Cancelled -> Error "Cannot send confirmation for cancelled order"

let aggregate = {
    prefix = "order"
    create = create
    decide = decide
    init = init
    evolve = evolve
}

