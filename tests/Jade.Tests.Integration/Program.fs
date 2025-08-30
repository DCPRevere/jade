open System
open Expecto

// Simple test to verify the F# project works
let basicTests =
    testList "Basic F# Tests" [
        test "F# integration test project works" {
            Expect.equal (2 + 2) 4 "Basic arithmetic should work"
        }
    ]

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv basicTests