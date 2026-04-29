# Self-hosted / VPC deployment with BYO key

**One-liner:** Customer-deployed control plane in their own cloud, routing through their Anthropic, Bedrock, or Vertex account.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

The single biggest unlock for banks, healthcare, EU customers, government, and anyone subject to data-residency rules. Their code cannot leave their VPC; their model traffic cannot route through a third party.

Buyer: Security / Infra. The deal-doubling feature for regulated buyers.

## Sketch

- **Control plane:** runs in customer's cloud. Containerized; helm chart for k8s, docker-compose for smaller deployments.
- **Components:** auth (terminates SSO from file 01), policy store (file 03), audit ingest (file 04), analytics rollups (file 02), session metadata.
- **Client (desktop app):** points at customer's control plane URL. No traffic to Conclave-hosted services.
- **Model routing:** customer configures Anthropic API key, Bedrock IAM role, or Vertex service account. Control plane proxies model calls, applies policy, logs to audit.
- **Update channel:** customer-controlled (they pull updates on their schedule, not ours).

## Open questions

- What's the minimum viable footprint? Single container vs full microservices?
- Do we ship a "lite" hosted version for SMB enterprises that don't want to operate it themselves?
- Telemetry: what (if anything) phones home? Anonymized usage stats? Crash dumps? Off by default for self-hosted.
- Licensing: how do we enforce seat counts in an air-gapped deployment?
- Ongoing cost: who runs upgrades? SLA model?

## Notes

Heaviest single item in the enterprise tier. Architecturally precedes most others (auth, audit, analytics all need a backend). Can be deferred behind a hosted-only enterprise tier in v1, but eventually unavoidable for top-of-market deals.
