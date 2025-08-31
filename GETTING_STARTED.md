# Getting Started with Jade

This guide shows how to build your own event-sourced application using the Jade library.

## 1. Setup Your Project

Create a new F# console application:

```bash
dotnet new console -lang F#
dotnet add package Marten
dotnet add package Npgsql
dotnet add package Testcontainers.PostgreSql
dotnet add reference path/to/Jade.Core.fsproj
```

## 2. Define Your Domain

Let's build a simple bank account system:

```fsharp
// Domain/BankAccount.fs
module BankAccount

open System
open Jade.Core.EventSourcing
open Jade.Core.Validation

// Commands
type AccountCommand =
    | OpenAccount of OpenAccountData
    | Deposit of DepositData  
    | Withdraw of WithdrawData

and OpenAccountData = { AccountId: Guid; InitialBalance: decimal; Owner: string }
and DepositData = { AccountId: Guid; Amount: decimal }
and WithdrawData = { AccountId: Guid; Amount: decimal }

// Events  
type AccountEvent =
    | AccountOpened of AccountOpenedData
    | MoneyDeposited of MoneyDepositedData
    | MoneyWithdrawn of MoneyWithdrawnData

and AccountOpenedData = { AccountId: Guid; InitialBalance: decimal; Owner: string }
and MoneyDepositedData = { AccountId: Guid; Amount: decimal; NewBalance: decimal }
and MoneyWithdrawnData = { AccountId: Guid; Amount: decimal; NewBalance: decimal }

// State
type AccountState = {
    AccountId: Guid
    Balance: decimal
    Owner: string
    IsOpen: bool
}

// Business Logic
let private validateDeposit amount =
    if amount <= 0m then Error "Deposit amount must be positive"
    else Ok amount

let private validateWithdrawal amount balance =
    if amount <= 0m then Error "Withdrawal amount must be positive"
    elif amount > balance then Error "Insufficient funds"
    else Ok amount

// Aggregate Definition
let accountAggregate = {
    create = fun cmd ->
        match cmd with
        | OpenAccount data ->
            validation {
                let! _ = Validation.notNullOrEmpty "owner" data.Owner
                let! _ = Validation.nonNegative "initialBalance" (int data.InitialBalance)
                return [AccountOpened { 
                    AccountId = data.AccountId
                    InitialBalance = data.InitialBalance 
                    Owner = data.Owner 
                }]
            }
        | _ -> Error "Can only open account on new aggregate"

    decide = fun cmd state ->
        if not state.IsOpen then Error "Account is not open"
        else
            match cmd with
            | Deposit data ->
                match validateDeposit data.Amount with
                | Ok amount -> 
                    let newBalance = state.Balance + amount
                    Ok [MoneyDeposited { 
                        AccountId = data.AccountId
                        Amount = amount
                        NewBalance = newBalance 
                    }]
                | Error err -> Error err
            
            | Withdraw data ->
                match validateWithdrawal data.Amount state.Balance with
                | Ok amount ->
                    let newBalance = state.Balance - amount
                    Ok [MoneyWithdrawn { 
                        AccountId = data.AccountId
                        Amount = amount
                        NewBalance = newBalance 
                    }]
                | Error err -> Error err
            
            | _ -> Error "Invalid command for existing account"

    evolve = fun state event ->
        match event with
        | AccountOpened data -> { 
            AccountId = data.AccountId
            Balance = data.InitialBalance
            Owner = data.Owner
            IsOpen = true 
          }
        | MoneyDeposited data -> { state with Balance = data.NewBalance }
        | MoneyWithdrawn data -> { state with Balance = data.NewBalance }

    init = { AccountId = Guid.Empty; Balance = 0m; Owner = ""; IsOpen = false }
}

let getId = function
    | OpenAccount data -> data.AccountId
    | Deposit data -> data.AccountId
    | Withdraw data -> data.AccountId
```

## 3. Create Your Application

