# Architecture

## 1. System boundary

WRN AI Gateway is a per-user orchestration application around an existing managed Claude Desktop installation.

It owns:

- the WRN launcher UI;
- the local WRN OpenRouter gateway;
- encrypted per-user OpenRouter credentials;
- the signed model catalogue cache;
- the WRN mode configuration;
- safe baseline configuration metadata;
- update state;
- redacted diagnostics.

It does not own:

- Claude Desktop binaries;
- the managed Claude MSIX/package;
- Cowork's privileged Windows service;
- user conversations;
- Cowork history;
- account/session data;
- workspace files.

## 2. Logical architecture

User → WRN AI Gateway → mode controller

WTW mode:
mode controller → validated WTW configuration → official managed Claude

WRN mode:
mode controller → local loopback WRN gateway → OpenRouter API → eligible ZDR model/provider

The upstream request uses the current user's OpenRouter credential. OpenRouter workspace/organisation policy may add central guardrails where configured; the separate WTW Common AI endpoint is not part of the production inference path.
mode controller → WRN Claude profile → official managed Claude

Central plane:
maintainer publisher → signed catalogue/update source → WRN AI Gateway clients

## 3. Components

### 3.1 Launcher/UI

Responsibilities:

- greeting and navigation;
- WTW/WRN mode choice;
- status cards;
- model information;
- changelog;
- support links;
- settings and diagnostics;
- preflight orchestration.

The UI should never directly hold plaintext secrets longer than necessary.

### 3.2 Mode controller

The most safety-critical component.

Suggested states:

- UNKNOWN
- WTW_READY
- WRN_READY
- TRANSITION_TO_WTW
- TRANSITION_TO_WRN
- BLOCKED_CLAUDE_RUNNING
- DEGRADED
- ERROR

Mode transitions should be transactional:

1. inspect current state;
2. build intended target state;
3. write staged configuration;
4. validate staged configuration;
5. atomically activate;
6. run postcondition checks;
7. roll back on failure.

Do not scatter direct configuration edits throughout the codebase.

### 3.3 WRN OpenRouter gateway

A native per-user loopback process.

Known prototype characteristics that should be preserved unless superseded:

- binds only to 127.0.0.1;
- exposes only the compatibility endpoints Claude needs;
- keeps the upstream OpenRouter key outside Claude configuration;
- enforces WRN routing policy;
- uses TLS 1.2+ upstream;
- supports streaming;
- supports tool-call and tool-result continuation;
- rejects unrecognised model aliases;
- provides a small health endpoint;
- does not require admin rights.

The production implementation must additionally translate upstream failures into a stable internal error taxonomy.

### 3.4 Credential store

Use Windows CurrentUser data protection for local encryption.

The application should store:

- encrypted key bytes;
- minimal metadata such as validation time and optional key label.

It should not store plaintext key material.

### 3.5 Catalogue client

Responsibilities:

- download catalogue and signature;
- verify signature using an embedded public key;
- enforce schema/version;
- enforce minimum gateway/application versions;
- cache last-known-good;
- reject rollback/downgrade where policy requires;
- stage rather than interrupt active Claude;
- expose model/changelog information to the UI.

### 3.6 Publisher

A maintainer tool, not distributed with end-user administrative credentials.

Responsibilities:

- edit model catalogue;
- run qualification checks;
- create changelog;
- sign the release;
- publish atomically.

The private signing key must not be present in ordinary client installations.

### 3.7 Updater

Application updates and catalogue updates are separate.

Catalogue updates are lightweight and frequent.

Application updates are versioned artifacts with signature verification, staging, atomic activation, and rollback.

## 4. Mode switching and Claude-running behaviour

The system must not rewrite mode configuration under a running Claude instance.

If a different mode is requested while Claude is running:

- detect the running process;
- explain that Claude needs to close to switch mode;
- request/guide a normal close;
- wait for exit;
- only then transition.

Do not force-kill Claude as the normal switching path.

## 5. Baseline preservation

The first production implementation must discover the minimum safe configuration surface required to move between normal WTW mode and WRN third-party inference mode.

The exact file set is deliberately not frozen in this document because the round-trip has not yet been qualified on the current managed Claude build.

Once established, the mode controller must use an explicit allowlist. Any write outside that allowlist should require a documented architecture decision.

Baseline snapshots must contain configuration only, not conversation/session data.

Baseline metadata should include the Claude version against which it was captured. Version changes may require revalidation before WRN mode is offered.

## 6. History isolation

History safety should be enforced architecturally rather than procedurally.

The switcher should never need to read the contents of history stores.

Where practical, code should contain denylisted paths/patterns for known Claude data stores and tests should assert that no mode transition writes there.

## 7. Dynamic model discovery

Preferred architecture:

Claude uses a stable WRN-facing set of aliases and the gateway resolves those aliases from the signed catalogue.

If Claude supports reliable dynamic model discovery through the gateway, the model picker should be driven from catalogue data.

If dynamic discovery is not reliable, the fallback architecture is:

- receive catalogue;
- generate WRN profile model definitions;
- stage them;
- apply only while Claude is closed;
- activate on next WRN launch.

Do not rewrite model configuration while Claude is active.

## 8. Error containment

Preflight should detect predictable failures before Claude opens.

Runtime gateway errors should be normalised.

Suggested internal categories:

- AUTH_INVALID
- AUTH_REVOKED
- NETWORK_UNAVAILABLE
- OPENROUTER_UNAVAILABLE
- RATE_LIMITED
- BUDGET_EXHAUSTED
- POLICY_BLOCKED
- MODEL_UNAVAILABLE
- NO_ZDR_ROUTE
- CATALOGUE_INVALID
- CATALOGUE_STALE
- GATEWAY_INCOMPATIBLE
- CLAUDE_STATE_UNSUPPORTED
- UNKNOWN_UPSTREAM

The UI maps these to short friendly explanations.

## 9. Same-model failover

Provider failover for the same requested model is allowed when compatible with policy.

Silent fallback to a different model is not allowed.

If a selected model is unavailable, the user should be told and offered another visible model.

## 10. Distribution abstraction

The catalogue/update source is intentionally abstract.

Development may use a simple Git-based or HTTP-backed source.

Production requirements:

- accessible without admin rights;
- authenticated or publicly safe as appropriate;
- supports atomic publication;
- supports detached signatures;
- does not require embedding privileged credentials in clients.

## 11. Architectural anti-patterns

Do not:

- ship a copied/repacked Claude executable;
- fake packaged identity in production;
- run Cowork service in an unprivileged foreground process;
- bypass Cowork named-pipe ownership checks;
- modify Program Files/WindowsApps;
- call Remove-AppxPackage;
- install privileged services;
- patch Claude binaries;
- silently migrate Claude history;
- hide a cross-model fallback from the user;
- store a shared organisation API key in the application.
