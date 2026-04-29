# Immutable audit log + SIEM export

**One-liner:** Every prompt, tool call, file change, and PR action recorded; stream to Splunk/Datadog/S3.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

Required for SOC2, ISO 27001, HIPAA, and any regulated industry (banks, health, gov). Also load-bearing for incident response — when an agent makes a bad change, you need to reconstruct what it saw, what it did, and who let it.

Buyer: Security / Compliance.

## Sketch

- Append-only event store. Each event signed (HMAC chain or per-event signature).
- Event shapes: session lifecycle, prompts (with redaction options), tool calls (request + response), file diffs, PR creates/merges, policy denies.
- Exports:
  - S3 bucket (customer-owned).
  - Splunk HEC, Datadog Logs, generic webhook.
- Retention: configurable, default 1 year. Tombstones for deletes (don't actually delete past retention).
- Search UI: simple filter-by-user/session/date, deep-link from fleet view.

## Open questions

- Default retention? Industries differ (finance often 7y, gen-tech 1y).
- PII redaction: opt-in per-org? Hard mode that strips file contents, soft mode that keeps diffs?
- Do we log full prompts and outputs, or hashes + on-demand fetch from session storage?
- Storage cost — full-fidelity transcripts at scale get expensive. Tiered (hot/cold)?

## Notes

One of the three I'd ship first in MVP enterprise. Pair with file 02 (analytics) and file 06 (playbooks) for a balanced first release. Defensive-but-deal-unblocking.
