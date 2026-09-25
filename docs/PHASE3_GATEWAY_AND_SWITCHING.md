# Phase 3 — Catalogue-driven gateway and transactional switching

## Purpose

Phase 3 integrates the proven direct-OpenRouter loopback gateway and builds the WTW ↔ WRN switching engine behind the Phase 2A fail-closed discovery boundary.

The current development laptop is a recovery/unsupported Claude installation. It must remain incapable of executing a real Claude transition.

A clean Company Portal-managed Claude installation remains the release-blocking positive-path qualification environment before live switching can be enabled.

## Gateway checkpoint

The production-shaped gateway is now built from the main repository as:

WRN-AI-Gateway-Gateway.exe

It binds only to loopback, accepts the Claude Anthropic Messages surface, authenticates Claude with a local bearer credential, and calls OpenRouter directly with the current user's DPAPI-protected OpenRouter credential.

The gateway loads the same validated signed catalogue as the launcher.

## Dynamic routing and privacy policy

The gateway does not contain a compiled model map.

For each request it:

1. accepts only a visible WRN Claude alias from the validated signed catalogue;
2. resolves that alias to the catalogue's upstream OpenRouter model ID;
3. rejects direct upstream IDs and unknown aliases;
4. overwrites caller provider policy with:
   - zdr = true
   - data_collection = deny
5. forwards Anthropic version/beta headers;
6. streams the upstream response without buffering the complete model output.

A signed catalogue update can therefore change the model service without rebuilding the gateway.

## Safe prototype reuse

The earlier prototype proved useful primitives which are retained:

- loopback-only TCP listener;
- local bearer authentication;
- CurrentUser DPAPI OpenRouter credential;
- Anthropic Messages proxying;
- streaming pass-through;
- health endpoint;
- forced OpenRouter privacy policy.

The old setup script is not production authority.

The following prototype behaviours are deliberately not carried forward:

- six-model hard-coded route map;
- broad copying/backing up of Claude config-library directories;
- immediate Claude configuration writes;
- Run-registry gateway startup;
- forced Claude process termination/restart;
- the old WRN-OpenRouter state root;
- model-specific smoke-test assumptions.

The gateway binary contains no Claude configuration/lifecycle mutation code.

## Gateway verification evidence — 25 September 2026

Fixture/policy verification passes for:

- signed catalogue loading;
- catalogue-driven alias routing;
- forced ZDR;
- forced data-collection denial;
- rejection of direct upstream IDs;
- rejection of unknown aliases;
- rejection of missing/invalid model requests;
- absence of hard-coded model IDs;
- absence of Claude lifecycle/configuration mutation strings.

All earlier Phase 1/2 verification suites continue to pass and the gateway binary is included in the client package SHA-256 manifest.

A live disposable-state smoke test also passed without touching Claude or persistent WRN credentials.

The test:

- created a temporary loopback port;
- created a temporary local bearer credential;
- protected the development OpenRouter credential with CurrentUser DPAPI under a temporary state root;
- started the new gateway;
- loaded signed catalogue release 4;
- dynamically resolved the catalogue default alias to GPT-6 Luna;
- completed a real OpenRouter inference request;
- confirmed the gateway log contained neither the API key nor prompt/response marker;
- terminated the temporary gateway and removed the disposable state.

## Switching work still to complete

The next checkpoint is a fixture-only transactional transition compiler/executor.

Before a clean managed-Claude machine is qualified, Phase 3 may compile and test the intended operations, generate the WRN-owned profile from signed catalogue data, and prove rollback on synthetic files.

It must not perform live Claude writes on the current recovery-state laptop.

The exact WTW restore contract remains provisional until the clean managed-Claude baseline is observed.
