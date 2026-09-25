# Phase 3 — Fail-closed mode coordinator

## Purpose

The mode coordinator is the single authority between user-facing mode selection and the lower-level discovery, catalogue, gateway, and transition components.

At this checkpoint it is intentionally **not** connected to the Home launch buttons.

It can answer:

- is the current Claude installation a supported managed build;
- is Claude closed;
- is the current source mode valid for the requested transition;
- is a signed catalogue available;
- is a usable local gateway configuration present;
- is a CurrentUser-DPAPI OpenRouter credential present and decryptable;
- is the packaged loopback gateway available;
- is the gateway already healthy on the expected catalogue release;
- is a transaction recovery pending;
- is the WRN ownership baseline present for WTW restoration;
- can the exact transition plan be compiled.

It never reports credential values.

## Read-only preflight command

The native app now supports:

`WRN-AI-Gateway.exe --mode-preflight-report <wrn|wtw> <report-path>`

The report distinguishes:

- **PreflightCompatible** — the machine/configuration can compile the requested plan;
- **LiveExecutionAllowed** — actual Claude writes may execute.

For this development checkpoint, `LiveExecutionAllowed` remains false even when a healthy fixture is preflight-compatible because:

`TransitionSafety.LiveClaudeWritesEnabled = false`

The command writes only the explicitly requested WRN diagnostic report. It does not alter Claude configuration.

## Pending transaction behaviour

A pending transition blocks compilation of another plan.

The coordinator exposes a recovery-before-planning operation backed by the Phase 3 DPAPI-protected transaction journal.

Fixture qualification proves:

1. an interrupted transition is reported as pending;
2. a new preflight is blocked;
3. recovery restores the safe prior fixture state;
4. preflight can then be retried.

The read-only preflight command does not invoke recovery automatically.

## Gateway prerequisites

For WRN activation, preflight requires:

- packaged gateway binary;
- valid local gateway config;
- valid loopback port;
- local Claude-facing bearer credential;
- decryptable CurrentUser-DPAPI OpenRouter credential;
- valid signed catalogue.

A stopped but otherwise valid gateway does **not** make the machine incompatible. Instead the report returns:

`GatewayStartRequired = true`

This lets a beta qualification pass establish compatibility before any runtime process is started.

## Owned gateway lifecycle

The coordinator slice also provides an internal owned gateway lifecycle.

For a compatible fixture it can:

1. start the exact packaged `WRN-AI-Gateway-Gateway.exe`;
2. pass an isolated WRN state root;
3. wait for `/health`;
4. require the health response to advertise the expected signed catalogue release;
5. persist only PID/executable/start metadata;
6. stop only a process whose live executable path exactly matches the packaged gateway.

If a stored PID now belongs to another executable, stop fails closed with an ownership mismatch rather than terminating it.

Gateway state-root handling was corrected during this slice: catalogue cache, credentials, config, logs, and PID state now all follow the supplied WRN state root. Production behaviour is unchanged because the production state root remains `%LOCALAPPDATA%\WRN-AI-Gateway`.

## Windows fixture qualification — 25 September 2026

Passing fixture cases:

- healthy synthetic WTW → WRN preflight;
- signed catalogue validation;
- gateway binary discovery;
- gateway config validation;
- CurrentUser-DPAPI credential validation;
- three-mutation transition compilation;
- stopped gateway correctly reported as start-required;
- owned gateway starts and reaches healthy state;
- healthy running gateway removes the start-required flag;
- owned gateway stops by exact process identity;
- local/OpenRouter credential values do not appear in reports;
- missing OpenRouter credential blocks WRN preflight;
- recovery-diagnostic Claude installation blocks preflight;
- pending transaction blocks planning;
- recovery allows a safe retry;
- healthy synthetic WRN → WTW restoration preflight compiles;
- live execution remains disabled.

## Current development-laptop observation

The real work laptop reports:

- installation: `RecoveryDiagnostic`;
- current mode: `Wrn`;
- WRN preflight compatible: false;
- block reason: unsupported Claude installation; a healthy managed Claude installation is required;
- live execution enabled: false.

The verifier hashes the three Claude configuration files before and after the real preflight command and proves they are unchanged.

## UI boundary

`AppController` still does not invoke:

- `ModeCoordinator`;
- `ClaudeTransitionExecutor.Execute`.

The accepted Home launch cards therefore remain non-destructive.

## Next product slice

Credential onboarding can now be developed independently of the damaged Claude installation.

The next slice should provide:

- first-run OpenRouter credential entry;
- validation without exposing the key;
- CurrentUser-DPAPI persistence;
- replace/test/remove operations;
- friendly connection status;
- gateway config/local bearer generation;
- no PowerShell/JSON requirement for colleagues.

Once that exists, a clean colleague laptop can install the beta, enter its key, and run preflight before any live Claude transition is enabled.
