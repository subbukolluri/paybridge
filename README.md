# PayBridge

Distributed payment orchestration system — REST API, gRPC fraud check, HTTP provider, RabbitMQ events, and SQL settlement.

Built with .NET 8, SQL Server, RabbitMQ, Redis, and the Grafana observability stack (Tempo, Prometheus, Loki).

---

## Quick Start

```powershell
# 1. Start infrastructure and all services
docker-compose up -d

# 2. Wait for health (SQL, RabbitMQ, Redis). Then send test traffic:
.\send-traffic.ps1

# 3. Open Swagger and Grafana
# Swagger:  http://localhost:5000/swagger
# Grafana:  http://localhost:3000  (admin / admin)
```

---

## Architecture

PayBridge is a **distributed payment pipeline**: four application services plus infrastructure, all containerized. Each service runs in its own process and talks over the network (REST, gRPC, HTTP, webhooks, AMQP), so distributed tracing and failure modes are real.

### Services

| Service | Protocol | Description |
|---------|----------|-------------|
| **Payment API** | REST + Webhook | Entry point. Orchestrates the flow: idempotency (Redis + DB), fraud check (gRPC), provider submit (HTTP), outbox to RabbitMQ. Receives provider webhooks. |
| **Fraud Service** | gRPC | Stub. Returns random risk score and approved/rejected. Simulates latency. |
| **Provider Service** | HTTP | Stub. Accepts payment submit, simulates processing, calls Payment API webhook asynchronously. |
| **Settlement Consumer** | RabbitMQ | Background worker. Subscribes to payment lifecycle events, persists settlement records to SQL. |

### Infrastructure

| Component | Role |
|-----------|------|
| SQL Server | Payment and settlement persistence (two databases: Payment API, Settlement Consumer). |
| RabbitMQ | Event bus. Payment API publishes PaymentInitiated / PaymentCompleted / PaymentFailed; Settlement Consumer consumes. |
| Redis | Idempotency fast-path cache (client-provided key → payment ID). DB unique constraint is source of truth. |
| Tempo | Distributed trace storage (OTLP). |
| Prometheus | Metrics. |
| Loki | Log aggregation. |
| Grafana | Dashboards (traces, metrics, logs). |

### End-to-End Flow

1. Client sends `POST /api/payments` with `idempotencyKey`.
2. Payment API: Redis lookup (fast path); if miss, insert payment in DB (unique on `MerchantId` + `IdempotencyKey`), then cache in Redis.
3. Fraud check via gRPC (approve/reject).
4. Provider submit via HTTP; on success, outbox message + payment state committed in one transaction.
5. OutboxProcessor publishes to RabbitMQ (payment.initiated / completed / failed).
6. Provider service calls webhook on Payment API; state machine validates transition, updates payment, enqueues completion event.
7. Settlement Consumer reads from queue, writes to `SettlementRecords` (event type, failure reason, etc.).

---

## Repository Structure

```
PayBridge/
├── src/
│   ├── PayBridge.PaymentApi/       # REST API, orchestration, outbox, idempotency
│   ├── PayBridge.FraudService/     # gRPC stub
│   ├── PayBridge.ProviderService/  # HTTP stub + webhook caller
│   ├── PayBridge.SettlementConsumer/  # RabbitMQ consumer → SQL
│   └── PayBridge.Contracts/        # Shared DTOs, enums, events
├── PayBridge.Logging/              # Shared logging (IAppLogger, etc.)
├── protos/                         # gRPC .proto (fraud)
├── tests/
│   └── PayBridge.Tests/            # Unit + integration tests
├── observability/
│   ├── tempo/                      # Tempo config
│   ├── prometheus/                 # Prometheus config
│   ├── loki/                       # Loki config
│   └── grafana/                    # Datasources, dashboards
├── docs/
│   └── DESIGN.md                  # Design doc (SLOs, runbooks, resilience)
├── docker-compose.yml
├── send-traffic.ps1                # Script to POST payments to the API
└── README.md
```

