module Jade.Core.Validation

open System

/// Validation error
type ValidationError = {
    Field: string
    Message: string
    Code: string option
}

/// Validation result
type ValidationResult<'T> = Result<'T, ValidationError list>

/// Helper functions for validation
module Validation =
    
    /// Create a validation error
    let error field message code = {
        Field = field
        Message = message
        Code = code
    }
    
    /// Succeed with a value
    let succeed value = Ok value
    
    /// Fail with errors
    let fail errors = Error errors
    
    /// Fail with a single error
    let failWith field message code = 
        Error [error field message code]
    
    /// Combine validation results
    let combine validations =
        let rec loop acc remaining =
            match remaining with
            | [] -> 
                match acc with
                | [] -> Ok []
                | errors -> Error (List.rev errors)
            | Ok value :: rest -> loop acc rest
            | Error errors :: rest -> loop (List.rev errors @ acc) rest
        
        loop [] validations
    
    /// Apply a function if validation succeeds
    let map f result =
        match result with
        | Ok value -> Ok (f value)
        | Error errors -> Error errors
    
    /// Apply a function that returns a validation result
    let bind f result =
        match result with
        | Ok value -> f value
        | Error errors -> Error errors
    
    /// Validate that a value is not null
    let notNull field value =
        if isNull (box value) then
            Error [error field (field + " cannot be null") (Some "NULL")]
        else
            succeed value
    
    /// Validate that a string is not null or empty
    let notNullOrEmpty field (value: string) =
        if String.IsNullOrEmpty value then
            Error [error field (field + " cannot be null or empty") (Some "NULL_OR_EMPTY")]
        else
            succeed value
    
    /// Validate that a string is not null, empty, or whitespace
    let notNullOrWhitespace field (value: string) =
        if String.IsNullOrWhiteSpace value then
            Error [error field (field + " cannot be null, empty, or whitespace") (Some "NULL_OR_WHITESPACE")]
        else
            succeed value
    
    /// Validate string length
    let stringLength field minLength maxLength (value: string) =
        let len = if isNull value then 0 else value.Length
        if len < minLength then
            Error [error field (field + " must be at least " + string minLength + " characters") (Some "TOO_SHORT")]
        elif len > maxLength then
            Error [error field (field + " must be at most " + string maxLength + " characters") (Some "TOO_LONG")]
        else
            succeed value
    
    /// Validate that a value is positive
    let positive field (value: int) =
        if value <= 0 then
            Error [error field (field + " must be positive") (Some "NOT_POSITIVE")]
        else
            succeed value
    
    /// Validate that a value is non-negative
    let nonNegative field (value: int) =
        if value < 0 then
            Error [error field (field + " must be non-negative") (Some "NEGATIVE")]
        else
            succeed value
    
    /// Validate email format (basic)
    let email field (value: string) =
        if String.IsNullOrWhiteSpace value || not (value.Contains "@") then
            Error [error field (field + " must be a valid email address") (Some "INVALID_EMAIL")]
        else
            succeed value
    
    /// Validate GUID is not empty
    let notEmptyGuid field (value: Guid) =
        if value = Guid.Empty then
            Error [error field (field + " cannot be empty GUID") (Some "EMPTY_GUID")]
        else
            succeed value

/// Validation computation expression
type ValidationBuilder() =
    member _.Return value = Validation.succeed value
    member _.ReturnFrom result = result
    member _.Bind (result, f) = Validation.bind f result
    member _.Combine(result1, result2) = 
        match result1, result2 with
        | Ok _, Ok value -> Ok value
        | Ok _, Error errors -> Error errors
        | Error errors, Ok _ -> Error errors
        | Error errors1, Error errors2 -> Error (errors1 @ errors2)

/// Global validation computation expression instance
let validation = ValidationBuilder()