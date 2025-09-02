module Jade.Core.CommandBus

open System
open Jade.Core.EventSourcing

type IHandler =
    abstract member Handle: obj -> Async<Result<unit, string>>

type ICommandBus =
    abstract member Send: 'Command -> Async<Result<unit, string>>

type CommandBus(getHandler: Type -> IHandler option) =
    
    member _.Send command = async {
        let commandType = command.GetType()
        
        match getHandler commandType with
        | Some handler -> return! handler.Handle command
        | None -> return Error $"No handler registered for command type {commandType.Name}"
    }
    
    interface ICommandBus with
        member this.Send command = this.Send command

let createHandler<'Command, 'Event, 'State when 'Event :> IEvent>
    (repository: IRepository<'State, 'Event>)
    (aggregate: Aggregate<'Command, 'Event, 'State>)
    (getId: 'Command -> AggregateId) : IHandler =
    
    let aggregateHandler = AggregateHandler(repository, aggregate, getId)
    
    { new IHandler with
        member _.Handle command = 
            let typedHandler = aggregateHandler :> ICommandHandler<'Command>
            typedHandler.Handle (command :?> 'Command) }