# Delta for Production Readiness

## ADDED Requirements

### Requirement: Archive and changelog consistency gate

CI MUST reject archive/changelog mismatch against the archive manifest.

#### Scenario: Archive is unlisted
- GIVEN an unlisted manifest archive
- WHEN consistency runs
- THEN CI MUST fail naming the archive key

#### Scenario: Changelog claim lacks an archive
- GIVEN an unmatched changelog claim
- WHEN consistency runs
- THEN CI MUST fail naming the claim

### Requirement: Non-delivering exact-commit RC readiness

`v1.1.0-rc1` readiness MUST identify its tested commit without tagging or publishing.

#### Scenario: Readiness evidence is non-delivering
- GIVEN gates pass at SHA S
- WHEN readiness is recorded
- THEN evidence MUST name S; tag and publications MUST be absent
