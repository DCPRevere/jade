# AGENTS.md

This file provides guidance to Claude Code for F# development in this repository.

## General Development Philosophy

Use idiomatic F# patterns and functional programming principles. Follow domain-driven design patterns. Code should be self-documenting - use comments only when the code logic is not immediately clear from reading it.

Maintain a tight development loop - write tests first, implement incrementally, verify changes work as expected before proceeding.

## Technology Stack

- .NET 9
- F# 9 
- C# 13 when interop is required
- Paket for dependency management
- Latest idiomatic testing frameworks (currently Expecto for F# projects)

## Core F# Style Guidelines

### Type Organization
- Domain types, data structures, and shared contracts belong at namespace level for maximum accessibility
- Implementation functions and business logic belong in modules
- Exception: Types tightly coupled to specific module functionality can be defined within the module

### Discriminated Unions Over Enums
Always prefer discriminated unions over enums for better F# idioms and type safety.

```fsharp
// Preferred
type OrderStatus = 
    | Draft | Pending | Confirmed | Shipped | Delivered | Cancelled
    
let getStatusPriority = function
    | Draft -> 1 | Pending -> 2 | Confirmed -> 3
    | Shipped -> 4 | Delivered -> 5 | Cancelled -> 0

// Avoid
type OrderStatus = Draft = 1 | Pending = 2 | Confirmed = 3
```

### Service Architecture Pattern
Use record types with function fields instead of classes for service abstractions.

```fsharp
type OrderService = {
    validateOrder: Order -> ValidationContext -> Async<Result<ValidatedOrder, ValidationError>>
    processPayment: ValidatedOrder -> PaymentProvider -> Async<Result<PaymentResult, PaymentError>>
    fulfillOrder: ValidatedOrder -> PaymentResult -> Async<Result<FulfilledOrder, FulfillmentError>>
}

let createOrderService (paymentGateway: IPaymentGateway) (inventory: IInventory) : OrderService =
    let validateOrderImpl order context = async {
        // implementation
    }
    
    {
        validateOrder = validateOrderImpl
        processPayment = processPaymentImpl  
        fulfillOrder = fulfillOrderImpl
    }
```

### Error Handling Standards
All API operations must return `Async<Result<T, string>>` where T is the success type. Server implementations must wrap operations in try/catch and return proper Result types. Never use `failwith` in API implementations.

```fsharp
// API contract
let createOrder: CreateOrderRequest -> Async<Result<OrderId, string>>

// Server implementation
let createOrderImpl request = async {
    try
        // implementation logic
        return Ok orderId
    with
    | ex -> return Error ex.Message
}

// Client usage
match! orderService.createOrder request with
| Ok orderId -> return orderId
| Error err -> return Error $"Order creation failed: {err}"
```

### Function Size Guidelines
Target 10-15 lines per function. Up to 25 lines is acceptable. Beyond 30 lines should be refactored unless there's a specific reason like tightly coupled sequential operations or extensive pattern matching.

### Pattern Matching and Lists
Proper indentation prevents compiler errors in match expressions and complex list construction.

```fsharp
// Match expressions
let resultType =
    match command.Type with
    | ProcessCommand _ -> "Process"
    | ValidateCommand _ -> "Validate"

// Complex list expressions
let items = 
    [ match value with
      | Case1 -> "result1" 
      | Case2 -> "result2"
      
      if condition then
          "extra"
      else
          ""
    ]
    |> List.filter (String.IsNullOrEmpty >> not)
```

### String Interpolation Guidelines
For complex interpolated expressions use let bindings or triple-quote strings to avoid compiler warnings.

```fsharp
// Use let bindings for complex conditions
let status = if result.Success then "Success" else "Failure"
let message = $"Processing: {status} (value {result.Value}, time {result.ElapsedMs} ms)"

// Triple-quote strings for complex expressions
let formatted = $"""Step {result.Step}: {if result.Success then "✓" else "✗"}"""

// Escape percentage signs in format specifiers
let percentage = $"Success Rate: {rate:F1}%%"
```

## Testing Strategy

### Framework and Execution
Use Expecto testing framework. Always use `dotnet run` instead of `dotnet test` for running Expecto tests.

```fsharp
// Test filtering options
--filter               // Filters by slash-separated hierarchy
--filter-test-list     // Filters test lists by substring  
--filter-test-case     // Filters test cases by substring
```

### Async Testing
Always use `testCaseAsync` for async operations. Never use `Async.RunSynchronously` in tests.

```fsharp
// Correct async testing
testCaseAsync "Can process order" <| async {
    let! result = orderService.processOrder order
    Expect.isOk result "Order processing should succeed"
}
```

### Test-Driven Development
1. Write failing tests first
2. Implement with stubbed return values
3. Implement functions progressively
4. Run tests to verify each change
5. Refactor while maintaining green tests

### Property-Based Testing
Use FsCheck for property-based testing when appropriate, especially for testing invariants and edge cases in domain logic.

## JSON Configuration

Centralize all JSON serialization configuration in one location to ensure consistent behavior across the application. Typically configure System.Text.Json options at application startup and reuse the same JsonSerializerOptions instance throughout.

```fsharp
module JsonConfig =
    let options = JsonSerializerOptions()
    options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    options.WriteIndented <- true
    // Configure once, use everywhere
```

## Security Requirements

### Database Queries
ALL database queries must use parameterized queries. Never use string interpolation in query strings.

```fsharp
// Secure parameterized queries
let queryText = "SELECT * FROM c WHERE c.userId = @userId AND c.documentType = @docType"
let queryDefinition = 
    QueryDefinition(queryText)
        .WithParameter("@userId", string userId)
        .WithParameter("@docType", "User")

// For discriminated union fields, use square bracket notation
let queryText = "SELECT * FROM c WHERE c.data.status['case'] = @statusType"
let queryDefinition = 
    QueryDefinition(queryText)
        .WithParameter("@statusType", "Active")
```

## Result

Prefer to use `Result (Ok, Error)` instead of `failwith`.

## Async and Parallel Processing

Use proper async patterns and parallel processing when needed.

```fsharp
let! resultsArray = 
    batch.Commands
    |> List.map processor.validateData
    |> Async.Parallel

let results = Array.toList resultsArray
```

## Paket Configuration

Use paket for dependency management. Keep paket.dependencies file clean and organized by grouping related dependencies.

## Logging

Use structured logging with parameters, not string interpolation.

```fsharp
Log.Information("Processing order {OrderId} for customer {CustomerId}", orderId, customerId)
Log.Error(ex, "Failed to process {OrderId}", orderId)
```

## Testing

Keep Arrange Act Assert comments in tests for clarity and structure.

## Git Workflow

Never push directly to protected branches. Use feature branches and pull requests. Use `gh` CLI for GitHub operations when available.

Create descriptive commit messages that explain what changed and why, not when the change was made.

D C P Revere (dcprevere@gmail.com) should be the only committer on any git commit you make.