namespace Jade.Example.Pgmq.Worker

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open System.Text.Json
open Marten
open Jade.Core.CommandBus
open Jade.Core.CommandQueue
open Jade.Core.CommandRegistry
open Jade.Marten.MartenRepository
open Jade.Marten.JsonConfiguration
open Jade.Marten.MartenConfiguration
open Jade.Example.Domain.MartenConfiguration
open Jade.Marten.PgmqCommandReceiver

module Program =
    [<EntryPoint>]
    let main args =
        let builder = Host.CreateApplicationBuilder(args)

        let connectionString = "Host=localhost;Port=5432;Database=jade_api;Username=sacra;Password=sacra_dev_password"
        let pgmqConnectionString = "Host=localhost;Port=5433;Database=jade_pgmq;Username=postgres;Password=postgres"

        let jsonOptions = JsonSerializerOptions()
        jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        jsonOptions.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())

        builder.Services.AddSingleton<JsonSerializerOptions>(jsonOptions) |> ignore

        builder.Services.AddMarten(fun (options: StoreOptions) ->
            options.Connection(connectionString)
            options.AutoCreateSchemaObjects <- JasperFx.AutoCreate.CreateOrUpdate
            options.DatabaseSchemaName <- "jade_events"
            configureMartenBase jsonOptions options
            configureDomainMarten options
        ).UseLightweightSessions() |> ignore

        builder.Services.AddSingleton<Registry>(fun sp ->
            let documentStore = sp.GetRequiredService<IDocumentStore>()
            let logger = sp.GetRequiredService<ILogger<Registry>>()
            let jsonOpts = sp.GetRequiredService<JsonSerializerOptions>()
            let registry = Registry(logger, jsonOpts)

            let loggerFactory = sp.GetRequiredService<ILoggerFactory>()

            let customerLogger = loggerFactory.CreateLogger("Customer.Repository")
            let customerRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, Customer.State> customerLogger documentStore Customer.aggregate
            let customerHandlerLogger = loggerFactory.CreateLogger("Customer.Handler")
            let customerHandler = createHandler customerHandlerLogger customerRepository Customer.aggregate Customer.getId

            let orderLogger = loggerFactory.CreateLogger("Order.Repository")
            let orderRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, Order.State> orderLogger documentStore Order.aggregate
            let orderHandlerLogger = loggerFactory.CreateLogger("Order.Handler")
            let orderHandler = createHandler orderHandlerLogger orderRepository Order.aggregate Order.getId

            let notificationService =
                { new OrderNotification.INotificationService with
                    member _.SendOrderConfirmation orderId customerId = async {
                        logger.LogInformation("Mock: Sending order confirmation for order {OrderId} to customer {CustomerId}", orderId, customerId)
                        return Ok ()
                    } }

            let sendConfirmationLogger = loggerFactory.CreateLogger("SendConfirmationHandler")
            let sendConfirmationHandler = OrderNotification.Handler.create sendConfirmationLogger orderRepository notificationService

            registry.registerHandlers([
                (customerHandler, [
                    typeof<Customer.Command.Create.V1>
                    typeof<Customer.Command.Create.V2>
                    typeof<Customer.Command.Update.V1>
                ])
                (orderHandler, [
                    typeof<Order.Command.Create.V1>
                    typeof<Order.Command.Create.V2>
                    typeof<Order.Command.Cancel.V1>
                ])
                (sendConfirmationHandler, [typeof<Order.Command.SendConfirmation.V1>])
            ])

            registry
        ) |> ignore

        builder.Services.AddSingleton<ICommandReceiver list>(fun sp ->
            let loggerFactory = sp.GetRequiredService<ILoggerFactory>()
            let jsonOpts = sp.GetRequiredService<JsonSerializerOptions>()

            // Create receiver for each aggregate type
            let customerLogger = loggerFactory.CreateLogger<PgmqCommandReceiver>()
            let customerReceiver = PgmqCommandReceiver(pgmqConnectionString, "customer", jsonOpts, customerLogger) :> ICommandReceiver

            let orderLogger = loggerFactory.CreateLogger<PgmqCommandReceiver>()
            let orderReceiver = PgmqCommandReceiver(pgmqConnectionString, "order", jsonOpts, orderLogger) :> ICommandReceiver

            [customerReceiver; orderReceiver]
        ) |> ignore

        builder.Services.AddHostedService<CommandWorker>() |> ignore

        let host = builder.Build()
        host.Run()

        0
