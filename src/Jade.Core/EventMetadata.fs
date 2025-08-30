module Jade.Core.EventMetadata

open System

/// Metadata associated with every event
type EventMetadata = {
    /// Unique identifier for the event
    EventId: Guid
    
    /// Correlation identifier for tracking related events/commands
    CorrelationId: Guid
    
    /// Causation identifier for tracking what caused this event
    CausationId: Guid
    
    /// When the event occurred
    Timestamp: DateTimeOffset
    
    /// Version of the event schema
    SchemaVersion: int
    
    /// User or system that caused the event
    UserId: string option
    
    /// Additional metadata as key-value pairs
    Metadata: Map<string, string>
}

/// Event with metadata wrapper
type EventWithMetadata<'Event> = {
    Event: 'Event
    Metadata: EventMetadata
}

/// Helper to create event metadata
let createEventMetadata 
    (correlationId: Guid option) 
    (causationId: Guid option) 
    (userId: string option) 
    (schemaVersion: int option)
    (metadata: Map<string, string> option) =
    {
        EventId = Guid.NewGuid()
        CorrelationId = defaultArg correlationId (Guid.NewGuid())
        CausationId = defaultArg causationId (Guid.NewGuid())
        Timestamp = DateTimeOffset.UtcNow
        SchemaVersion = defaultArg schemaVersion 1
        UserId = userId
        Metadata = defaultArg metadata Map.empty
    }

/// Helper to wrap event with metadata
let wrapEventWithMetadata event metadata = {
    Event = event
    Metadata = metadata
}