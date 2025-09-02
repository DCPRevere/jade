namespace Jade.Example.Api.Controllers

open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Marten
open System.Threading.Tasks
open Jade.Example.Domain.Projections.CustomerView

[<ApiController>]
[<Route("api/[controller]")>]
type CustomersController(documentStore: IDocumentStore, logger: ILogger<CustomersController>) =
    inherit ControllerBase()

    [<HttpGet>]
    member this.GetAllCustomers() : Task<IActionResult> = task {
        try
            logger.LogInformation("📋 GET /api/customers - Retrieving all customers with orders")
            
            use session = documentStore.LightweightSession()
            let! customers = session.Query<CustomerView>().ToListAsync()
            
            logger.LogInformation("📋 Found {Count} customers", customers.Count)
            
            return this.Ok(customers) :> IActionResult
        with
        | ex -> 
            logger.LogError(ex, "❌ Error retrieving customers")
            return this.StatusCode(500, {| error = "Internal server error retrieving customers" |}) :> IActionResult
    }