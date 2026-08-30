# GDPR Data Subject Request (DSAR) Runbook

> **Wave 11 slice 11.4 — operational companion to `gdpr-compliance` spec.**
> This is the canonical on-call playbook for handling a Data Subject
> Access Request (DSAR) under GDPR Art. 15-22. The runbook is read by
> support engineers, on-call SREs, and the privacy team on receipt of a
> verified DSAR.
>
> **Compliance owner:** privacy@jadecapital.com
> **Audit owner:** privacy@jadecapital.com (audit trail via the
> GdprAuditAnonymizer + HardDeleteSweepBackgroundService)
> **Scope:** all Subject Requests (access, rectification, erasure,
> restriction, portability, objection, automated decision-making).

---

## 1. Intake

### 1.1 Verifying the request

Every DSAR lands at **privacy@jadecapital.com** (or via the in-product
"Export my data" / "Eliminar mi cuenta" buttons). The privacy team
verifies the requester's identity using one of:

1. The signed JWT (`Authorization: Bearer ...`) which proves the
   caller has the credentials for the account in question.
2. A support-ticket assertion + matching email address in the user
   table.

If the requester refuses or cannot prove identity, the runbook is
frozen until proof is supplied (GDPR permits up to 1-month extension
while waiting for identity).

### 1.2 Logging the DSAR

Every request creates a support ticket with:

- Type: `DSAR` (auto-detected from the endpoint route or the email
  subject).
- Subject reference: the user_id (Guid) + email lowercased.
- Channel: `in-product` (for the in-app buttons) or `email` (for
  privacy@jadecapital.com).
- Verification method: `jwt` or `support-ticket`.

The ticket is the canonical record for the 1-month SLA response
window (GDPR Art. 12(3)).

## 2. GDPR Art. 15 — Right of access

If the user clicked "Mis datos" or emailed "what do you hold on me":

```bash
# 1) Issue the GDPR Art. 20 portability export.
curl -X GET https://api.jadecapital.com/api/users/me/export \
    -H "Authorization: Bearer ${JWT}" \
    -H "Accept: application/json" \
    -o /srv/dsar/<subject_id>-export.json

# 2) Verify the export body contains the 12 core sections (the
# spec requires: profile, password_history, risk_profile, accounts,
# trades, journal_entries, trade_reviews, strategies, scanners,
# imports, subscriptions, audit_metadata).
jq -r '.sections | keys | sort' /srv/dsar/<subject_id>-export.json

# 3) Upload to the support ticket + send a signed download link
# to the requester.
```

The export endpoint streams the JSON via Transfer-Encoding: chunked,
so files are not held in memory; the on-call engineer copies the file
to the support bucket and provides a 7-day signed URL.

## 3. GDPR Art. 17 — Right to be forgotten

If the user clicked "Eliminar mi cuenta" or emailed "delete my data":

The flow is split into TWO invariants:

### 3.1 Immediate soft-delete cascade

```bash
# 1) Trigger the GDPR cascade (idempotent — returns Conflict if
# already SoftDeleted).
curl -X DELETE https://api.jadecapital.com/api/users/me/account \
    -H "Authorization: Bearer ${JWT}"

# 2) Confirm the response carries:
#    - softDeletedAt           (UTC ISO-8601)
#    - scheduledHardDeleteAt    (= softDeletedAt + 30 days, configurable)
#    - cascadeSoftDeletedRows  (≥ 8 per spec)
```

The cascade handler (`GdprCascadeOrchestrator`) flips the user row
into `UserStatus.SoftDeleted` and propagates the soft-delete to every
dependent table (Identity, Trading, Billing modules). The audit
anonymizer (`GdprAuditAnonymizer`) pseudonymises the `actor_user_id`
in audit events BEFORE the hard-delete sweep runs.

### 3.2 30-day grace window + restoration

The user can RESTORE the account during the 30-day grace via the
in-product "Undo delete" affordance (CSRF-protected; see
`features/settings/account-deletion-confirmation.component.ts`).
Restoration reverses the soft-delete cascade by flipping every
soft-deleted row back to its pre-delete state. Logs from the original
cascade are preserved (each step appends an audit row, never deletes).

### 3.3 Hard-delete sweep

After the 30-day window the `HardDeleteSweepBackgroundService` (a
.NET HostedService, runs daily) executes:

```sql
-- 0009-gdpr-right-to-be-forgotten migration (already applied)
SELECT id, scheduled_hard_delete_at FROM identity.users
WHERE status = 'ScheduledHardDelete'
  AND scheduled_hard_delete_at <= now();
-- For every row returned:
--   - Call CascadeHardDeleteAsync (physical DELETE per module)
--   - Call AuditAnonymizationAsync (replace `actor_user_id` with
--     `sha256(id + audit_salt)`)
```

The sweep is idempotent — re-running on an already-deleted user is a
no-op (the WHERE clause filters them out).

## 4. GDPR Art. 20 — Right to data portability

See §2 above. The export endpoint returns the SAME payload that
backed the GDPR Art. 15 disclosure; the difference is the legal framing
(passive disclosure vs. active portability).

## 5. GDPR Art. 16 / 18 / 21 — Rectification / restriction / objection

These rights live in the support-ticket workflow (no self-service
path). The privacy team uses the in-app admin tooling (or the SQL
console + a DSAR-named audit row) to:

- Rectify a user profile field (`display_name` etc.).
- Restrict processing (toggles `is_processing_restricted = true` so
  no BackgroundService touches the user).
- Object to a specific processing activity (auditable; logged with
  `action = 'dsar_objection_recorded'`).

Each operation emits an audit row via `IAuditLogger` with
`actor_user_id = privacy_bot_jti` + `correlation_id = ticket_id`.

## 6. Verification checklist

Before closing a DSAR ticket the on-call must confirm:

- [ ] The 12-section export (or cascade) executed without a 4xx/5xx
      response.
- [ ] The audit trail contains at minimum:
  - `dsar_received` (timestamp + ticket_id + subject_id)
  - `dsar_verified` (verification method)
  - `dsar_action_executed` (cascade / export / objection etc.)
  - `dsar_response_sent` (delivery channel + on-call signature)
- [ ] The privacy team has signed off in the ticket comments.
- [ ] If Art. 17: `psql -c "SELECT scheduled_hard_delete_at FROM
      identity.users WHERE id = '<subject_id>';"` shows a non-NULL
      value within `(now, now + 31 days)`.

## 7. Escalation path

If any step fails or the privacy team cannot reach the requester:

1. Tag the ticket `needs-legal` and ping the DPO (when appointed).
2. If the request crosses jurisdictions (e.g., a Spain GDPR requester
   on an Argentina DB) the privacy team consults the cross-jurisdiction
   matrix in `openspec/specs/gdpr-compliance/spec.md` Appendix B.
3. If the cascade throws an unhandled exception, freeze the runbook —
   do NOT partially delete; ping the on-call engineering lead.

## 8. References

- `openspec/specs/gdpr-compliance/spec.md` — canonical GDPR scenarios.
- `infrastructure/postgres/migrations/0009-gdpr-right-to-be-forgotten.sql`
  — schema for the `scheduled_hard_delete_at` column + sweep index.
- `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/`
  — `DeleteAccountHandler` (Art. 17 entry point).
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/GdprAuditAnonymizer.cs`
  — audit row pseudonymisation.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/HardDeleteSweepBackgroundService.cs`
  — daily sweep loop.
- `docs/runbooks/disaster-recovery.md` — for backup-related DSAR
  queries (e.g., point-in-time restoration requests).
