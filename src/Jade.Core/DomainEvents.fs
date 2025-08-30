module Jade.Core.DomainEvents

open System
open Jade.Core.EventMetadata

/// Domain event that represents something that happened in the domain
type IDomainEvent =
    abstract member EventId: Guid
    abstract member OccurredAt: DateTimeOffset

/// Domain event publisher for publishing events to external systems
type IDomainEventPublisher =
    abstract member Publish: IDomainEvent list -> Async<Result<unit, string>>

/// In-memory domain event publisher for testing
type InMemoryDomainEventPublisher() =
    let mutable publishedEvents: IDomainEvent list = []
    
    member this.PublishedEvents = publishedEvents
    member this.Clear() = publishedEvents <- []
    
    interface IDomainEventPublisher with
        member this.Publish(events: IDomainEvent list) = async {
            publishedEvents <- publishedEvents @ events
            return Ok ()
        }

/// Domain event dispatcher that coordinates publishing
type DomainEventDispatcher(publisher: IDomainEventPublisher) =
    
    member this.PublishEvents(events: IDomainEvent list) = async {
        if not (List.isEmpty events) then
            return! publisher.Publish events
        else
            return Ok ()
    }
    
    member this.PublishEvent(event: IDomainEvent) = async {
        return! this.PublishEvents [event]
    }

/// Helper to create a simple domain event
let createDomainEvent<'T when 'T :> IDomainEvent> (createEvent: Guid -> DateTimeOffset -> 'T) = 
    createEvent (Guid.NewGuid()) DateTimeOffset.UtcNow

/// Aggregate base that supports domain events
type AggregateWithDomainEvents<'State>() =
    let mutable domainEvents: IDomainEvent list = []
    
    member this.AddDomainEvent(event: IDomainEvent) =
        domainEvents <- event :: domainEvents
    
    member this.GetDomainEvents() = List.rev domainEvents
    
    member this.ClearDomainEvents() =
        domainEvents <- []
    
    member this.HasDomainEvents = not (List.isEmpty domainEvents)