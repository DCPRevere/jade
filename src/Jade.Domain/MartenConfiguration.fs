module Jade.Domain.MartenConfiguration

open Marten
open Jade.Core.MartenConfiguration

let configureDomainMarten (options: StoreOptions) =
    configureMartenBase options
    
    options.Events.MapEventType<Customer._Event> "Customer.Event"
    options.Events.MapEventType<Order._Event> "Order.Event"
    
    options.Events.MapEventType<Customer.CreatedCustomerV1> Customer.CreatedCustomerV1.toSchema
    options.Events.MapEventType<Customer.CreatedCustomerV2> Customer.CreatedCustomerV2.toSchema
    options.Events.MapEventType<Customer.UpdatedCustomerV1> Customer.UpdatedCustomerV1.toSchema
    options.Events.MapEventType<Order.CreatedOrderV1> Order.CreatedOrderV1.toSchema
    options.Events.MapEventType<Order.CreatedOrderV2> Order.CreatedOrderV2.toSchema
    options.Events.MapEventType<Order.CancelledOrderV1> Order.CancelledOrderV1.toSchema
    |> ignore