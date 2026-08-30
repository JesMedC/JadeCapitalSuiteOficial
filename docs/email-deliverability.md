# Email Deliverability

> **Wave 11 slice 11.4 — executive summary.** For the full DNS
> records + provider env-var mapping + DKIM rotation cadence, see
> [`docs/runbooks/email-deliverability.md`](./runbooks/email-deliverability.md).
> This document is the one-page roll-up.

## Why it matters

Jade Capital Suite sends transactional emails (password recovery,
tenant invites, post-registration welcome). If Gmail/Outlook/Yahoo
treat our emails as spam, users miss password resets + account
deletion confirmations. A one-time misconfiguration can land the
domain on a block-list (recovery takes 1-3 weeks).

## TL;DR

| Provider  | SPF include | DKIM selector                 | DMARC policy   |
|-----------|-------------|------------------------------|----------------|
| Mailgun   | `mailgun.org` | `smtp._domainkey`           | `quarantine` → `reject` |
| Amazon SES| `amazonses.com` | `selector1._domainkey`   | `quarantine` → `reject` |
| SendGrid  | `sendgrid.net` | `s1._domainkey`            | `quarantine` → `reject` |
| Postmark  | `messagingengine.com` | `opendkim`         | `quarantine` → `reject` |

The Jade Capital Suite wildcard string for the production DNS is:

```text
jadecapital.com       TXT     v=spf1 include:mailgun.org include:amazonses.com ~all
smtp._domainkey       TXT     v=DKIM1; k=rsa; p=<paste from provider>
selector1._domainkey  TXT     v=DKIM1; k=rsa; p=<paste from SES>
_dmarc                TXT     v=DMARC1; p=quarantine; rua=mailto:privacy@jadecapital.com; ruf=mailto:privacy@jadecapital.com; pct=100; adkim=s; aspf=s
```

## Operational checklist

1. **On a new domain** — publish the 3 records above. Wait 24h for
   DNS to propagate.
2. **On a new provider** — set the Mail__* env vars per the table in
   `docs/runbooks/email-deliverability.md` §3.
3. **DKIM rotation** — every 90 days (publish new key as
   `selector2._domainkey`, leave selector1 intact for 14 days, then
   delete selector1).
4. **Bounce handling** — subscribe to the provider's bounce webhook
   (`ops@jadecapital.com`); the receiver adds the address to a
   hard-bounce suppression list.
5. **Verification** — send a test to `check-auth@verifier.port25.com`
   + Gmail/Outlook/Yahoo. None of the three should land in spam.

## Where to go next

- Full DNS record specs → [`docs/runbooks/email-deliverability.md`](./runbooks/email-deliverability.md)
- Provider env-var mappings → same file, §3
- DKIM rotation playbook → same file, §2
- Spec scenarios → `openspec/specs/email-deliverability/spec.md`
- Source code → `src/3.Shared/JadeCapital.Shared.Infrastructure/Email/`

## Owners

- **Ops:** ops@jadecapital.com (DNS rotation, webhook receiver)
- **Privacy:** privacy@jadecapital.com (DMARC reports, complaint triage)
- **Legal:** legal@jadecapital.com (provider terms + DPA review)
