module ValidationTests

open Expecto
open Jade.Core.Validation

[<Tests>]
let validationTests = 
    testList "Validation Tests" [
        testCase "notNull succeeds with valid value" <| fun _ ->
            let result = Validation.notNull "field" "value"
            match result with
            | Ok value -> Expect.equal value "value" "Should return the value"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "notNull fails with null value" <| fun _ ->
            let result = Validation.notNull "field" null
            match result with
            | Ok _ -> Tests.failtest "Should fail"
            | Error errors ->
                Expect.equal errors.Length 1 "Should have one error"
                Expect.equal errors.[0].Field "field" "Field should match"
                Expect.equal errors.[0].Code (Some "NULL") "Code should be NULL"

        testCase "notNullOrEmpty succeeds with valid string" <| fun _ ->
            let result = Validation.notNullOrEmpty "name" "test"
            match result with
            | Ok value -> Expect.equal value "test" "Should return the value"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "notNullOrEmpty fails with empty string" <| fun _ ->
            let result = Validation.notNullOrEmpty "name" ""
            match result with
            | Ok _ -> Tests.failtest "Should fail"
            | Error errors ->
                Expect.equal errors.Length 1 "Should have one error"
                Expect.equal errors.[0].Field "name" "Field should match"

        testCase "positive succeeds with positive number" <| fun _ ->
            let result = Validation.positive "count" 5
            match result with
            | Ok value -> Expect.equal value 5 "Should return the value"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "positive fails with zero" <| fun _ ->
            let result = Validation.positive "count" 0
            match result with
            | Ok _ -> Tests.failtest "Should fail"
            | Error errors ->
                Expect.equal errors.Length 1 "Should have one error"
                Expect.equal errors.[0].Code (Some "NOT_POSITIVE") "Code should be NOT_POSITIVE"

        testCase "stringLength succeeds within bounds" <| fun _ ->
            let result = Validation.stringLength "name" 3 10 "test"
            match result with
            | Ok value -> Expect.equal value "test" "Should return the value"
            | Error _ -> Tests.failtest "Should succeed"

        testCase "stringLength fails when too short" <| fun _ ->
            let result = Validation.stringLength "name" 5 10 "hi"
            match result with
            | Ok _ -> Tests.failtest "Should fail"
            | Error errors ->
                Expect.equal errors.Length 1 "Should have one error"
                Expect.equal errors.[0].Code (Some "TOO_SHORT") "Code should be TOO_SHORT"

        testCase "validation computation expression works" <| fun _ ->
            let result = validation {
                let! name = Validation.notNullOrEmpty "name" "test"
                let! count = Validation.positive "count" 5
                return (name, count)
            }
            match result with
            | Ok (name, count) -> 
                Expect.equal name "test" "Name should match"
                Expect.equal count 5 "Count should match"
            | Error _ -> Tests.failtest "Should succeed"
    ]