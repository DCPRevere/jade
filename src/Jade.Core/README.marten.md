# Jade.Marten

Demonstrates event sourcing with Marten from F#.

## Features Demonstrated

- **Domain Events**: CustomerEvent and OrderEvent types with pattern matching
- **Event Versioning**: Immutable event evolution with backward compatibility
- **Aggregates**: Customer and Order state types with apply functions
- **Event Store**: Abstraction over Marten's event store with F#-friendly interface
- **Projections**: Both Marten-based and in-memory projection examples
- **Configuration**: Marten document store setup for F#

## Running

```bash
dotnet run
```

The demo will attempt to connect to PostgreSQL, but gracefully falls back to in-memory processing if unavailable.

## Dependencies

- Marten 7.33.1 - Event sourcing framework for PostgreSQL
- Npgsql 9.0.0 - PostgreSQL driver

## Marten Docs

https://martendb.io/events/quickstart.html
