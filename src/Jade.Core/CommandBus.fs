module Jade.Core.CommandBus

open System
open Jade.Core.EventSourcing

type IDomainCommandHandler =
    abstract member CommandTypes: Type list
    abstract member Handle: obj -> Async<Result<unit, string>>

type ICommandBus =
    abstract member Send: 'Command -> Async<Result<unit, string>>
    abstract member RegisterDomainHandler: IDomainCommandHandler -> unit

type CommandBus() =
    let handlers = System.Collections.Generic.Dictionary<Type, IDomainCommandHandler>()
    
    member _.Send command = async {
        let commandType = command.GetType()
        
        match handlers.TryGetValue commandType with
        | true, handler -> return! handler.Handle command
        | false, _ -> return Error $"No handler registered for command type {commandType.Name}"
    }
    
    member _.RegisterDomainHandler (handler: IDomainCommandHandler) =
        // Register this handler for all command types it knows about
        for commandType in handler.CommandTypes do
            handlers.[commandType] <- handler
    
    interface ICommandBus with
        member this.Send command = this.Send command
        member this.RegisterDomainHandler handler = this.RegisterDomainHandler handler