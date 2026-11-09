# ADR-013: Brownfield Environment Discovery & Reconciliation

| Item | Value |
|---|---|
| Status | Accepted |
| Date | 2026-11-09 |
| Author | Antigravity (AI Assistant) / Pranjal (System Owner) |
| Relates to | §13.5 Brownfield discovery & Section 1 of Implementation Instructions |

---

## 1. Context

In accordance with §13.5 of `JobPilot_Architecture_Spec_v1.1.md` and user instructions, a comprehensive environment inspection was conducted before creating or modifying code. The objective was to discover any existing backend, frontend, database schemas, tables, migrations, or configuration files, document the findings, and ensure no existing databases or data are impacted or overwritten.

---

## 2. Environment Discovery Findings

### 2.1 Workspace and Directory Structure
- **Workspace Location:** `F:\JobPilot`
- **Initial Directory State:** The directory was entirely empty. No previous source files, projects, or `.git` repository existed.
- **Action Taken:** Initialized local Git repository (`git init`) to establish version control and traceable change history.

### 2.2 System Prerequisites and Toolchain
- **Operating System:** Windows 11 (OS Architecture: x64)
- **.NET SDKs:**
  - .NET 10 SDK: `10.0.403` (Primary target for .NET 10 development)
  - .NET 8 SDK: `8.0.427` (Also present)
- **Global .NET Tools:**
  - `dotnet-ef`: `10.0.5` (Installed and active)
- **Node.js & Package Manager:**
  - Node.js: `v24.18.0` (LTS/current)
  - npm: `10.8.1`
- **PostgreSQL Database Server:**
  - Service: `postgresql-x64-17` (PostgreSQL 17.x, running actively on `localhost:5432`)
  - Binaries: `D:\Program Files\PostgreSQL\bin`
  - Authentication: `scram-sha-256`, accessible on loopback
  - Existing databases inspected: 21 databases present on the server (e.g., `dev`, `Testing`, `EntityFramework`, `vitatrack_develop`, etc.).
  - Inspection for JobPilot entities: Checked `information_schema.tables` across databases for any tables matching `%job%`, `%application%`, `%profile%`, or schema `jobpilot`. Zero JobPilot tables or schemas exist. All existing databases belong to other local projects and will remain completely untouched.
- **Docker / Containers:**
  - Docker CLI `29.7.2` installed, but Docker Desktop daemon is not currently running.
  - Native PostgreSQL 17 is already running locally and accepting connections, providing immediate high-performance database support for local development without container overhead.

---

## 3. Reconciliation Decisions

1. **Database Strategy:**
   - Create a dedicated development database: `jobpilot_dev`.
   - Create a dedicated test database: `jobpilot_test`.
   - All tables will reside inside the dedicated schemas `jobpilot` and `identity` within `jobpilot_dev`.
   - No modifications, drops, or alterations will be made to any of the other 21 existing databases.

2. **Codebase Strategy:**
   - Clean, greenfield implementation within `F:\JobPilot` adhering strictly to the architecture laid out in `JobPilot_Architecture_Spec_v1.1.md`.
   - Adhere to the multi-project Clean Architecture layout:
     - `src/JobPilot.Domain`
     - `src/JobPilot.Application`
     - `src/JobPilot.Infrastructure`
     - `src/JobPilot.Api`
     - `src/JobPilot.AutomationWorker`
     - `frontend/` (React + TypeScript + Vite + Tailwind/shadcn-ui)
     - `tests/` (Domain, Application, Infrastructure, Api, Automation, ArchitectureTests)
     - `fixtures/mock-workday`

3. **Migration Strategy:**
   - Follow the incremental migration plan from §13.4:
     - `0001_Baseline`: Core profile tables, provenance, identity, resumes, source documents, saved answers.
     - `0002_JobsAndApplications`: ATS tenant, job post, immutable discovery trigger, job application, status history.
     - `0003_Automation`: Application session, fields, mappings, answers, automation commands, automation events, worker heartbeat.
     - `0004_AiAudit`: AI interaction logs, retention support.
   - Migrations will be executed explicitly (`dotnet ef database update`) rather than silently on application startup.

---

## 4. Consequences & Safety Guarantees

- Zero risk to existing databases or developer projects on the host machine.
- Direct alignment with the architecture specification v1.1.
- All prerequisites for .NET 10, Node 24, EF Core 10, and PostgreSQL 17 are confirmed and operational.

