# Delta for Audit Retention Policy

## ADDED Requirements

### Requirement: Retention across partitions

Retention MUST purge expired monthly/DEFAULT events while preserving newer events and parent writability.

#### Scenario: Partition-aware retention
- GIVEN expired/retained events across historical, current, and DEFAULT partitions
- WHEN one retention cycle completes
- THEN expired events MUST be removed and retained events MUST remain readable
