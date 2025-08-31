# Jade - F# Event Sourcing Library

Jade is a functional event sourcing library for F# that provides a clean, type-safe foundation for building event-sourced applications using domain-driven design principles.

## Features

- **Idiomatic F# Aggregates**: Record-of-functions pattern for aggregate behavior
- **Event Metadata & Versioning**: Correlation tracking and schema evolution support
- **Snapshots**: Performance optimization for large event streams
- **Projections**: Read model generation from event streams
- **Sagas/Process Managers**: Long-running business process orchestration
- **Validation Framework**: Composable validation with error aggregation
- **Domain Events**: Publish domain events for external integration
- **Command Bus**: Decoupled command handling with pluggable handlers
- **Marten Integration**: Persistent event storage using PostgreSQL

## Usage

### Define Your Aggregate

```fsharp
type CustomerCommand = Create of CreateCustomer | Update of UpdateCustomer
type CustomerEvent = Created of CustomerCreated | Updated of CustomerUpdated
type CustomerState = { Id: Guid; Name: string; Email: string }

let customerAggregate = {
    create = fun cmd -> 
        match cmd with
        | Create data -> Ok [Created { Id = data.Id; Name = data.Name; Email = data.Email }]
        | _ -> Error "Invalid create command"
    
    decide = fun cmd state ->
        match cmd with 
        | Update data -> Ok [Updated { Id = state.Id; Name = data.Name; Email = data.Email }]
        | _ -> Error "Invalid command"
    
    evolve = fun state event ->
        match event with
        | Created data -> { Id = data.Id; Name = data.Name; Email = data.Email }
        | Updated data -> { state with Name = data.Name; Email = data.Email }
    
    init = { Id = Guid.Empty; Name = ""; Email = "" }
}
```

### Process Commands

```fsharp
let repository = createMartenRepository documentStore
let! result = processCommand repository customerAggregate getId command
```

### Validation

```fsharp
let validateCustomer customer = validation {
    let! name = Validation.notNullOrEmpty "name" customer.Name
    let! email = Validation.email "email" customer.Email
    return { customer with Name = name; Email = email }
}
```

### Snapshots

```fsharp
let strategy = EveryNEvents 100
let snapshot = createSnapshot aggregateId state version
```

### Projections

```fsharp
type CustomerProjectionHandler() =
    interface IProjectionHandler<CustomerEvent, CustomerReadModel> with
        member _.CanHandle event = match event with Created _ | Updated _ -> true
        member _.Handle eventWithMetadata readModel = async {
            // Update read model based on event
            return updatedReadModel
        }
```

## Testing

Run the comprehensive test suite (25+ tests):

```bash
cd tests/Jade.Tests.Unit && dotnet run
cd tests/Jade.Tests.Integration && dotnet run
```

Tests cover validation, event metadata, aggregates, snapshots, projections, sagas, command bus, and Marten integration with PostgreSQL via Testcontainers.

## Getting Started

For a complete walkthrough of building your own event-sourced application, see [GETTING_STARTED.md](GETTING_STARTED.md) which shows how to build a banking application from scratch using Jade.

## Example Application

See `src/Jade.Example.Console` for a complete working example demonstrating the full event sourcing flow with PostgreSQL persistence.

```bash
cd src/Jade.Example.Console && dotnet run
```
