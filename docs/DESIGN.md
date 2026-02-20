# PayBridge — Design Document

Author: Subrahmanyam Kolluri

---

## 1. Architecture Overview

PayBridge is a distributed payment orchestration system with four services communicating
over REST, gRPC, HTTP, webhooks, and AMQP — all fully containerized and observable.

### Services

| Service | Protocol | Description |
|---------|----------|-------------|
| **Payment API** | REST + Webhook | Orchestrates payment flow, receives provider callbacks |
| **Fraud Service** | gRPC | Stub — returns random risk score and approved/rejected |
| **Provider Service** | HTTP | Stub — simulates processing with async webhook callback |
| **Settlement Consumer** | RabbitMQ | Persists settlement records from payment lifecycle events |

### Infrastructure

| Component | Role |
|-----------|------|
| SQL Server | Payment and settlement persistence |
| RabbitMQ | Event bus for payment lifecycle events |
| Redis | Idempotency fast-path cache |
| Tempo | Distributed trace storage (OTLP) |
| Prometheus | Metrics collection |
| Loki | Log aggregation |
| Grafana | Unified dashboards (traces, metrics, logs) |

### Why this structure?

- Each service runs as a separate container communicating over the network, so distributed
  tracing is real, not simulated
- The Payment API contains the real business logic (state machine, idempotency, outbox pattern)
- Fraud and Provider are stubs with simulated latency and random outcomes
- The Settlement Consumer is fully implemented with SQL persistence and event deduplication

---

## 2. End-to-End Flow

1. Client submits payment via `POST /api/payments`
2. Payment persisted (DB-first idempotency via unique constraint)
3. Fraud check via gRPC
4. Provider submission via HTTP
5. Payment state + OutboxMessage committed in same DB transaction
6. OutboxProcessor background service publishes event to RabbitMQ
7. Provider sends webhook callback asynchronously
8. Payment state transition validated by domain state machine
9. Completion event written to Outbox
10. Settlement Consumer persists final record from queue

---

## 3. Key Design Decisions

### Outbox Pattern
Events are not published directly to RabbitMQ. An OutboxMessage is written to the database
in the same transaction as the payment state change. A background OutboxProcessor polls and
publishes to RabbitMQ. This prevents orphaned payments when the broker is unavailable.

### Idempotency
Primary enforcement is a unique DB constraint on (MerchantId, IdempotencyKey) with an
insert-first strategy. Redis serves only as a fast-path optimization — the system is correct
even if Redis is unavailable.

### Provider Timeout Semantics
Timeouts do not immediately fail the payment. Status remains Submitted and a metric is
incremented. The system awaits the webhook callback, modeling real-world "unknown state."

### State Machine
Payment status transitions are enforced by a domain state machine. Invalid transitions
throw a domain exception. Terminal states (Failed, Refunded) cannot transition further.

---

## 4. SLO Definitions

### SLO 1: Payment Success Rate
- **Metric:** `payment_success_total / payment_requests_total`
- **Target:** ≥ 99% over a rolling 5-minute window
- **Alert:** Fire when success rate drops below 98% for 2 consecutive minutes.
  Check fraud rejection spike, provider availability, and circuit breaker state.

### SLO 2: End-to-End Latency
- **Metric:** `payment_latency_ms` (P99)
- **Target:** P99 < 2 seconds
- **Alert:** Fire when P99 exceeds 2s for 3 consecutive minutes.
  Check provider latency, SQL query times, and RabbitMQ backpressure.

### SLO 3: Settlement Lag
- **Metric:** Time between PaymentCompleted event timestamp and SettlementRecord.PersistedAt
- **Target:** < 5 seconds for 99% of completed payments
- **Alert:** Fire when settlement lag exceeds 5s.
  Check consumer health, queue depth, and SQL connectivity.

---

## 5. Incident Runbook: Payment Success Rate Dropped

**Symptom:** `payment_success_total` rate drops; alerts fire on SLO 1.

**Step 1 — Triage the failure type:**
- Check `fraud_rejection_total` rate — if spiking, the fraud service may have changed behavior
- Check `provider_timeout_total` — if spiking, the provider is slow or down
- Check `circuit_breaker_state` gauge — if 2 (open), provider calls are being short-circuited

**Step 2 — Check provider health:**
- Query `provider_latency_ms` histogram — is P99 elevated?
- Check provider service logs in Grafana → Loki
- Check provider container health: `docker-compose ps provider-service`

**Step 3 — Check infrastructure:**
- Verify SQL connectivity via `/health/ready`
- Check RabbitMQ queue depth in management UI (port 15672)
- Verify Redis is responding: `/health/ready` reports Redis status

**Step 4 — Mitigate:**
- If provider is down: circuit breaker will auto-open; payments stay in Submitted state
  and will resolve when provider recovers and sends webhooks
- If queue is backed up: check Settlement Consumer logs for errors; restart if stuck
- If fraud service is rejecting everything: restart fraud service container

---

## 6. PII & Data Governance

### What is PII in this system?
- Customer email (in payment request)
- Payment amounts (financial data)
- Merchant identifiers

### How it's handled:
- **Logs:** Customer email is never logged. Log statements use payment IDs, merchant IDs,
  and status values only. Serilog structured logging ensures consistent field control.
- **Traces:** Trace attributes include payment.id, payment.merchant_id, payment.currency,
  and payment.method — no email, no amount in span attributes.
- **Metrics:** All metric labels are low-cardinality (status, outcome). No merchant IDs,
  email addresses, or payment IDs appear as metric labels.
- **Database:** PII (email) is stored only in the Payments table. Settlement records
  contain only payment ID, status, and timestamps.

---

## 7. Cost Awareness

At 1,000 payments/minute (~1.4M/day):

- **Trace sampling:** Head-based sampling at the collector level. Sample 10-20% in production
  while keeping 100% for errored traces. Reduces storage by ~80%.
- **Metric cardinality:** All labels are low-cardinality (no merchant IDs or payment IDs).
  This keeps the Prometheus time series count bounded regardless of traffic volume.
- **Log volume:** Only state transitions and errors are logged at Info level. Debug/Verbose
  is disabled in production. Compact JSON format minimizes per-log byte cost.
- **Batched export:** OpenTelemetry SDK batches spans and metrics before export, reducing
  network overhead to the collector.
- **Outbox cleanup:** Processed outbox messages should be archived/deleted on a schedule
  to prevent unbounded table growth.

---

## 8. Resilience

| Pattern | Where | Behavior |
|---------|-------|----------|
| Retry (2x, exponential) | Provider HTTP calls | Retries transient failures before giving up |
| Circuit breaker | Provider HTTP calls | Opens after 5 failures, 30s recovery window |
| Outbox | Event publishing | Guarantees at-least-once delivery even if broker is down |
| Idempotency | Payment creation | Redis fast-path + DB unique constraint prevents double-charge |
| Idempotent webhooks | Webhook handler | Terminal states ignore duplicate callbacks |
| Manual ACK | Settlement Consumer | Messages re-delivered on consumer crash |

All resilience mechanisms are observable: circuit breaker state is exposed as a Prometheus
gauge, retry attempts are logged, and outbox queue depth is visible in the database.

---

## 9. Tradeoffs

This implementation intentionally avoids:
- Heavy Clean Architecture layering (CQRS, MediatR)
- Kubernetes manifests
- Chaos engineering tooling
- Runtime kill switch for payment processing

Focus is on correctness, traceability, reliability, and clarity.
