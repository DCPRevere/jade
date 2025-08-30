module Program

open Expecto

[<EntryPoint>]
let main args =
    let allTests = testList "Jade.Core Tests" [
        ValidationTests.validationTests
        EventMetadataTests.eventMetadataTests
        AggregateTests.aggregateTests
        SnapshotTests.snapshotTests
        CommandBusTests.commandBusTests
        MartenRepositoryTests.martenRepositoryTests
    ]
    
    runTestsWithCLIArgs [] args allTests
