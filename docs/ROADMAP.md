# Roadmap

The roadmap is intentionally incremental. Each phase should deliver a measurable, reviewable capability rather than one large feature branch.

## Phase 0 — Documentation and architecture

**Status: complete.**

Goal: establish repository authority and prevent scope drift.

Deliverables:

- product requirements;
- architecture;
- project context;
- security/policy boundaries;
- catalogue/update design;
- UX specification;
- testing/acceptance;
- architecture decisions.

Exit: a new developer can explain the system, constraints, evidence, unknowns, and next work purely from the repository.

## Phase 1 — WRN AI Gateway shell

**Status: complete. Native shell and no-admin installer implemented, qualified and owner-accepted.**

Goal: polished standalone launcher with no destructive behaviour.

Deliverables:

- Windows user-space application shell;
- home screen;
- greeting;
- WTW/WRN cards;
- Models/Updates/Support navigation;
- local app version;
- no-admin packaging;
- placeholder/mock health states.

No Claude configuration writes yet.

Exit: launcher is visually credible and installable without admin.

## Phase 2 — Safe Claude mode discovery

Goal: establish the minimum safe switching surface on a healthy managed Claude installation.

Deliverables:

- official Claude discovery;
- Claude-running detection;
- read-only baseline analysis;
- explicit configuration allowlist proposal;
- WTW/WRN transition state machine;
- dry-run transition report;
- no history writes.

Exit: mode transition plan is empirically grounded.

## Phase 3 — Round-trip switching

Goal: implement WTW ↔ WRN switching safely.

Deliverables:

- transactional configuration activation;
- rollback;
- gateway start/stop integration;
- preflight;
- repeated round-trip qualification;
- history preservation qualification;
- Cowork managed-service qualification.

Exit: release-blocking round-trip tests pass.

## Phase 4 — Credential onboarding and hardened gateway

Goal: remove all technical onboarding from colleagues.

Deliverables:

- hidden key input;
- validation;
- DPAPI CurrentUser storage;
- connection status;
- replace/test key;
- normalised error taxonomy;
- bounded retry;
- same-model provider failover;
- friendly failure mapping.

Exit: colleague can install and open WRN Claude without PowerShell/JSON.

## Phase 5 — Signed dynamic model catalogue

Goal: decouple model list from application releases.

Deliverables:

- catalogue schema;
- signature verification;
- last-known-good cache;
- dynamic/generative Claude model list path;
- changelog;
- recommended model;
- model qualification metadata.

Exit: model can be added/removed without rebuilding client.

## Phase 6 — Maintainer publisher

Goal: allow a maintainer to publish catalogue changes from their laptop.

Deliverables:

- publisher UI/CLI;
- model qualification checks;
- changelog generation;
- signing;
- atomic publication;
- publication audit metadata.

Exit: model publication is a repeatable two-minute workflow.

## Phase 7 — Application updater

Goal: user-space application updates without admin.

Deliverables:

- release manifest;
- artifact verification;
- staged update;
- safe activation;
- rollback;
- update UI.

Exit: update can fail without breaking existing installation.

## Phase 8 — Failure containment qualification

Goal: make non-happy paths fit for non-technical users.

Deliverables:

- fault injection;
- full failure matrix;
- screenshot/UX review;
- no-raw-error acceptance;
- diagnostics bundle.

Exit: expected failures are understandable and recoverable.

## Phase 9 — Small WRN pilot

Goal: validate on multiple real colleague laptops.

Pilot should be deliberately small.

Measure:

- first-run success;
- switching reliability;
- history preservation;
- model usability;
- support burden;
- error frequency;
- update/catalogue propagation.

Do not scale until pilot issues are addressed.

## Phase 10 — Optional integrations

Only after desktop launcher is stable:

- VS Code / Claude Code;
- browser-based credential authorisation if appropriate;
- richer publisher automation;
- usage/health telemetry consistent with policy.

These are explicitly secondary to the core Claude Desktop experience.
