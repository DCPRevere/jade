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

### PGMQ Queue-Based Application

The PGMQ example demonstrates asynchronous, queue-based command processing for scalable, distributed systems. It consists of two services:

**API Service** - Accepts CloudEvents and publishes to PGMQ queues
**Worker Service** - Consumes from queues and processes commands asynchronously

This architecture provides:
- Asynchronous command processing
- Horizontal scalability (multiple workers)
- Automatic retry on failure
- Decoupling of command acceptance from execution
- Per-aggregate queue isolation

Run with:
```bash
# Terminal 1 - API
dotnet run --project src/Jade.Example.Pgmq.Api

# Terminal 2 - Worker
dotnet run --project src/Jade.Example.Pgmq.Worker
```

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

## Queue-Based Processing with PGMQ

For distributed, asynchronous command processing, Jade supports PGMQ (PostgreSQL Message Queue). This architecture separates command acceptance from execution, providing scalability and reliability.

### Architecture

The queue-based approach uses two separate services:

1. **API Service** - Receives CloudEvents via HTTP and publishes to PGMQ queues
2. **Worker Service** - Consumes messages from queues and processes commands

Each aggregate type gets its own queue (e.g., "customer", "order"), enabling isolated scaling and processing.

### API Service Setup

```fsharp
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open System.Text.Json
open Jade.Core.CommandQueue
open Jade.Marten.PgmqCommandPublisher

let builder = WebApplication.CreateBuilder(args)

// Configure JSON with F# support
let jsonOptions = JsonSerializerOptions()
jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
jsonOptions.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())

builder.Services.AddControllers() |> ignore

// Register PGMQ publisher
let pgmqConnectionString = "Host=localhost;Port=5433;Database=jade_pgmq;Username=postgres;Password=postgres"
builder.Services.AddSingleton<ICommandPublisher>(fun sp ->
    let logger = sp.GetRequiredService<ILogger<PgmqCommandPublisher>>()
    PgmqCommandPublisher(pgmqConnectionString, jsonOptions, logger) :> ICommandPublisher
) |> ignore

let app = builder.Build()
app.MapControllers() |> ignore
app.Run()
```

The API automatically provides a `/api/cloudevents` endpoint that accepts CloudEvents and routes them to appropriate queues based on the aggregate type extracted from the `dataschema` field.

### Worker Service Setup

```fsharp
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open System.Text.Json
open Marten
open Jade.Core.CommandRegistry
open Jade.Marten.MartenRepository
open Jade.Marten.PgmqCommandReceiver

let builder = Host.CreateApplicationBuilder(args)

let martenConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass"
let pgmqConnectionString = "Host=localhost;Port=5433;Database=jade_pgmq;Username=postgres;Password=postgres"

// Configure JSON
let jsonOptions = JsonSerializerOptions()
jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
jsonOptions.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())

builder.Services.AddSingleton(jsonOptions) |> ignore

// Configure Marten for event storage
builder.Services.AddMarten(fun options ->
    options.Connection(martenConnectionString)
    options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.CreateOrUpdate
    configureMartenBase jsonOptions options
).UseLightweightSessions() |> ignore

// Register command handlers
builder.Services.AddSingleton<Registry>(fun sp ->
    let documentStore = sp.GetRequiredService<IDocumentStore>()
    let logger = sp.GetRequiredService<ILogger<Registry>>()
    let registry = Registry(logger, jsonOptions)

    let loggerFactory = sp.GetRequiredService<ILoggerFactory>()

    // Create repository and handler for each aggregate
    let customerLogger = loggerFactory.CreateLogger("Customer.Repository")
    let customerRepository = createRepository customerLogger documentStore Customer.aggregate
    let handlerLogger = loggerFactory.CreateLogger("Customer.Handler")
    let customerHandler = createHandler handlerLogger customerRepository Customer.aggregate Customer.getId

    // Register command types with handler
    registry.register([
        typeof<Customer.Command.Create.V1>
        typeof<Customer.Command.Update.V1>
    ], customerHandler)

    registry
) |> ignore

// Create PGMQ receivers - one per aggregate type
builder.Services.AddSingleton<ICommandReceiver list>(fun sp ->
    let loggerFactory = sp.GetRequiredService<ILoggerFactory>()
    let jsonOpts = sp.GetRequiredService<JsonSerializerOptions>()

    // Queue name matches aggregate prefix
    let customerLogger = loggerFactory.CreateLogger<PgmqCommandReceiver>()
    let customerReceiver = PgmqCommandReceiver(pgmqConnectionString, "customer", jsonOpts, customerLogger) :> ICommandReceiver

    [customerReceiver]
) |> ignore

// Register the background worker
builder.Services.AddHostedService<CommandWorker>() |> ignore

let host = builder.Build()
host.Run()
```

### Sending Commands

Send commands as CloudEvents via HTTP POST:

```bash
curl -X POST http://localhost:5000/api/cloudevents \
  -H "Content-Type: application/cloudevents+json" \
  -d '{
    "specversion": "1.0",
    "type": "command",
    "source": "my-app",
    "id": "cmd-123",
    "datacontenttype": "application/json",
    "dataschema": "urn:schema:jade:command:customer:create:1",
    "data": {
      "customerId": "cust-001",
      "name": "John Doe",
      "email": "john@example.com",
      "metadata": {
        "id": "meta-uuid",
        "correlationId": "corr-uuid",
        "causationId": null,
        "userId": null,
        "timestamp": "2025-11-11T10:00:00Z"
      }
    }
  }'
```

### Queue Routing

Queue names are automatically derived from aggregate prefixes:

- Commands for `Customer` aggregate → `customer` queue
- Commands for `Order` aggregate → `order` queue

The `dataschema` URN must follow the pattern: `urn:schema:jade:command:{aggregate}:{action}:{version}`

The aggregate portion determines which queue receives the message.

### Scaling Workers

Run multiple worker instances to process commands in parallel. Each worker will consume from all configured queues, and PGMQ ensures at-most-once processing per message.

### Error Handling

Failed commands are automatically retried by PGMQ. Messages remain in the queue with a visibility timeout, allowing workers to retry processing after the timeout expires.