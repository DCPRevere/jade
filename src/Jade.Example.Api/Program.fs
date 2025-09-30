namespace Jade.Example.Api

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Logging
open Marten
open Jade.Marten.ApiInfrastructure
open Jade.Core.CommandBus
open Jade.Core.CommandRegistry
open Jade.Marten.MartenRepository
open Jade.Example.Domain.MartenConfiguration

module Program =
    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)
        
        let config = {
            ApiTitle = "Jade Example CloudEvents API"
            ApiVersion = "v1"
            ApiDescription = "Example API for processing commands via CloudEvents using Jade infrastructure"
            DatabaseSchemaName = "jade_events"
            UseContainerInDevelopment = 
                match builder.Configuration.["JadeConfiguration:UseContainerInDevelopment"] with
                | null | "" -> false
                | value -> value.ToLowerInvariant() = "true"
        }
        
        let registryConfig = fun (documentStore: IDocumentStore) (registry: Registry) ->
            // Create a temporary logger factory for this configuration
            let loggerFactory = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)

            // Register Customer handler with commands
            let customerLogger = loggerFactory.CreateLogger("Customer.Repository")
            let customerRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, Customer.State> customerLogger documentStore Customer.aggregate
            let customerHandlerLogger = loggerFactory.CreateLogger("Customer.Handler")
            let customerHandler = createHandler customerHandlerLogger customerRepository Customer.aggregate Customer.getId
            registry.register([
                typeof<Customer.Command.Create.V1>
                typeof<Customer.Command.Create.V2>
                typeof<Customer.Command.Update.V1>
            ], customerHandler)

            // Register Order handler with commands
            let orderLogger = loggerFactory.CreateLogger("Order.Repository")
            let orderRepository = createRepository<Jade.Core.EventSourcing.ICommand, Jade.Core.EventSourcing.IEvent, Order.State> orderLogger documentStore Order.aggregate
            let orderHandlerLogger = loggerFactory.CreateLogger("Order.Handler")
            let orderHandler = createHandler orderHandlerLogger orderRepository Order.aggregate Order.getId
            registry.register([
                typeof<Order.Command.Create.V1>
                typeof<Order.Command.Create.V2>
                typeof<Order.Command.Cancel.V1>
            ], orderHandler)
        
        let jsonConfig = fun (options: System.Text.Json.JsonSerializerOptions) ->
            options.PropertyNamingPolicy <- System.Text.Json.JsonNamingPolicy.CamelCase
            options.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())
        
        let app = 
            JadeApiBuilder(builder)
                .WithConfiguration(config)
                .ConfigureServices(jsonConfig, configureDomainMarten, registryConfig)
                .Build()

        app.Run()
        0