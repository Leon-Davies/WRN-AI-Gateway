# Phase 4B — Friendly failures and routing hardening

## Purpose

Phase 4B completes the hardened-gateway portion of colleague onboarding.

The goals are:

- one normalized failure vocabulary across Settings and the loopback gateway;
- no raw OpenRouter/provider errors presented to colleagues;
- bounded retry only where replay is safe;
- same-model provider failover without silent cross-model fallback;
- request-level privacy policy that cannot be weakened by a caller.

Live Claude configuration switching remains separately blocked by the clean managed-Claude qualification gate.

## Normalized failure taxonomy

`RuntimeFailureCatalog` maps technical conditions into stable product failures.

Current categories include:

- configuration;
- authentication;
- policy / no eligible route;
- usage limit;
- rate limit;
- network;
- service unavailable;
- request rejected;
- recovery/unsupported state;
- unknown failure.

Each normalized failure carries:

- stable internal code;
- short title;
- teammate-facing message;
- retryability;
- HTTP status for the local compatibility surface;
- Anthropic-compatible error type.

Normal UI paths consume the friendly message rather than maintaining their own raw HTTP/.NET error strings.

## Retry policy

Automatic retries are deliberately narrow.

### Retried once

OpenRouter key validation is an idempotent GET and may be retried once after 250 ms for:

- network failure;
- HTTP 408;
- HTTP 500;
- HTTP 502;
- HTTP 503;
- HTTP 504.

Authentication, usage, policy, and rate-limit failures are not automatically retried.

### Not automatically replayed

Inference POST requests are never replayed by WRN AI Gateway.

A lost inference response could arrive after an upstream provider already processed/billed the request, so blindly retrying it could duplicate work.

Instead, same-model provider failover is delegated to OpenRouter's provider-routing layer.

## Same-model failover / no cross-model fallback

The WRN gateway owns the outbound provider-routing policy.

For every inference request it:

- rewrites the visible Claude alias to the signed-catalogue upstream model;
- removes any caller-supplied `models` fallback array;
- discards any caller-supplied provider routing overrides;
- forces `provider.zdr = true`;
- forces `provider.data_collection = "deny"`;
- sets `provider.allow_fallbacks = true`.

This means provider failover may occur only among eligible providers serving the **same selected model**.

The gateway never silently changes to a different model.

## Friendly upstream failure mapping

Non-success HTTP statuses are normalized before returning to Claude.

Examples:

- 401/403 → OpenRouter connection needs attention;
- 402 → WRN Claude usage limit reached;
- 404 → no approved route is currently available for this model;
- 429 → model service busy;
- 5xx → model service temporarily unavailable;
- invalid request → selected model could not accept the request.

Raw upstream response bodies are not logged or returned for these cases.

## HTTP 200 embedded errors

Live qualification exposed an important Anthropic Messages behavior: OpenRouter can return an upstream model error inside an HTTP 200 response.

Two forms were observed/supported:

1. JSON error object;
2. streamed SSE `error` event after `message_start`.

The gateway therefore inspects successful transport responses for embedded Anthropic errors.

For normal JSON:

- the body is buffered;
- an embedded `type: "error"` is normalized;
- only the friendly Anthropic-compatible error is returned.

For SSE:

- normal event lines are streamed through;
- an `error` event is parsed and replaced in-line with the friendly normalized error;
- model slugs, provider instructions, URLs, and other raw upstream detail are removed.

Successful message/content events remain unchanged.

## Deterministic qualification

Phase 4B fixtures prove:

- credential auth/network/service errors normalize correctly;
- only safe transient key-validation states are retryable;
- inference transport failure is not auto-retried;
- HTTP 404 no-route state maps to a policy failure;
- rate limit maps to Anthropic `rate_limit_error`;
- gateway startup failures normalize consistently;
- caller cross-model fallback is removed;
- caller provider allowlists/order/sort are removed;
- caller cannot disable provider fallback;
- caller cannot weaken ZDR/data-collection controls;
- raw embedded JSON rate-limit details are removed;
- raw streamed SSE rate-limit details are removed;
- normal JSON and SSE responses are not altered.

## Live OpenRouter qualification — 25 September 2026

The reusable live gateway smoke was rerun after Phase 4B hardening.

Observed:

- loopback health: PASS;
- isolated test catalogue release: 1;
- selected signed alias: `anthropic/claude-wrn-luna`;
- non-stream request: upstream rate limited;
- non-stream result: friendly mapped HTTP 429, no raw OpenRouter/model detail;
- streamed request: SUCCESS;
- friendly-failure sanitization: PASS;
- invalid local bearer: rejected;
- direct upstream model ID: rejected.

A preceding diagnostic run also observed the inverse combination:

- non-stream success through Azure;
- streamed upstream rate-limit event;
- streamed event sanitized to: `The model service is busy. Wait a moment and try again.`

That real-world failure originally contained the upstream model slug and an OpenRouter settings URL. Those details no longer cross the local gateway boundary.

## Phase 4 exit

Phase 4 implementation is complete:

- native connect/replace/test/remove onboarding;
- CurrentUser DPAPI storage;
- gateway credential envelope;
- normalized failure taxonomy;
- bounded safe retry;
- same-model provider failover;
- no cross-model fallback;
- ZDR/data-collection enforcement;
- friendly JSON/SSE error mapping.

The product is still not permitted to perform live Claude mode transitions until Phase 3 is qualified on a clean current Company Portal-managed Claude installation.
