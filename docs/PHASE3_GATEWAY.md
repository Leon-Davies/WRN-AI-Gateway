# Phase 3 — Catalogue-driven loopback gateway

## Purpose

This is the first Phase 3 checkpoint.

It productionises the previously proved direct-OpenRouter compatibility gateway while keeping Claude mode switching disabled.

The gateway is a separate standard-user Windows process:

`Claude Desktop/Cowork → 127.0.0.1 WRN gateway → OpenRouter → ZDR-eligible provider`

The gateway does not install, uninstall, patch, launch, stop, re-register, or otherwise manage Claude.

## Runtime authority

Routing comes only from the current valid signed WRN catalogue.

For an inbound model request, the gateway:

1. requires a visible WRN Claude alias from the signed catalogue;
2. rejects direct OpenRouter model IDs and unknown aliases;
3. replaces the alias with the catalogue upstream model ID;
4. forces `provider.zdr = true`;
5. forces `provider.data_collection = "deny"`;
6. proxies the Anthropic Messages request directly to OpenRouter.

The gateway contains no fixed GPT, Claude, DeepSeek, or other model routing table.

A catalogue update therefore changes the available gateway routes without rebuilding the gateway binary.

## Local security boundary

The listener binds only to `127.0.0.1`.

Messages requests require a random local bearer credential. The OpenRouter credential is separate and is loaded from CurrentUser DPAPI-protected storage.

The local Claude-facing credential is never forwarded upstream. The OpenRouter credential is never returned to Claude.

Logs contain:

- startup/catalogue metadata;
- requested WRN alias;
- selected upstream model ID;
- upstream status;
- request latency;
- policy rejection reason.

Logs do not contain prompts, responses, OpenRouter credentials, or the local bearer credential.

## Supported surface at this checkpoint

The gateway currently supports:

- `HEAD /api/hello`;
- `GET /api/hello`;
- `GET /health`;
- `POST /v1/messages`;
- normal Anthropic Messages responses;
- SSE/streamed Anthropic Messages responses;
- tool-call and tool-result payload pass-through;
- relevant Anthropic version/beta request headers.

Request headers and bodies are bounded.

This checkpoint intentionally does not implement Claude lifecycle/configuration switching.

## Verification

The deterministic Phase 3 gateway verifier proves:

- gateway builds with the existing standard-user .NET Framework toolchain;
- signed catalogue aliases drive routing;
- hostile caller-supplied privacy policy is overwritten;
- direct upstream IDs are rejected;
- unknown aliases are rejected;
- missing/invalid model requests fail closed;
- the gateway source contains no Claude configuration/lifecycle write logic;
- model-specific routing is absent from gateway source.

## Live direct-OpenRouter smoke — 25 September 2026

The reusable live smoke test was run on the Windows work-laptop network namespace using an external OpenRouter key file.

The temporary test state used a DPAPI-protected OpenRouter credential and a random local bearer token.

Observed:

- gateway health: PASS;
- signed catalogue release: 4;
- default alias: `anthropic/claude-wrn-luna`;
- upstream provider observed in the model response: Azure;
- non-streamed Anthropic Messages response: PASS;
- streamed/SSE Anthropic Messages response: PASS;
- invalid local bearer token rejected with HTTP 401: PASS;
- direct upstream model ID rejected with HTTP 400: PASS.

The gateway log contained route/status/latency metadata only.

The live test does not alter Claude and removes its temporary gateway state on completion.

## Next Phase 3 checkpoint

Implement the three-file transactional Claude switching engine behind fixtures only.

The switching engine must:

- generate the WRN inference profile from the signed catalogue;
- preserve all unrelated desktop preferences;
- preserve unrelated config-library entries;
- change only the Phase 2A candidate allowlist;
- stage and validate writes before activation;
- maintain durable rollback/recovery state;
- restore WTW mode field-by-field rather than replacing unrelated preference state;
- remain disconnected from the Home launch buttons until the clean managed-Claude positive-path qualification is complete.

The current recovered Claude installation must not be used to approve production writes.
