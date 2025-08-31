module Jade.Domain.MartenConfiguration

open Marten
open Marten.Events.Projections
open JasperFx.Events.Projections
open Jade.Core.MartenConfiguration
open Jade.Domain.Projections.CustomerWithOrders

let configureDomainMarten (options: StoreOptions) =
    configureMartenBase options
    
    // Map Customer event types with their schema URNs - nested module structure
    options.Events.MapEventType<Customer.Event.Created.V1> "urn:schema:jade:event:customer:created:1"
    options.Events.MapEventType<Customer.Event.Created.V2> "urn:schema:jade:event:customer:created:2" 
    options.Events.MapEventType<Customer.Event.Updated.V1> "urn:schema:jade:event:customer:updated:1"
    
    // Map Order event types with their schema URNs - demonstrating nested module structure
    options.Events.MapEventType<Order.Event.Created.V1> "urn:schema:jade:event:order:created:1"
    options.Events.MapEventType<Order.Event.Created.V2> "urn:schema:jade:event:order:created:2"
    options.Events.MapEventType<Order.Event.Cancelled.V1> "urn:schema:jade:event:order:cancelled:1"
    
    // Use async projection - MultiStreamProjection is designed for async processing
    // options.Projections.Add(CustomerWithOrdersProjection(), ProjectionLifecycle.Async)
    options.Projections.Add(CustomerWithOrdersProjection(), ProjectionLifecycle.Inline)
    
    |> ignore