module Jade.Core.Snapshots

open System
open Jade.Core.EventSourcing

/// Snapshot of aggregate state at a specific version
type Snapshot<'State> = {
    AggregateId: AggregateId
    State: 'State
    Version: int64
    Timestamp: DateTimeOffset
}

/// Repository for storing and retrieving snapshots
type ISnapshotRepository<'State> =
    abstract member Save: Snapshot<'State> -> Async<Result<unit, string>>
    abstract member GetLatest: AggregateId -> Async<Result<Snapshot<'State> option, string>>
    abstract member GetAtVersion: AggregateId -> int64 -> Async<Result<Snapshot<'State> option, string>>

/// Snapshot strategy determines when to take snapshots
type SnapshotStrategy =
    | EveryNEvents of int
    | EveryNMinutes of int
    | Custom of (int64 -> DateTimeOffset -> bool)

/// Helper to determine if snapshot should be taken
let shouldTakeSnapshot strategy currentVersion lastSnapshotTime =
    match strategy with
    | EveryNEvents n -> currentVersion % int64 n = 0L
    | EveryNMinutes minutes ->
        let timeDiff: TimeSpan = DateTimeOffset.UtcNow - lastSnapshotTime
        timeDiff.TotalMinutes >= float minutes
    | Custom predicate -> predicate currentVersion lastSnapshotTime

/// Create a snapshot
let createSnapshot aggregateId state version = {
    AggregateId = aggregateId
    State = state
    Version = version
    Timestamp = DateTimeOffset.UtcNow
}