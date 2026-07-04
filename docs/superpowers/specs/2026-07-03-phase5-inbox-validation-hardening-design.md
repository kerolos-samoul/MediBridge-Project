# Phase 5 Inbox Validation Hardening Design

## Problem

The opt-in inbox benchmark sends 20 warm-ups and 200 measured HTTP requests through one authenticated Doctor partition while the production policy correctly permits 60 requests per minute. The resulting 429 responses measure throttling rather than inbox latency. Separately, the Phase 5 integration tests claim a broader date, authorization, pagination, and asset-state matrix than they directly assert.

## Root Cause

The benchmark models load as one user's burst instead of representative aggregate load across independent user partitions. The limiter is intentionally user-partitioned, so changing or bypassing it would invalidate the HTTP execution path and weaken security. The proof gaps arose because broad scenarios were combined into a few happy-path tests without a traceable case matrix.

## Design

- Keep production rate-limiting code and configuration unchanged.
- Seed four approved Doctors, each with an equivalent 100-item current-day inbox and approved assets.
- Create one authenticated client per Doctor and distribute 20 warm-ups and 200 measured requests round-robin. Each partition receives 55 total requests, below the production limit of 60.
- Attach a thread-safe EF Core `DbCommandInterceptor` to the real SQL Server-backed API host. Reset it after warm-up and count measured delivery and asset SELECTs separately.
- Require exactly one bounded delivery query and one bounded asset query per successful request, with zero signed-grant calls.
- Emit p50, p95, p99, query counts, within-target count, and failure count on successful measured runs.
- Split missing functional coverage into focused integration cases for Cairo DST/midnight visibility, default/maximum paging, role/ownership denial, and unavailable asset states.

## Security and Performance

No limiter bypass, privileged header, alternate endpoint, or production policy change is introduced. The benchmark exercises authentication, authorization, rate limiting, controller, service, repositories, SQL Server, and serialization. Query interception is confined to test infrastructure and observes commands without mutating them.

## Success Criteria

- Four partitions remain below 60 requests per minute.
- The opt-in reference run completes 200 measured requests at concurrency 10, with at least 190 under one second and zero failures.
- Measured inbox requests execute one delivery page query and one approved-assets query each and issue no storage grants.
- The declared negative and boundary scenarios have direct automated assertions.
- Focused contract/integration tests and the full solution build pass.
