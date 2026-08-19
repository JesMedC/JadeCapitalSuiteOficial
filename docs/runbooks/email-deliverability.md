# Email Deliverability Runbook

> **Wave 11 slice 11.4 — operational companion to `email-deliverability` spec.**
> Read this before configuring a new transactional-email provider or
> diagnosing why Jade Capital Suite emails are landing in the spam
> folder. The runbook is the canonical reference for the SMTP DNS
> records + key rotation cadence + provider-side env-var mapping.
>
> **Ops owner:** ops@jadecapital.com
> **Compliance owner:** privacy@jadecapital.com
> **Covered providers:** Mailgun, Amazon SES, SendGrid, Postmark.
> **Stack:** MailKit (.NET) + JSON-template HotFolder under `frontend/src/assets/legal/`.

---

## 1. DNS records (paste into your DNS provider)

The three CNAME/TXT records below are mandatory BEFORE any
production-bound email is sent. Without them, the major providers
(Gmail, Outlook, Yahoo) will downgrade Jade Capital Suite mail to
spam.

### 1.1 SPF (TXT record at the root domain)

```text
v=spf1 include:mailgun.org include:amazonses.com ~all
```

Replace `mailgun.org` + `amazonses.com` with the providers you
actually use. The `~all` is a SOFTFAIL signal: emails from
unauthorised sources are accepted but flagged. Switch to `-all` once
the SPF record has been stable for ≥30 days (so legacy services that
were sending as jadecapital.com have migrated to the new SPA).

### 1.2 DKIM (TXT record at the selector subdomain)

The DKIM public key is published by each provider under a
provider-specific selector. Mailgun example:

```text
# Hostname:  smtp._domainkey.jadecapital.com
# Type:      TXT
# Value:
v=DKIM1; k=rsa; p=MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQ...
```

A 1024-bit RSA key is the minimum. For 2048-bit (now recommended by
Gmail + Yahoo per their 2024 bulk-sender rules), use `k=rsa; p=`
with a longer `p=` base64 string. Mailgun/SES auto-generate these
keys; copy the DNS record straight from the provider dashboard.

Selectors: Mailgun uses `smtp`; SES uses `selector1._domainkey`;
SendGrid uses `s1._domainkey`; Postmark uses `opendkim`.

### 1.3 DMARC (TXT record at `_dmarc.jadecapital.com`)

```text
v=DMARC1; p=quarantine; rua=mailto:privacy@jadecapital.com; ruf=mailto:privacy@jadecapital.com; pct=100; adkim=s; aspf=s
```

`p=quarantine` is the safe initial rollout (inbox providers will
quarantine any email that fails DMARC rather than reject outright).
After 30 days of clean reports at `privacy@jadecapital.com`, switch
to `p=reject` for hard enforcement. The `rua=` + `ruf=` addresses
collect the aggregate + forensic reports — review weekly during the
quarantine phase.

## 2. DKIM rotation cadence

