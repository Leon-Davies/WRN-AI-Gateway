# Phase 4B — Friendly failure and routing hardening

## Purpose

Phase 4B turns predictable OpenRouter/gateway failures into a stable WRN-owned contract for non-technical users.

The goal is not to hide real failures. It is to make them:

- understandable;
- safe to retry when appropriate;
- free of provider/model implementation details;
- consistent between Settings and Claude;
- non-destructive.

No Claude configuration transition is retried by this layer.

## Normalized failure taxonomy

Runtime failures are classified into bounded WRN categories:

- configuration;
- authentication;
- policy / no eligible route;
- usage limit;
- rate limit;
- network;
- temporary service unavailability;
- request rejected;
- recovery required;
- unsupported state;
- unknown.

Each normalized failure carries:

- stable WRN code;
- short title;
- user-facing message;
- retryability flag;
- Anthropic-compatible HTTP/error type where relevant.

Settings now consumes the same taxonomy as the gateway instead of maintaining separate hand-written error wording.

## Retry policy

Automatic retries are deliberately narrow.

### Allowed

OpenRouter key validation is an idempotent GET, so it may retry **once** after a short delay for:

- transport failure;
- HTTP 408;
- HTTP 500;
- HTTP 502;
- HTTP 503;
- HTTP 504.

### Not automatically retried

The WRN gateway does **not** replay inference POSTs.

Reason:

A lost response does not prove the upstream request failed before execution. Replaying a model request could duplicate work, cost, or tool effects.

Instead, the request is sent once through OpenRouter. OpenRouter may transparently fail over between eligible providers for the **same model**.

## Routing ownership

The WRN gateway owns the provider-routing block.

For every inference request it:

- removes a caller-supplied cross-model `models` fallback array;
- discards caller provider pinning/order/sort overrides;
- forces `provider.zdr = true`;
- forces `provider.data_collection = "deny"`;
- allows same-model provider fallback.

This preserves transparent provider failover while preventing silent cross-model fallback or local weakening of privacy policy.

## Friendly gateway errors

Final non-success upstream responses are converted into Anthropic-compatible error objects with short WRN messages.

Examples include:

- reconnect OpenRouter;
- usage limit reached;
- no approved route is available;
- model service is busy;
- model service is temporarily unavailable;
- selected model could not accept the request.

Raw OpenRouter/provider response bodies are not forwarded or logged on these paths.

## HTTP-200 embedded error handling

Live qualification exposed an important OpenRouter/Anthropic Messages edge case:

OpenRouter may return HTTP 200 while the Anthropic response body itself is an error.

Observed during qualification:

- Luna was temporarily rate-limited upstream;
- non-stream response was a JSON Anthropic `type:error` object;
- streaming response emitted `message_start` followed by an SSE `error` event;
- the raw upstream message contained the upstream model slug and an OpenRouter help URL.

The gateway now sanitizes both forms:

- JSON `type:error` bodies;
- streamed SSE `data: { type: "error" ... }` events.

Successful JSON/SSE content is passed through unchanged.

Regression fixtures use the observed rate-limit shape and prove that:

- the raw model slug is removed;
- the OpenRouter URL is removed;
- a short rate-limit message remains;
- normal JSON is untouched;
- normal SSE events are untouched.

## Live qualification — 25 September 2026

The exact packaged gateway was run against OpenRouter using disposable DPAPI state and the external authorized development key file.

A successful qualification was completed with **DeepSeek V4.1 Flash**:

- health endpoint: PASS;
- normal Anthropic Messages request: SUCCESS;
- streaming request: SUCCESS;
- bad local bearer rejected: PASS;
- direct upstream model ID rejected: PASS;
- failure-sanitization policy checks: PASS.

The live smoke now supports an explicit catalogue `ModelKey` so gateway qualification does not depend on whichever model is currently marked default.

The temporary Luna rate-limit condition was useful fault evidence but is not interpreted as a gateway failure.

## Exit

Phase 4 is complete when:

- per-user credential onboarding is native and DPAPI protected;
- gateway routing enforces ZDR/data-collection policy;
- caller routing overrides cannot weaken policy;
- same-model provider fallback is allowed;
- cross-model fallback is forbidden;
- transient key validation has one bounded retry;
- inference is never blindly replayed;
- upstream errors are normalized and sanitized;
- deterministic and live gateway regressions pass.