```fsharp
// Program.fs
open System
open Marten
open Testcontainers.PostgreSql
open Jade.Core.CommandBus
open Jade.Core.EventSourcing
open BankAccount

// Command Handler
type BankAccountCommandHandler(repository: IAggregateRepository<AccountState, AccountEvent>) =
    interface ICommandHandler<AccountCommand> with
        member _.Handle command = async {
            return! processCommand repository accountAggregate getId command
        }

// Repository Implementation
let createAccountRepository (documentStore: IDocumentStore) =
    { new IAggregateRepository<AccountState, AccountEvent> with
        member _.GetById(aggregateId) = async {
            try
                use session = documentStore.LightweightSession()
                let! streamEvents = session.Events.FetchStreamAsync(aggregateId) |> Async.AwaitTask
                
                if streamEvents.Count = 0 then
                    return Error $"Account {aggregateId} not found"
                else
                    let events = streamEvents |> Seq.map (fun e -> e.Data :?> AccountEvent) |> Seq.toList
                    let state = events |> List.fold accountAggregate.evolve accountAggregate.init
                    let version = streamEvents |> Seq.last |> (fun e -> e.Version)
                    return Ok (state, version)
            with ex -> return Error ex.Message
        }
            
        member _.Save(aggregateId) (eventList) (expectedVersion) = async {
            try
                use session = documentStore.LightweightSession()
                
                if expectedVersion = 0L then
                    session.Events.StartStream(aggregateId, eventList |> List.toArray |> Array.map box) |> ignore
                else
                    session.Events.Append(aggregateId, expectedVersion, eventList |> List.toArray |> Array.map box) |> ignore
                    
                do! session.SaveChangesAsync() |> Async.AwaitTask
                return Ok ()
            with ex -> return Error ex.Message
        }
    }

[<EntryPoint>]
let main argv = async {
    // Setup PostgreSQL
    let postgresContainer = 
        PostgreSqlBuilder()
            .WithDatabase("bankapp")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build()
    
    do! postgresContainer.StartAsync() |> Async.AwaitTask
    
    try
        let connectionString = postgresContainer.GetConnectionString()
        
        // Setup Marten
        let documentStore = 
            DocumentStore.For(fun options ->
                options.Connection(connectionString)
                options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.All
                options.DatabaseSchemaName <- "bank_events"
            )
        
        // Setup Command Bus
        let commandBus = CommandBus()
        let repository = createAccountRepository documentStore
        let handler = BankAccountCommandHandler(repository)
        commandBus.RegisterHandler(handler)
        
        // Use the system
        let accountId = Guid.NewGuid()
        
        printfn "Opening bank account..."
        let openCmd = OpenAccount { AccountId = accountId; InitialBalance = 1000m; Owner = "Alice Smith" }
        let! result1 = commandBus.Send openCmd
        
        match result1 with
        | Ok () -> 
            printfn "Account opened successfully"
            
            printfn "Making deposit..."
            let depositCmd = Deposit { AccountId = accountId; Amount = 500m }
            let! result2 = commandBus.Send depositCmd
            
            match result2 with
            | Ok () ->
                printfn "Deposit successful"
                
                printfn "Making withdrawal..."
                let withdrawCmd = Withdraw { AccountId = accountId; Amount = 200m }
                let! result3 = commandBus.Send withdrawCmd
                
                match result3 with
                | Ok () ->
                    printfn "Withdrawal successful"
                    
                    // Check final balance
                    let! stateResult = repository.GetById accountId
                    match stateResult with
                    | Ok (state, _) ->
                        printfn $"Final balance: ${state.Balance}"
                        printfn $"Account owner: {state.Owner}"
                    | Error err -> printfn $"Error getting state: {err}"
                        
                | Error err -> printfn $"Withdrawal failed: {err}"
            | Error err -> printfn $"Deposit failed: {err}"
        | Error err -> printfn $"Account opening failed: {err}"
        
        documentStore.Dispose()
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
        
        return 0
    with ex ->
        printfn $"Error: {ex.Message}"
        do! postgresContainer.DisposeAsync().AsTask() |> Async.AwaitTask
        return 1
} |> Async.RunSynchronously
```

## 4. Add a Read Model (Projection)