Rotate DKIM keys every 90 days (matches Gmail's "trusted-sender"
window + Mailgun's recommendation). The rotation workflow:

1. Generate a new 2048-bit RSA keypair in the provider dashboard.
2. Publish the new public key as `selector2._domainkey.jadecapital.com`
   alongside the existing `selector1._domainkey.jadecapital.com`.
3. Switch the provider's outbound selector from `selector1` to
   `selector2` (one-click in Mailgun; SES requires an
   `UpdateActiveSigningKey` API call).
4. After 14 days of clean delivery, retire selector1 by deleting
   the DNS record.

The 90-day window + 14-day overlap gives a zero-downtime rotation.

## 3. Provider env-var mapping

Each provider requires its own SMTP credentials. The Jade Capital
Suite `MailKitSmtpEmailSender` reads them via the `Mail__*` config
keys (env-var convention for ASP.NET Core):

| Provider  | Host                  | Port  | STARTTLS | Username              | Password           |
|-----------|-----------------------|-------|----------|-----------------------|--------------------|
| Mailgun   | `smtp.mailgun.org`    | 587   | Yes      | `postmaster@...`      | API key (4 lines)  |
| Amazon SES| `email-smtp.us-east-1.amazonaws.com` | 587 | Yes | SMTP user (16 chars) | SMTP password (40+) |
| SendGrid  | `smtp.sendgrid.net`   | 587   | Yes      | `apikey`              | API key (SG.xxx…)   |
| Postmark  | `smtp.postmarkapp.com`| 587   | Yes      | Server API token      | API token          |

For local development, the canonical mailpit container listens on
`localhost:1025` with no STARTTLS + no auth (the `MailpitSmtpEmailSender`
wires the localhost defaults into DI).

### 3.1 Mailgun-specific knobs

```bash
Mail__Host=smtp.mailgun.org
Mail__Port=587
Mail__Username=postmaster@mg.jadecapital.com
Mail__Password=<API_KEY>
Mail__From=no-reply@jadecapital.com
Mail__UseStartTls=true
Mail__TimeoutMs=4000
Mail__MaxAttempts=3
```

### 3.2 SES-specific knobs (region-aware)

```bash
Mail__Host=email-smtp.us-east-1.amazonaws.com
Mail__Port=587
Mail__Username=<SES_SMTP_USERNAME>
Mail__Password=<SES_SMTP_PASSWORD>
Mail__From=no-reply@jadecapital.com
Mail__UseStartTls=true
Mail__TimeoutMs=4000
Mail__MaxAttempts=3
```

Note: SES also requires the `production-access` lift on the account
(new accounts start in sandbox mode — emails sent to unverified
addresses return 4xx).

### 3.3 SendGrid-specific knobs

```bash
Mail__Host=smtp.sendgrid.net
Mail__Port=587
Mail__Username=apikey
Mail__Password=<SENDGRID_API_KEY>
Mail__From=no-reply@jadecapital.com
Mail__UseStartTls=true
Mail__TimeoutMs=4000
Mail__MaxAttempts=3
```

## 4. Verifying the deployment

After rolling a new provider or rotating keys:

1. **DKIM verification** — Mailgun: `curl -s 'https://api.mailgun.net/v3/.../dkim'` or
   `dig TXT selector1._domainkey.jadecapital.com`. The response must
   contain `v=DKIM1`.
2. **SPF verification** — `dig TXT jadecapital.com`. The response
   starts with `v=spf1 ...`.
3. **DMARC verification** — `dig TXT _dmarc.jadecapital.com`. The
   response includes `v=DMARC1; p=quarantine;` (or `p=reject`).
4. **Live test** — Send a test email from the Mailpit sandbox to
   `check-auth@verifier.port25.com`. The auto-reply includes a score
   breakdown (SPF, DKIM, DMARC, spam-likelihood).
5. **Inbox check** — Send to a Gmail + Outlook + Yahoo address.
   All three must land in the inbox (NOT in Promotions/Spam).

## 5. Bounce / complaint handling

Every provider exposes a webhook for bounce + complaint events. The
canonical webhooks + JSON payloads are documented per-provider:

- Mailgun: `https://api.mailgun.net/v3/<domain>/events`
- SES: SNS topic ` ses-bounces` / `ses-complaints`
- SendGrid: `Event Webhook` (signed with `public_key`)
- Postmark: `Bounce webhook` / `Spam Complaint webhook`

The Jade Capital Suite `MailKitSmtpEmailSender` does NOT subscribe
directly — the providers push to a webhook receiver hosted at
`ops@jadecapital.com`. The receiver adds the address to a hard-bounce
suppression list (separate from the email log) so subsequent sends are
short-circuited.

## 6. References

- `docs/email-deliverability.md` — high-level summary (executive
  audience).
- `src/3.Shared/JadeCapital.Shared.Infrastructure/Email/MailKitSmtpEmailSender.cs`
  — the .NET SMTP wrapper.
- `src/3.Shared/JadeCapital.Shared.Infrastructure/Email/MailOptions.cs`
  — env-var-binding record.
- `src/3.Shared/JadeCapital.Shared.Infrastructure/Email/MailpitSmtpEmailSender.cs`
  — localhost dev defaults.
- `openspec/specs/email-deliverability/spec.md` — canonical scenarios.
