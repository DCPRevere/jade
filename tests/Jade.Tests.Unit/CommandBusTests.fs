module CommandBusTests

open Expecto
open Jade.Core.CommandBus
open Jade.Core.EventSourcing
open System

type TestCommand = {
    Id: Guid
    Message: string
}

type TestCommandHandler(expectedCommand: TestCommand, result: Result<unit, string>) =
    let mutable receivedCommand = None
    
    member this.ReceivedCommand = receivedCommand
    
    interface IHandler with
        member this.Handle command = async {
            receivedCommand <- Some (command :?> TestCommand)
            return result
        }

[<Tests>]
let commandBusTests =
    testList "CommandBus Tests" [
        
        testCaseAsync "Send command without registered handler returns error" <| async {
            let getHandler (_: Type) = None
            let bus = CommandBus(getHandler)
            let command = { Id = Guid.NewGuid(); Message = "test" }
            
            let! result = bus.Send command
            
            Expect.isError result "Should return error when no handler registered"
            match result with
            | Error msg -> Expect.stringContains msg "No handler registered" "Error message should mention no handler"
            | Ok _ -> failwith "Should not succeed"
        }
        
        testCaseAsync "Send command with registered handler calls handler" <| async {
            let command = { Id = Guid.NewGuid(); Message = "test message" }
            let handler = TestCommandHandler(command, Ok ())
            let getHandler (commandType: Type) = 
                if commandType = typeof<TestCommand> then Some (handler :> IHandler) else None
            let bus = CommandBus(getHandler)
            
            let! result = bus.Send command
            
            Expect.isOk result "Should succeed when handler is registered"
            Expect.equal handler.ReceivedCommand (Some command) "Handler should receive the command"
        }
        
        testCaseAsync "Send command with handler that returns error" <| async {
            let command = { Id = Guid.NewGuid(); Message = "test" }
            let expectedError = "Handler error"
            let handler = TestCommandHandler(command, Error expectedError)
            let getHandler (commandType: Type) = 
                if commandType = typeof<TestCommand> then Some (handler :> IHandler) else None
            let bus = CommandBus(getHandler)
            
            let! result = bus.Send command
            
            Expect.isError result "Should return error when handler fails"
            match result with
            | Error msg -> Expect.equal msg expectedError "Should return handler error"
            | Ok _ -> failwith "Should not succeed"
        }
    ]