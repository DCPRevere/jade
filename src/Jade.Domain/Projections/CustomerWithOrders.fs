module Jade.Domain.Projections.CustomerWithOrders

open System
open Marten.Events.Projections
open Serilog

type OrderStatus = 
    | Created
    | Cancelled

[<CLIMutable>]
type OrderSummary = {
    OrderId: Guid
    Status: OrderStatus
    TotalValue: decimal
    PromoCode: string option
}

[<CLIMutable>]
type CustomerWithOrders = {
    Id: Guid
    CustomerId: Guid
    Name: string
    Email: string
    Phone: string option
    Orders: OrderSummary list
}

type CustomerWithOrdersProjection() as this =
    inherit MultiStreamProjection<CustomerWithOrders, Guid>()
    
    do
        // Set up identity routing more carefully
        try
            this.Identity<Customer.Event.Created.V1>(fun e -> e.CustomerId)
            this.Identity<Customer.Event.Created.V2>(fun e -> e.CustomerId)  
            this.Identity<Customer.Event.Updated.V1>(fun e -> e.CustomerId)
            this.Identity<Order.Event.Created.V1>(fun e -> e.CustomerId)
            this.Identity<Order.Event.Created.V2>(fun e -> e.CustomerId)
            this.Identity<Order.Event.Cancelled.V1>(fun e -> e.CustomerId)
        with
        | ex -> 
            Log.Error(ex, "Error setting up projection identity routing")
            reraise()
    
    // Customer events - create and update the document
    member this.Create(event: Customer.Event.Created.V1) : CustomerWithOrders =
        Log.Information("🔄 PROJECTION: Creating CustomerWithOrders from Customer.Created.V1: {CustomerId}", event.CustomerId)
        {
            Id = event.CustomerId
            CustomerId = event.CustomerId
            Name = event.Name
            Email = event.Email
            Phone = None
            Orders = []
        }

    member this.Create(event: Customer.Event.Created.V2) : CustomerWithOrders =
        try
            Log.Information("🔄 PROJECTION: Creating CustomerWithOrders from Customer.Created.V2: {CustomerId}", event.CustomerId)
            
            let result = {
                Id = event.CustomerId
                CustomerId = event.CustomerId
                Name = event.Name
                Email = event.Email
                Phone = event.Phone
                Orders = []
            }
            Log.Information("🔄 PROJECTION: Successfully created CustomerWithOrders with Id: {Id}", result.Id)
            result
        with
        | ex ->
            Log.Error(ex, "🔄 PROJECTION: Error in Create method: {Message} | StackTrace: {StackTrace}", ex.Message, ex.StackTrace)
            reraise()

    member this.Apply(event: Customer.Event.Updated.V1, current: CustomerWithOrders) : CustomerWithOrders =
        Log.Information("🔄 PROJECTION: Applying Customer.Updated.V1 to CustomerWithOrders: {CustomerId}", event.CustomerId)
        { current with
            Name = event.Name
            Email = event.Email }

    // Order events - add to or modify the orders list in the same document
    member this.Apply(event: Order.Event.Created.V1, current: CustomerWithOrders) : CustomerWithOrders =
        Log.Information("🔄 PROJECTION: Adding Order.Created.V1 to CustomerWithOrders: {OrderId} for {CustomerId}", event.OrderId, event.CustomerId)
        let orderSummary = {
            OrderId = event.OrderId
            Status = Created
            TotalValue = event.Items |> List.sumBy (fun i -> i.Price * decimal i.Quantity)
            PromoCode = None
        }
        { current with Orders = orderSummary :: current.Orders }

    member this.Apply(event: Order.Event.Created.V2, current: CustomerWithOrders) : CustomerWithOrders =
        Log.Information("🔄 PROJECTION: Adding Order.Created.V2 to CustomerWithOrders: {OrderId} for {CustomerId}", event.OrderId, event.CustomerId)
        let orderSummary = {
            OrderId = event.OrderId
            Status = Created
            TotalValue = event.Items |> List.sumBy (fun i -> i.Price * decimal i.Quantity)
            PromoCode = event.PromoCode
        }
        { current with Orders = orderSummary :: current.Orders }

    member this.Apply(event: Order.Event.Cancelled.V1, current: CustomerWithOrders) : CustomerWithOrders =
        Log.Information("🔄 PROJECTION: Cancelling Order in CustomerWithOrders: {OrderId} for {CustomerId}", event.OrderId, event.CustomerId)
        let updatedOrders = 
            current.Orders 
            |> List.map (fun order -> 
                if order.OrderId = event.OrderId then
                    { order with Status = Cancelled }
                else
                    order)
        { current with Orders = updatedOrders }