module Program

open Expecto

[<EntryPoint>]
let main args =
    let allTests = testList "Jade.Core Tests" [
        AggregateTests.aggregateTests
        CommandBusTests.commandBusTests
        MartenRepositoryTests.martenRepositoryTests
    ]
    
    runTestsWithCLIArgs [] args allTests
