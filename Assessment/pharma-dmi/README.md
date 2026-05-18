# PharmaDMI – Digital Manufacturing Intelligence Platform

> **Engineering Manager Assessment Submission**  
> Built with .NET 8 Microservices + Angular-style UI + Open-Source AI (Llama)

---

## 🏗 Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     PharmaDMI Platform                          │
│                                                                 │
│  ┌─────────────┐   ┌──────────────┐   ┌────────────────────┐   │
│  │  Angular UI │   │ TelemetryAPI │   │   Alert Service    │   │
│  │  (Port 4200)│ → │  (Port 5001) │ ← │   (Port 5002)      │   │
│  │  Dashboard  │   │  + Simulator │   │  + AnomalyDetector │   │
│  └──────┬──────┘   └──────┬───────┘   └────────┬───────────┘   │
│         │                 │                     │               │
│         └─────────────────┼─────────────────────┘               │
│                           ↓                                     │
│                  ┌────────────────┐                             │
│                  │ Insight Service│  ← Open-Source AI (Llama)   │
│                  │  (Port 5003)   │    Ollama / HF / Claude opt │
│                  └────────────────┘                             │
└─────────────────────────────────────────────────────────────────┘
```

### Microservices

| Service | Port | Responsibility |
|---------|------|----------------|
| **TelemetryService** | 5001 | Simulates & stores machine telemetry (SQLite), exposes REST APIs |
| **AlertService** | 5002 | Polls telemetry every 8s, detects threshold breaches, stores alerts |
| **InsightService** | 5003 | Aggregates context from both services, calls **open-source LLM** (Llama), answers queries |
| **Angular UI** | File / 4200 | Real-time dashboard, machine viewer, alert management, AI chat |

### Machines Monitored

| ID | Name | Type | Block |
|----|------|------|-------|
| M001 | Reactor Vessel A | Bioreactor | A |
| M002 | Mixing Unit B | Mixer | B |
| M003 | Filtration Unit C | Filter | C |
| M004 | Dryer Unit D | Dryer | D |
| M005 | Granulator E | Granulator | A |

---

## 🚀 How to Run

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Docker Desktop](https://www.docker.com/products/docker-desktop/) for containerized run

### Option 1: Windows — One-Click Start

```bat
double-click run-all.bat
```

Or from command prompt:
```bat
run-all.bat
```

### Option 2: Linux / macOS

```bash
chmod +x run-all.sh
./run-all.sh
```

### Option 3: Manual (each in its own terminal)

**Terminal 1 — Telemetry Service:**
```bash
cd services/TelemetryService
dotnet run
# Swagger: http://localhost:5001/swagger
```

**Terminal 2 — Alert Service:**
```bash
cd services/AlertService
dotnet run
# Swagger: http://localhost:5002/swagger
```

**Terminal 3 — Insight Service:**
```bash
# Default: uses an open-source LLM (Llama family) via a free
# public gateway — no installation, no key needed.
#
# Optional overrides (any one of these will be used if set):
#   OLLAMA_URL=http://localhost:11434         # local OSS LLM
#   OLLAMA_MODEL=llama3.2                     # model for Ollama
#   HF_TOKEN=hf_xxx                           # Hugging Face Inference
#   HF_MODEL=meta-llama/Meta-Llama-3-8B-Instruct
#   ANTHROPIC_API_KEY=sk-ant-...              # Claude (optional)

cd services/InsightService
dotnet run
# Swagger: http://localhost:5003/swagger
```

**Open UI:**
```
Open angular-ui/index.html in any browser
```

### Option 4: Docker Compose

```bash
# No env vars required — open-source AI is on by default.
# Optional overrides:
#   export OLLAMA_URL=http://host.docker.internal:11434
#   export HF_TOKEN=hf_xxx
#   export ANTHROPIC_API_KEY=sk-ant-...

docker-compose up --build
# UI: http://localhost:4200
```

---

## 🤖 AI Integration (Open-Source by Default)

The InsightService talks to a Large Language Model on every query. **Out of the box it uses an open-source LLM (Llama family) via a free public gateway** — no installation, no API key, no signup. Live plant telemetry, active alerts, and machine thresholds are injected as context on every request.

**Backend priority (first available wins):**

| # | Backend | Trigger | Notes |
|---|---------|---------|-------|
| 1 | Anthropic Claude | `ANTHROPIC_API_KEY` set | Optional, closed-source |
| 2 | **Ollama** (local OSS LLM) | `OLLAMA_URL` set | Fully local, requires Ollama installed |
| 3 | **Hugging Face Inference API** | `HF_TOKEN` set | Free tier, open-source models |
| 4 | **Public open-source AI gateway** | *(default)* | Zero-install, zero-key, Llama-class model |
| 5 | Rule-based engine | Network unreachable | Final safety net |

**What gets injected as context for every backend:**
- Live telemetry summary for all 5 machines
- All active alerts with severities
- Machine thresholds and configurations
- Plant layout information

**Run a fully local open-source model (optional):**
```bash
# 1. Install Ollama once (https://ollama.com)
ollama pull llama3.2

