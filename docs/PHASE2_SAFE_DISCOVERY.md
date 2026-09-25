# Phase 2A — Safe Claude discovery

## Purpose

Phase 2A is deliberately read-only with respect to Claude.

Its job is to identify the current Claude installation and mode, prove the minimum configuration surface needed for a future WTW ↔ WRN transition, and fail closed when the environment is not the healthy managed Claude baseline.

It must not make a mode change.

## Current implementation boundary

The native application can generate a discovery report with:

WRN-AI-Gateway.exe --discovery-report <path>

The report:

- identifies the observed Claude installation class;
- detects running Claude processes;
- classifies the current Claude configuration as WTW, WRN, another third-party profile, degraded, or unknown;
- reports only the three candidate Claude configuration paths currently under investigation;
- produces dry-run WTW and WRN transition plans;
- blocks transition planning for unsupported/recovery installs, malformed configuration, a running Claude instance, or an invalid source mode;
- contains no model-specific business logic.

The command reads only the explicitly identified Claude configuration files. The requested report path is WRN-owned diagnostic output, not a Claude data store.

## Candidate Claude configuration write allowlist

The current discovery hypothesis is limited to:

1. %LOCALAPPDATA%\Claude-3p\claude_desktop_config.json
2. %LOCALAPPDATA%\Claude-3p\configLibrary\_meta.json
3. %LOCALAPPDATA%\Claude-3p\configLibrary\<WRN profile id>.json

This is a hypothesis until it is re-qualified against the current healthy managed Claude build.

No future transition implementation may expand this list merely because another Claude file is convenient to modify. Expansion requires evidence and explicit review.

## History/data exclusion

The mode controller must not read, copy, back up, migrate, delete, or rewrite:

- conversation history;
- Cowork session history;
- IndexedDB;
- Local Storage;
- account cookies/tokens;
- Claude-created workspace/project/session contents.

History preservation is achieved by leaving those stores outside WRN ownership.

## Evidence recovered from the prior prototype

A configuration snapshot captured immediately before the previous WRN third-party profile activation did not contain deploymentMode.

The successful prototype subsequently added:

- deploymentMode = "3p" in the desktop configuration;
- one WRN-owned config-library profile;
- one config-library metadata entry selecting that WRN profile.

That is useful historical evidence, not authority for production writes. The current managed Claude version must be observed before the allowlist and exact field-level transition are accepted.

## 25 September 2026 local observation

The initial Phase 2A probe correctly classified the current work-laptop runtime as an unsupported diagnostic/recovery build rather than a healthy managed Claude baseline.

The observed running executable was the old Claude-Recovery diagnostic copy and no managed-package marker was found by the current native discovery probes.

Accordingly:

- the discovery layer blocks WTW → WRN activation;
- it also blocks WRN → WTW restoration;
- no Claude configuration was changed;
- this state must not be used to approve the production transition contract.

The official managed Claude installation must be restored/confirmed before the healthy-baseline acceptance checkpoint.

## Remote model control constraint

Phase 2A contains no fixed model names or OpenRouter model IDs.

Before live switching is enabled, Phase 2B/2C must provide the signed catalogue runtime and maintainer publisher. The production WRN profile/gateway will therefore be generated from verified catalogue state rather than a permanently compiled shortlist.
