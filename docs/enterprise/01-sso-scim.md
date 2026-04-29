# SSO + SCIM provisioning

**One-liner:** SAML/OIDC sign-in and automated user lifecycle via SCIM v2.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

Most enterprise security teams won't even start a procurement conversation without SSO. SCIM removes the manual step of provisioning/deprovisioning seats when employees join or leave — also the failure mode that gets a vendor cut at renewal.

Buyer: IT / Security. User-invisible — but a hard gate.

## Sketch

- SAML 2.0 and OIDC sign-in. Support Okta, Entra ID, Google Workspace, generic.
- SCIM 2.0 endpoint (`/scim/v2/Users`, `/scim/v2/Groups`) with bearer-token auth.
- Map IdP groups → Conclave roles (admin, member, viewer).
- JIT user creation on first SSO login if SCIM hasn't pre-provisioned.
- Session-management: enforce SSO-only login on the org (no password fallback).

## Open questions

- Do we need fine-grained role mapping in v1, or is admin/member enough?
- Is per-team scoping (group → team) required day one or follow-up?
- Where does this live architecturally — does this assume the self-hosted control plane (file 05) is already there, or does Conclave grow a hosted backend just for auth first?

## Notes

Defensive feature — table stakes, not differentiating. Validated in every enterprise B2B SaaS playbook.
