# Audit Partition Management Specification

## Purpose

Define `audit.events` partitioning.

## Requirements

### Requirement: Safe migration 0040

`0040` MUST preserve rows, validate before atomic activation, retain source, and use EF key `(id, occurred_at)`.

#### Scenario: Fresh database
- GIVEN an empty database
- WHEN `0040` completes
- THEN `audit.events` MUST be partitioned and writable

#### Scenario: Historical upgrade
- GIVEN historical prior rows
- WHEN `0040` completes
- THEN count/values MUST match, each row readable once

#### Scenario: Safe rerun
- GIVEN completion or a checkpoint stop
- WHEN it runs again
- THEN it MUST converge without duplication, loss, or invalid activation

#### Scenario: Default coverage
- GIVEN an out-of-range row
- WHEN it is inserted
- THEN it MUST be readable in DEFAULT

#### Scenario: Future coverage
- GIVEN current and future partitions
- WHEN matching rows are inserted
- THEN each MUST route to its month

#### Scenario: Retained source
- GIVEN validated swap data
- WHEN `0040` commits
- THEN source MUST remain without automatic deletion

#### Scenario: EF identity
- GIVEN equal `id`, different `occurred_at`
- WHEN EF tracks and queries both
- THEN both MUST remain distinct
