module Jade.Core.CommandBus

open System
open Jade.Core.EventSourcing

type ICommandBus =
    abstract member Send: 'Command -> Async<Result<unit, string>>
    abstract member RegisterHandler<'Command>: ICommandHandler<'Command> -> unit

type CommandBus() =
    let handlers = System.Collections.Generic.Dictionary<System.Type, obj -> Async<Result<unit, string>>>()
    
    member _.Send command = async {
        let commandType = command.GetType()
        
        // Try direct type match first
        match handlers.TryGetValue commandType with
        | true, handler -> return! handler (box command)
        | false, _ -> 
            // Try base type for F# discriminated unions
            let baseType = commandType.BaseType
            if not (isNull baseType) then
                match handlers.TryGetValue baseType with
                | true, handler -> return! handler (box command)
                | false, _ -> return Error $"No handler registered for command type {commandType.Name} or its base type {baseType.Name}"
            else
                return Error $"No handler registered for command type {commandType.Name}"
    }
    
    member _.RegisterHandler<'Command> (handler: ICommandHandler<'Command>) =
        let handlerFunc = fun (cmd: obj) -> async {
            let typedCmd = cmd :?> 'Command
            return! handler.Handle typedCmd
        }
        handlers.[typeof<'Command>] <- handlerFunc
    
    interface ICommandBus with
        member this.Send command = this.Send command
        member this.RegisterHandler<'Command> (handler: ICommandHandler<'Command>) = this.RegisterHandler handler