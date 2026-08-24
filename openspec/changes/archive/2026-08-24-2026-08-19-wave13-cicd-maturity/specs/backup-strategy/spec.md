# Delta for Backup Strategy

## ADDED Requirements

### Requirement: wal-g recoverability evidence

The system MUST produce restorable wal-g base/WAL backups and prove isolated PITR with RPO ≤1 hour.

#### Scenario: Base/WAL coverage
- GIVEN a wal-g base followed by marker A
- WHEN WAL archival completes
- THEN catalog MUST show base/WAL coverage through A

#### Scenario: Isolated PITR target
- GIVEN A before target T and B after T
- WHEN separate empty PostgreSQL recovers to T
- THEN A MUST exist, B MUST not, and source MUST be unchanged

#### Scenario: Drill proves the RPO
- GIVEN cutoff and latest-recovered timestamps
- WHEN their difference is calculated
- THEN evidence MUST report RPO ≤1 hour
