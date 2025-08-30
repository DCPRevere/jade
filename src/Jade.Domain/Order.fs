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

type CreateOrderV1 = {
    OrderId: Guid
    CustomerId: Guid
    Items: OrderItem list
} with
    static member toSchema =
        $"urn:schema:jade:command:order:create:1"

type CreateOrderV2 = {
    OrderId: Guid
    CustomerId: Guid
    Items: OrderItem list
    PromoCode: string
} with
    static member toSchema =
        $"urn:schema:jade:command:order:create:2"

type CreateOrder =
    | V1 of CreateOrderV1
    | V2 of CreateOrderV2

type CancelOrderV1 = {
    OrderId: Guid
} with
    static member toSchema =
        $"urn:schema:jade:command:order:cancel:1"

type CancelOrder =
    | V1 of CancelOrderV1

type Command =
    | Create of CreateOrder
    | Cancel of CancelOrder

type CreatedOrderV1 = {
    OrderId: Guid
    CustomerId: Guid
    Items: OrderItem list
} with
    static member toSchema =
        $"urn:schema:jade:event:order:created:1"

type CreatedOrderV2 = {
    OrderId: Guid
    CustomerId: Guid
    Items: OrderItem list
    PromoCode: string
} with
    static member toSchema =
        $"urn:schema:jade:event:order:created:2"

type CreatedOrder =
    | V1 of CreatedOrderV1
    | V2 of CreatedOrderV2

type CancelledOrderV1 = {
    OrderId: Guid
} with
    static member toSchema =
        $"urn:schema:jade:event:order:cancelled:1"

type CancelledOrder =
    | V1 of CancelledOrderV1

type _Event =
    | Created of CreatedOrder
    | Cancelled of CancelledOrder

type State = {
    Id: Guid
    CustomerId: Guid
    Items: OrderItem list
    Status: OrderStatus
    PromoCode: string option
}

// Clean domain functions using pattern matching
let create command : Result<_Event list, string> =
    match command with
    | Create version -> 
        match version with
        | CreateOrder.V1 cmd -> Ok [ Created (CreatedOrder.V1 { OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items }) ]
        | CreateOrder.V2 cmd -> Ok [ Created (CreatedOrder.V2 { OrderId = cmd.OrderId; CustomerId = cmd.CustomerId; Items = cmd.Items; PromoCode = cmd.PromoCode }) ]
    | Cancel _ -> Error "Cancel command cannot be used for creation"

let decide command state : Result<_Event list, string> =
    match command with
    | Create _ -> Error "Create command cannot be used on existing aggregate"
    | Cancel version -> 
        match version with
        | CancelOrder.V1 cmd -> Ok [ Cancelled (CancelledOrder.V1 { OrderId = cmd.OrderId }) ]

let init event : State =
    match event with
    | Created version -> 
        match version with
        | CreatedOrder.V1 e -> { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
        | CreatedOrder.V2 e -> { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = Some e.PromoCode }
    | Cancelled version -> 
        match version with
        | CancelledOrder.V1 e -> { Id = e.OrderId; CustomerId = Guid.Empty; Items = []; Status = OrderStatus.Cancelled; PromoCode = None }

let evolve state event : State =
    match event with
    | Created version -> 
        match version with
        | CreatedOrder.V1 e -> { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = None }
        | CreatedOrder.V2 e -> { Id = e.OrderId; CustomerId = e.CustomerId; Items = e.Items; Status = OrderStatus.Created; PromoCode = Some e.PromoCode }
    | Cancelled version -> 
        match version with
        | CancelledOrder.V1 _ -> { state with Status = OrderStatus.Cancelled }

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