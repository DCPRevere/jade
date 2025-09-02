# Jade - F# Event Sourcing Library

Jade is a functional event sourcing library for F# that provides a clean, type-safe foundation for building event-sourced applications using domain-driven design principles.

## Examples

Jade includes two complete examples demonstrating the full event sourcing flow:

### Console Application

The console example demonstrates the complete command bus flow from command creation through event storage and projection updates. It shows:

Customer and Order aggregate operations
Command processing through the bus
Event persistence to PostgreSQL via Marten
Projection building and querying

Run with: dotnet run --project src/Jade.Example.Console

### API Application

The API provides a CloudEvents-compliant REST endpoint for processing commands. Features include:

CloudEvents v1.0 specification compliance
Schema-based command deserialization
Command routing through the bus
Query endpoint for projections
Automatic PostgreSQL container management in development

Run with: dotnet run --project src/Jade.Example.Api

## Usage

To use Jade in your project, follow these steps:

Define your domain commands and events:

```fsharp
module Customer

module Command =
    module Create =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            interface ICommand
            static member toSchema = "urn:schema:jade:command:customer:create:1"

module Event =
    module Created =
        type V1 = {
            CustomerId: Guid
            Name: string
            Email: string
        } with
            interface IEvent
            static member toSchema = "urn:schema:jade:event:customer:created:1"

type State = {
    Id: Guid
    Name: string
    Email: string
}
```

Implement the aggregate pattern:

```fsharp
let create (command: ICommand) : Result<IEvent list, string> =
    match command with
    | :? Command.Create.V1 as cmd -> 
        Ok [ ({ CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email } : Event.Created.V1) :> IEvent ]
    | _ -> Error "Invalid command for creation"

let decide (command: ICommand) state : Result<IEvent list, string> =
    // Handle commands on existing state
    Error "No update commands defined"

let init (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email }
    | _ -> failwithf "Unknown event type: %A" event

let evolve state (event: IEvent) : State =
    match event with
    | :? Event.Created.V1 as e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email }
    | _ -> state

let aggregate = {
    prefix = "customer"
    create = create
    decide = decide
    init = init
    evolve = evolve
}
```

Set up Marten and register handlers:

```fsharp
open Jade.Core.MartenConfiguration
open Jade.Core.MartenRepository
open Jade.Core.CommandBus
open Jade.Core.CommandRegistry

// Configure Marten with string stream identifiers
let store = DocumentStore.For(fun options ->
    options.Connection(connectionString)
    configureMartenBase options
    options.Events.MapEventType<Customer.Event.Created.V1> "urn:schema:jade:event:customer:created:1"
)

// Create repository and handler
let repository = createRepository store Customer.aggregate
let handler = createHandler repository Customer.aggregate Customer.getId

// Register with command bus
let registry = Registry()
registry.register([typeof<Customer.Command.Create.V1>], handler)
let commandBus = CommandBus(registry.GetHandler)
```

Process commands:

```fsharp
let command = { CustomerId = Guid.NewGuid(); Name = "John"; Email = "john@example.com" }
let! result = commandBus.Send command
```