- **Payment API** holds the real business logic: payment state machine, idempotency (Redis + DB), outbox pattern, metrics.
- **Fraud** and **Provider** are stubs for assignment/demo; replace with real integrations later.
- **Settlement Consumer** is fully implemented: subscribe to queue, dedupe, persist.

---

## How to Run

### Full stack (Docker)

```powershell
docker-compose up -d
```

Starts: SQL Server, RabbitMQ, Redis, Tempo, Prometheus, Loki, Grafana, Payment API, Fraud Service, Provider Service, Settlement Consumer. Payment API and Settlement Consumer use Polly to retry RabbitMQ connection at startup.

### Send traffic

```powershell
.\send-traffic.ps1
```

Posts 200 payment requests to `http://localhost:5000/api/payments` with unique idempotency keys and a short delay between calls. Use Swagger or curl with the same body (and same `idempotencyKey`) twice to verify idempotency.

### Local development (without Docker for apps)

Run SQL Server, RabbitMQ, Redis (and optionally Tempo/Loki) via Docker; run the .NET apps from Visual Studio or `dotnet run`, pointing config to `localhost` (see `appsettings.json` and environment variables in `docker-compose` for ports).

---

## What to Check

| Check | How |
|-------|-----|
| **API health** | `GET http://localhost:5000/health/ready` — includes SQL Server, Redis, RabbitMQ. |
| **Payments created** | Query `Payments` table in the Payment API database (connection string in appsettings / env). |
| **Settlement records** | Query `SettlementRecords` in the Settlement Consumer database. |
| **RabbitMQ** | Management UI: http://localhost:15672 (guest/guest). Queue bindings and message flow. |
| **Redis** | Idempotency keys: `payment:idempotency:{merchantId}:{idempotencyKey}`. Use Redis CLI or Redis Insight. |
| **Logs** | Grafana → Explore → Loki; filter by `service` label (e.g. PayBridge.PaymentApi). |
| **Traces** | Grafana → Explore → Tempo; search by trace ID or service. |
| **Metrics** | Grafana → Explore → Prometheus; e.g. `payment_requests_total`, `payment_latency_ms`. |

---

## Technology Stack

| Category | Technology |
|----------|------------|
| Runtime | .NET 8 |
| API | ASP.NET Core (REST), gRPC (Fraud), HTTP (Provider) |
| Data | SQL Server (EF Core), Redis (StackExchange.Redis) |
| Messaging | RabbitMQ (RabbitMQ.Client 7.x) |
| Resilience | Polly (RabbitMQ connection retry at startup) |
| Logging | Serilog (Compact JSON, Loki sink), ReadFrom.Configuration |
| Observability | OpenTelemetry (traces, metrics), Tempo, Prometheus, Loki, Grafana |
| Testing | xUnit, FluentAssertions, Moq, EF Core InMemory |

---

## Design Document

Detailed design, SLOs, runbooks, and tradeoffs are in **[docs/DESIGN.md](docs/DESIGN.md)**. It covers:

- Outbox pattern, idempotency (Redis + DB), provider timeout semantics, state machine
- SLO definitions (success rate, latency, settlement lag)
- Incident runbook (e.g. payment success rate dropped)
- PII and cost awareness
- Resilience table (outbox, idempotency, manual ACK, etc.)

### Alignment with DESIGN.md

- **Implemented:** Payment API, Fraud/Provider stubs, Settlement Consumer, SQL + RabbitMQ + Redis, outbox, idempotency (Redis fast-path + DB), webhook handling, state machine, Tempo/Prometheus/Loki/Grafana, RabbitMQ connection retry (Polly), Serilog + ReadFrom.Configuration.
- **Optional gap:** DESIGN.md section 8 lists **retry (2x, exponential)** and **circuit breaker** for *Provider HTTP calls* from Payment API. The current Payment API uses a plain `HttpClient` for the provider (no Polly retry/circuit breaker on that client). Metrics include `circuit_breaker_state` for future use. Adding Polly to the provider `HttpClient` would align fully with that part of the design.

---

## Prerequisites

- .NET 8 SDK
- Docker (and Docker Compose) for full stack
- PowerShell (for `send-traffic.ps1`)

---

## License

MIT