# 2. Point InsightService at it
export OLLAMA_URL=http://localhost:11434
export OLLAMA_MODEL=llama3.2
```

**Use Hugging Face open-source models (optional):**
```bash
export HF_TOKEN=hf_xxx
export HF_MODEL=meta-llama/Meta-Llama-3-8B-Instruct
```

---

## 📡 API Reference

### Telemetry Service (port 5001)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/machines` | List all machines |
| GET | `/api/machines/{id}` | Get machine details |
| GET | `/api/machines/{id}/latest` | Latest telemetry reading |
| GET | `/api/machines/{id}/telemetry` | Historical readings |
| POST | `/api/machines/{id}/telemetry` | Push custom reading |
| GET | `/api/telemetry/summary` | All machines summary |
| GET | `/api/telemetry/history` | Recent readings (last 30 min) |

### Alert Service (port 5002)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/alerts` | All alerts |
| GET | `/api/alerts/active` | Unacknowledged alerts |
| GET | `/api/alerts/summary` | Count by severity |
| GET | `/api/alerts/{machineId}` | Alerts for a machine |
| POST | `/api/alerts/{id}/acknowledge` | Acknowledge single alert |
| POST | `/api/alerts/acknowledge-all` | Acknowledge all |

### Insight Service (port 5003)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/insights/query` | Ask AI a question |
| GET | `/api/insights/summary` | Plant summary insight |
| GET | `/api/insights/machine/{id}` | Machine-specific insight |

---

## 🏭 How It Works — For Stakeholders

### The Problem Solved
Pharma plant managers need to monitor 5+ machines simultaneously, spot anomalies before they cause batch failures, understand root causes quickly, and query plant health in plain English — without wading through raw sensor data.

### The Solution
**PharmaDMI** is a real-time Digital Manufacturing Intelligence platform that:

1. **Ingests telemetry every 5 seconds** — simulating real OPC-UA/SCADA data feeds for temperature, pressure, vibration, humidity, power, and production rate.

2. **Detects anomalies automatically** — the AlertService polls telemetry every 8 seconds, checks each parameter against configurable thresholds, and generates Warning or Critical alerts with pre-analyzed root causes.

3. **Exposes clean APIs** — three independent microservices with Swagger documentation, allowing integration with MES, ERP, or SCADA systems.

4. **Provides an AI assistant** — operators can ask "Why is Reactor A running hot?" and get a context-aware answer that references actual live telemetry data, not generic advice.

### Design Decisions & Tradeoffs

| Decision | Chosen Approach | Why | Tradeoff |
|----------|----------------|-----|----------|
| Database | SQLite | Zero-config, portable | Replace with PostgreSQL/SQL Server for production scale |
| Messaging | HTTP polling | Simple, demo-friendly | Replace with RabbitMQ/Azure Service Bus for real-time events |
| Auth | None | Demo scope | Add JWT + Azure AD for production |
| AI model | Open-source LLM (Llama) by default | No key/install needed; fully open ecosystem | Public gateway for demo; swap to self-hosted Ollama for production |
| Deployment | Docker + manual scripts | Flexible for demo | Production: Kubernetes on AKS/EKS |

### Production Roadmap (Next Steps)
- Replace SQLite → PostgreSQL with TimescaleDB for time-series
- Add SignalR WebSockets for real-time UI push
- Integrate RabbitMQ for event-driven alert pipeline
- Add JWT authentication + role-based access
- Add Prometheus metrics + Grafana dashboards
- Deploy on AKS with Helm charts
- Add OPC-UA adapter for real SCADA connectivity

---

## 📊 AI Usage Disclosure

| Area | AI Tool Used | How |
|------|-------------|-----|
| Code scaffolding | Claude (Anthropic) | Architecture design, service structure |
| Root cause text | Rule-based templates | Pre-authored by developer |
| AI assistant feature | **Open-source LLM (Llama family)** via public gateway; optional Ollama / Hugging Face / Claude | Runtime: answers operator queries with live plant context |
| UI design | Manual HTML/CSS | Developer-authored |

All code was reviewed, understood, and customized by the submission author.

---

## 🗂 Project Structure

```
pharma-dmi/
├── services/
│   ├── TelemetryService/          # .NET 8 Web API
│   │   ├── Controllers/           # MachinesController, TelemetryController
│   │   ├── Data/                  # EF Core DbContext
│   │   ├── Models/                # Machine, TelemetryReading
│   │   ├── Services/              # TelemetrySimulator (BackgroundService)
│   │   └── Program.cs
│   ├── AlertService/              # .NET 8 Web API
│   │   ├── Controllers/           # AlertsController
│   │   ├── Models/                # Alert, ThresholdConfig
│   │   ├── Services/              # AnomalyDetector (BackgroundService)
│   │   └── Program.cs
│   └── InsightService/            # .NET 8 Web API
│       ├── Controllers/           # InsightsController (multi-backend AI)
│       └── Program.cs             # Open-source LLM + Ollama/HF/Claude integration
├── angular-ui/
│   └── index.html                 # Full Angular-style SPA dashboard
├── docker/
│   └── nginx.conf
├── docker-compose.yml
├── run-all.bat                    # Windows launcher
├── run-all.sh                     # Linux/Mac launcher
├── PharmaDMI.sln
└── README.md
```