```fsharp
// Projections/AccountSummary.fs
module AccountSummary

open System
open Jade.Core.Projections
open BankAccount

type AccountSummaryReadModel = {
    AccountId: Guid
    Owner: string
    CurrentBalance: decimal
    TransactionCount: int
    LastActivity: DateTimeOffset
}

type AccountSummaryProjectionHandler() =
    interface IProjectionHandler<AccountEvent, AccountSummaryReadModel> with
        member _.CanHandle event = 
            match event with 
            | AccountOpened _ | MoneyDeposited _ | MoneyWithdrawn _ -> true
            
        member _.Handle eventWithMetadata readModel = async {
            let updatedModel = 
                match eventWithMetadata.Event with
                | AccountOpened data -> {
                    AccountId = data.AccountId
                    Owner = data.Owner
                    CurrentBalance = data.InitialBalance
                    TransactionCount = 1
                    LastActivity = eventWithMetadata.Metadata.Timestamp
                  }
                | MoneyDeposited data -> {
                    readModel with 
                        CurrentBalance = data.NewBalance
                        TransactionCount = readModel.TransactionCount + 1
                        LastActivity = eventWithMetadata.Metadata.Timestamp
                  }
                | MoneyWithdrawn data -> {
                    readModel with 
                        CurrentBalance = data.NewBalance
                        TransactionCount = readModel.TransactionCount + 1
                        LastActivity = eventWithMetadata.Metadata.Timestamp
                  }
            
            return updatedModel
        }
```

## 5. Add Business Process (Saga)

```fsharp
// Sagas/LowBalanceNotification.fs
module LowBalanceNotification

open System
open Jade.Core.Sagas
// EventMetadata has been removed - Marten provides built-in event metadata
open BankAccount

type NotificationCommand = SendLowBalanceAlert of Guid * string * decimal

type LowBalanceSagaData = {
    AccountId: Guid
    Owner: string
    LastBalance: decimal
    AlertSent: bool
}

type LowBalanceNotificationHandler() =
    interface ISagaHandler<AccountEvent, NotificationCommand, LowBalanceSagaData> with
        member _.CanHandle event =
            match event with
            | MoneyWithdrawn _ -> true
            | _ -> false
            
        member _.Handle eventWithMetadata sagaData = async {
            match eventWithMetadata.Event with
            | MoneyWithdrawn data when data.NewBalance < 100m && not sagaData.AlertSent ->
                let updatedSaga = { sagaData with LastBalance = data.NewBalance; AlertSent = true }
                let command = SendLowBalanceAlert (data.AccountId, sagaData.Owner, data.NewBalance)
                return (updatedSaga, [command])
            | _ -> 
                return (sagaData, [])
        }
```

## Key Benefits of This Approach

1. **Type Safety**: F# discriminated unions ensure compile-time correctness
2. **Functional**: Pure functions for business logic, immutable state
3. **Testable**: Easy to unit test aggregate behavior without infrastructure
4. **Scalable**: Event sourcing provides natural audit trail and temporal queries
5. **Flexible**: Add projections and sagas without changing core domain logic

## Testing Your Domain

```fsharp
// Tests/BankAccountTests.fs
let accountTests = testList "Bank Account Tests" [
    testCase "can open account with valid data" <| fun _ ->
        let cmd = OpenAccount { AccountId = Guid.NewGuid(); InitialBalance = 100m; Owner = "Test" }
        let result = accountAggregate.create cmd
        
        match result with
        | Ok [AccountOpened data] -> 
            Expect.equal data.InitialBalance 100m "Balance should match"
            Expect.equal data.Owner "Test" "Owner should match"
        | _ -> Tests.failtest "Should create account successfully"
    
    testCase "cannot withdraw more than balance" <| fun _ ->
        let state = { AccountId = Guid.NewGuid(); Balance = 50m; Owner = "Test"; IsOpen = true }
        let cmd = Withdraw { AccountId = state.AccountId; Amount = 100m }
        let result = accountAggregate.decide cmd state
        
        match result with
        | Error "Insufficient funds" -> () // Expected
        | _ -> Tests.failtest "Should reject overdraft"
]
```

This example shows how to build a complete event-sourced banking application using Jade, demonstrating all the key patterns: aggregates, projections, sagas, validation, and testing.