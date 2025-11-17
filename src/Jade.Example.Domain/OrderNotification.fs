module OrderNotification

open Microsoft.Extensions.Logging
open Jade.Core
open Jade.Core.EventSourcing
open Jade.Core.CommandBus

type INotificationService =
    abstract member SendOrderConfirmation: orderId: string -> customerId: string -> Async<Result<unit, string>>

module Handler =
    let create
        (logger: ILogger)
        (repository: IRepository<Order.State, IEvent>)
        (notificationService: INotificationService) : IHandler =

        { new IHandler with
            member _.Handle command = async {
                match command with
                | :? Order.Command.SendConfirmation.V1 as cmd ->
                    try
                        let! stateResult = repository.GetById cmd.OrderId

                        match stateResult with
                        | Error err ->
                            return Error $"Failed to load order: {err}"

                        | Ok (state, currentVersion) ->
                            match Order.canSendConfirmation state with
                            | Error validationErr ->
                                logger.LogWarning("Validation failed for order {OrderId}: {Error}", cmd.OrderId, validationErr)
                                return Error validationErr

                            | Ok () ->
                                logger.LogInformation("Sending order confirmation for {OrderId}", cmd.OrderId)
                                let! sendResult = notificationService.SendOrderConfirmation cmd.OrderId state.CustomerId

                                match sendResult with
                                | Error err ->
                                    logger.LogError("Failed to send confirmation for {OrderId}: {Error}", cmd.OrderId, err)
                                    return Error $"Notification failed: {err}"

                                | Ok () ->
                                    let event : Order.Event.ConfirmationSent.V1 = {
                                        OrderId = cmd.OrderId
                                        CustomerId = state.CustomerId
                                        SentAt = System.DateTimeOffset.UtcNow
                                        Metadata = Some cmd.Metadata
                                    }

                                    let! saveResult = repository.Save cmd.OrderId [event :> IEvent] currentVersion

                                    match saveResult with
                                    | Ok () ->
                                        logger.LogInformation("Order confirmation sent and recorded for {OrderId}", cmd.OrderId)
                                        return Ok ()
                                    | Error err ->
                                        logger.LogError("Failed to save confirmation event for {OrderId}: {Error}", cmd.OrderId, err)
                                        return Error $"Failed to record notification: {err}"
                    with ex ->
                        logger.LogError(ex, "Unexpected error handling SendOrderConfirmation for {OrderId}", cmd.OrderId)
                        return Error ex.Message

                | _ -> return Error "Invalid command type for OrderNotification handler"
            } }
