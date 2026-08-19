# Privacy Policy — Jade Capital Suite

> ⚠️ **PLACEHOLDER — DO NOT DEPLOY WITHOUT LEGAL COUNSEL SIGN-OFF**
>
> This document is a STRUCTURAL PLACEHOLDER for the Jade Capital Suite
> Privacy Policy page. The text below is generated copy that satisfies
> a development sanity check. It MUST be replaced with the canonical
> version reviewed by legal counsel + product + compliance before the
> service is exposed to real users.
>
> The placeholder exists so that:
> - the lazy route (`/legal/privacy`) renders without runtime errors,
> - the registration form can link to a real URL,
> - GDPR Art. 13 information notices have a navigable target.
>
> Owning role: **Legal counsel + Compliance (DPO if appointed) +
> Product**. Replace this file with the final copy once the v1.0 GA
> release has legal sign-off.

## 1. Who we are

Jade Capital Suite ("we", "us", "our") operates the trading-journal
+ analytics platform accessible at the Jade Capital Suite domain. We
are the data controller for the personal data we collect via the
Service.

## 2. What data we collect

We collect the following categories of personal data:

- **Account data**: email address, display name, password hash.
- **Consent ledger**: timestamps + IP at which you accepted these
  terms and the cookie banner (Art. 7 GDPR).
- **Tenant data**: organization name + slug if you create a workspace.
- **Usage data**: which features you access, anonymised crash reports.
- **Support data**: messages you send us and our responses.

## 3. Why we collect it (legal basis)

- **Contract performance** (Art. 6.1.b GDPR): to provide the Service
  you signed up for.
- **Legitimate interest** (Art. 6.1.f GDPR): to keep the Service
  secure, prevent abuse, and improve features.
- **Consent** (Art. 6.1.a GDPR): for non-essential cookies + for
  the registration consent ledger.

## 4. How long we keep it

We keep your account data for the lifetime of your account. After you
delete your account (`DELETE /api/users/me/account`), the data enters
a 30-day grace window (you can cancel), after which it is
irreversibly anonymised + the audit trail pseudonymised.

## 5. Your rights (GDPR Art. 15-22)

You can:

- **Access** the data we hold on you — `GET /api/users/me/export`.
- **Rectify** inaccurate data — your account settings page.
- **Erase** your data — `DELETE /api/users/me/account`.
- **Restrict or object** to processing — contact us.
- **Port** your data — `GET /api/users/me/export` returns a
  machine-readable JSON.

## 6. Cookies

The cookie banner on the Service defaults to **essential only**.
You can opt into analytics cookies by clicking "Aceptar todas".
We do NOT set non-essential cookies before consent (ePrivacy
Directive 2002/58/EC art. 5(3)).

## 7. Contact + DPO

Questions about this policy or your data rights should be sent to
**privacy@jadecapital.com**. If we have appointed a Data Protection
Officer, their contact details will be listed here.

## 8. Supervisory authority

If you believe we have processed your data unlawfully, you have the
right to lodge a complaint with your local supervisory authority
(e.g., the Agencia de Acceso a la Información Pública in Argentina,
or the AEPD in Spain).

---

*Effective date: TBD by legal counsel*
*Version: placeholder (pre-v1.0)*
