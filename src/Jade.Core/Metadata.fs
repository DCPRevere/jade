namespace Jade.Core

open System

type Metadata = {
    Id: string
    CorrelationId: string
    CausationId: string option
    UserId: string option
    Timestamp: DateTime option
}
