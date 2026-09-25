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

## Phase 2A — Safe Claude mode discovery

**Status: in progress.**

Goal: establish the minimum safe switching surface on a healthy managed Claude installation.

Deliverables:

- official Claude discovery;
- Claude-running detection;
- read-only baseline analysis;
- explicit configuration allowlist proposal;
- WTW/WRN transition state machine;
- dry-run transition report;
- no history writes;
- fail-closed handling for copied/recovery/unsupported Claude installations.

Exit: mode transition plan is empirically grounded on the current managed Claude build.

## Phase 2B — Signed dynamic model catalogue runtime

Goal: make the model service remotely maintainable before live switching is enabled.

Deliverables:

- catalogue schema;
- signature verification;
- last-known-good cache;
- dynamic/generative Claude model list path;
- changelog;
- recommended model;
- model qualification metadata;
- no permanent model-name/OpenRouter-ID logic in the mode controller;
- safe pull-on-launch and periodic refresh.

Exit: a signed catalogue change published remotely can add/remove/update a model on a test client without rebuilding the application.

## Phase 2C — Maintainer publisher

Goal: allow the maintainer to publish qualified model changes from the maintainer laptop before beta rollout.

Deliverables:

- publisher UI/CLI;
- OpenRouter existence and ZDR-route checks;
- inference/streaming/tool qualification;
- gateway mapping and Claude/Cowork smoke checks where practical;
- changelog generation;
- signing;
- atomic publication;
- publication audit metadata.

Exit: model publication is a repeatable bounded workflow and a test laptop consumes the new signed catalogue automatically.

## Phase 3 — Round-trip switching

Goal: implement WTW ↔ WRN switching safely against the dynamic catalogue runtime.

Deliverables:

- transactional configuration activation;
- rollback;
- direct-OpenRouter loopback gateway start/stop integration;
- preflight;
- repeated round-trip qualification;
- history preservation qualification;
- Cowork managed-service qualification.

Exit: release-blocking round-trip tests pass without any fixed production model list in the switching layer.

## Phase 4 — Credential onboarding and hardened gateway

Goal: remove all technical onboarding from colleagues.

Deliverables:

- hidden per-user OpenRouter key input;
- validation;
- DPAPI CurrentUser storage;
- connection status;
- replace/test key;
- normalised error taxonomy;
- bounded retry;
- same-model provider failover;
- request-level ZDR/data-collection enforcement;
- friendly failure mapping.

Exit: colleague can install and open WRN Claude without PowerShell/JSON.

## Phase 5 — Application updater

Goal: update the launcher/gateway separately from the model catalogue without admin or manual reinstall.

Deliverables:

- signed release manifest;
- artifact verification;
- staged update;
- safe activation;
- rollback;
- update UI.

Exit: an application update can be published from the maintainer environment and can fail without breaking the existing installation.

## Phase 6 — Failure containment qualification

Goal: make non-happy paths fit for non-technical users.

Deliverables:

- fault injection;
- full failure matrix;
- screenshot/UX review;
- no-raw-error acceptance;
- diagnostics bundle.

Exit: expected failures are understandable and recoverable.

## Phase 7 — Small WRN pilot

Goal: validate on multiple real colleague laptops.

Pilot should be deliberately small.

Measure:

- first-run success;
- switching reliability;
- history preservation;
- remote catalogue propagation;
- application update propagation;
- model usability;
- support burden;
- error frequency.

Do not scale until pilot issues are addressed.

## Phase 8 — Optional integrations

Only after desktop launcher is stable:

- VS Code / Claude Code;
- browser-based credential authorisation if appropriate;
- richer publisher automation;
- usage/health telemetry consistent with policy.

These are explicitly secondary to the core Claude Desktop experience.
