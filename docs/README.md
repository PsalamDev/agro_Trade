# Implementation Plan — Index

This folder contains the technical implementation plan for the **Agro-Allied Global
Trade Platform**, derived from the product concept & technical specification.

**Positioning (locked):** the platform is an **import/export platform first** — a
cross-border agricultural trade network for Africa and global markets, corridor-neutral.
Domestic retail marketplace is a follow-on phase.

## Environment audit (this machine)

Checked before writing the plan, so the plan uses what is actually installed:

| Requirement        | Found                                            | Verdict                                  |
| ------------------ | ------------------------------------------------ | ---------------------------------------- |
| .NET SDK           | 8.0.423 **and** 10.0.302 (default)               | Use **.NET 10 (LTS)** — .NET 8 support ends Nov 2026 |
| Node.js / npm      | v24.19.0 / 11.17.0                                | Use Vite 7 + React 19 + Tailwind CSS v4  |
| SQL Server         | `MSSQLSERVER` default instance **running**        | Use `Server=localhost;...` — no LocalDB  |
| Git                | Not a repo                                        | `git init` in Phase 0                    |

## Document map

1. **[01-roadmap.md](01-roadmap.md)** — phased delivery roadmap (0→5) built
   cross-border-first: verification & exporter listings → sourcing/RFQ → trade execution
   (docs, payment, shipment) → trust/intelligence → domestic add-on → global
   infrastructure. Each phase has goal, scope, deliverables, exit criteria, dependency
   graph, and a workstream task breakdown.
2. **[02-data-model.md](02-data-model.md)** — full database design: 29 entities mapped
   from spec section 12, key columns, relationships, indexes, workflow enums
   (incoterms, payment terms, trade docs…), and the seed plan.
3. **[03-api-surface.md](03-api-surface.md)** — API versioning, auth & role model,
   pagination/filtering/error conventions, complete endpoint list per module, webhooks,
   and realtime hubs.
4. **[04-architecture.md](04-architecture.md)** — repository layout, backend layering
   rules, frontend structure, key NuGet/npm packages, cross-cutting services (auth,
   RBAC, idempotency, audit, storage, background jobs), dev setup commands, and CI/CD.
5. **[05-mvp-scope.md](05-mvp-scope.md)** — the MVP contract: spec section 13 delivered
   around import/export (verified parties, cross-border listings, international RFQs,
   trade documents, payment basics, shipment tracking), acceptance criteria per item, an
   explicit "out of scope" parking lot, the first sprint backlog, and a demo journey.
6. **[06-decisions-and-risks.md](06-decisions-and-risks.md)** — open product/technical
   decisions that need a human decision, plus the risk register (incl. spec section 18
   compliance controls).

## Reading order

Start with `01-roadmap.md` for the big picture, then `02-data-model.md` and
`03-api-surface.md` when you start Phase 0, and use `05-mvp-scope.md` as the
single source of truth for what "done" means for the MVP.