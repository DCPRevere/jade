module Customer

open System
open Jade.Core.EventSourcing

type CreateCustomerV1 = {
    CustomerId: Guid
    Name: string
    Email: string
} with
    static member toSchema =
        $"urn:schema:jade:command:customer:create:1"

type CreateCustomerV2 = {
    CustomerId: Guid
    Name: string
    Email: string
    Phone: string
} with
    static member toSchema =
        $"urn:schema:jade:command:customer:create:2"

type CreateCustomer =
    | V1 of CreateCustomerV1
    | V2 of CreateCustomerV2

type UpdateV1 = {
    CustomerId: Guid
    Name: string
    Email: string
} with
    static member toSchema =
        $"urn:schema:jade:command:customer:update:1"

type UpdateCustomer =
    | V1 of UpdateV1

type Command =
    | Create of CreateCustomer
    | Update of UpdateCustomer

type CreatedCustomerV1 = {
    CustomerId: Guid
    Name: string
    Email: string
} with
    static member toSchema =
        $"urn:schema:jade:event:customer:created:1"

type CreatedCustomerV2 = {
    CustomerId: Guid
    Name: string
    Email: string
    Phone: string
} with
    static member toSchema =
        $"urn:schema:jade:event:customer:created:2"

type CreatedCustomer =
    | V1 of CreatedCustomerV1
    | V2 of CreatedCustomerV2

type UpdatedCustomerV1 = {
    CustomerId: Guid
    Name: string
    Email: string
} with
    static member toSchema =
        $"urn:schema:jade:event:customer:updated:1"

type UpdatedCustomer =
    | V1 of UpdatedCustomerV1

type _Event =
    | Created of CreatedCustomer
    | Updated of UpdatedCustomer

type State = {
    Id: Guid
    Name: string
    Email: string
    Phone: string option
}

// Clean domain functions using pattern matching
let create command : Result<_Event list, string> =
    match command with
    | Create version -> 
        match version with
        | CreateCustomer.V1 cmd -> Ok [ Created (CreatedCustomer.V1 { CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email }) ]
        | CreateCustomer.V2 cmd -> Ok [ Created (CreatedCustomer.V2 { CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email; Phone = cmd.Phone }) ]
    | Update _ -> Error "Update command cannot be used for creation"

let decide command state : Result<_Event list, string> =
    match command with
    | Create _ -> Error "Create command cannot be used on existing aggregate"
    | Update version -> 
        match version with
        | UpdateCustomer.V1 cmd -> Ok [ Updated (UpdatedCustomer.V1 { CustomerId = cmd.CustomerId; Name = cmd.Name; Email = cmd.Email }) ]

let init event : State =
    match event with
    | Created version -> 
        match version with
        | CreatedCustomer.V1 e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
        | CreatedCustomer.V2 e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = Some e.Phone }
    | Updated version -> 
        match version with
        | UpdatedCustomer.V1 e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }

let evolve state event : State =
    match event with
    | Created version -> 
        match version with
        | CreatedCustomer.V1 e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = None }
        | CreatedCustomer.V2 e -> { Id = e.CustomerId; Name = e.Name; Email = e.Email; Phone = Some e.Phone }
    | Updated version -> 
        match version with
        | UpdatedCustomer.V1 e -> { state with Name = e.Name; Email = e.Email }

let getId command : Guid =
    match command with
    | Create version -> 
        match version with
        | CreateCustomer.V1 cmd -> cmd.CustomerId
        | CreateCustomer.V2 cmd -> cmd.CustomerId
    | Update version -> 
        match version with
        | UpdateCustomer.V1 cmd -> cmd.CustomerId

let aggregate = {
    create = create
    decide = decide
    init = init
    evolve = evolve
}
