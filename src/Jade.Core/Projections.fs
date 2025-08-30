module Jade.Core.Projections

open System
open Jade.Core.EventSourcing
open Jade.Core.EventMetadata

/// Projection handler for creating read models
type IProjectionHandler<'Event, 'ReadModel> =
    abstract member Handle: EventWithMetadata<'Event> -> 'ReadModel -> Async<'ReadModel>
    abstract member CanHandle: 'Event -> bool

/// Repository for read models/projections
type IProjectionRepository<'ReadModel> =
    abstract member Save: string -> 'ReadModel -> Async<Result<unit, string>>
    abstract member Get: string -> Async<Result<'ReadModel option, string>>
    abstract member Delete: string -> Async<Result<unit, string>>

/// Position tracking for projection progress
type ProjectionPosition = {
    ProjectionName: string
    LastProcessedEventId: Guid
    LastProcessedTimestamp: DateTimeOffset
    Version: int64
}

/// Repository for tracking projection positions
type IProjectionPositionRepository =
    abstract member GetPosition: string -> Async<Result<ProjectionPosition option, string>>
    abstract member SavePosition: ProjectionPosition -> Async<Result<unit, string>>

/// Projection runner that processes events
type ProjectionRunner<'Event, 'ReadModel>(
    projectionName: string,
    handler: IProjectionHandler<'Event, 'ReadModel>,
    projectionRepo: IProjectionRepository<'ReadModel>,
    positionRepo: IProjectionPositionRepository,
    initialReadModel: 'ReadModel) =
    
    member this.ProcessEvent (eventWithMetadata: EventWithMetadata<'Event>) (readModelId: string) = async {
        if handler.CanHandle eventWithMetadata.Event then
            let! currentReadModelResult = projectionRepo.Get readModelId
            let readModel = 
                match currentReadModelResult with
                | Ok (Some model) -> model
                | Ok None -> initialReadModel
                | Error _ -> initialReadModel
            
            let! updatedReadModel = handler.Handle eventWithMetadata readModel
            let! saveResult = projectionRepo.Save readModelId updatedReadModel
            
            match saveResult with
            | Ok () ->
                let position = {
                    ProjectionName = projectionName
                    LastProcessedEventId = eventWithMetadata.Metadata.EventId
                    LastProcessedTimestamp = eventWithMetadata.Metadata.Timestamp
                    Version = 0L // This would typically come from the event store
                }
                let! positionResult = positionRepo.SavePosition position
                return positionResult
            | Error err -> return Error err
        else
            return Ok ()
    }

/// Helper to create a simple projection handler
let createSimpleProjectionHandler<'Event, 'ReadModel> 
    (canHandle: 'Event -> bool) 
    (handle: EventWithMetadata<'Event> -> 'ReadModel -> Async<'ReadModel>) =
    { new IProjectionHandler<'Event, 'ReadModel> with
        member _.CanHandle event = canHandle event
        member _.Handle eventWithMeta readModel = handle eventWithMeta readModel
    }