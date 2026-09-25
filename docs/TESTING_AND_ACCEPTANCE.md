# Testing and acceptance

## 1. Philosophy

The product is not accepted because a happy-path model request succeeds.

History preservation, mode round-tripping, failure containment, no-admin operation, and recovery behaviour are release-blocking.

## 2. Required test environments

At minimum:

- healthy organisation-managed Claude on standard-user Windows account;
- no local admin;
- WRN OpenRouter credential;
- normal corporate network;
- a controlled test folder for Cowork file operations.

Do not qualify production by using copied/unpackaged Claude recovery builds.

## 3. History preservation suite

Create distinct artefacts in baseline Claude and WRN Claude.

Test sequence:

1. Create WTW chat A.
2. Switch to WRN.
3. Create WRN chat B.
4. Create/use Cowork project/session C.
5. Switch to WTW.
6. Verify A, B, and C remain visible/usable as expected.
7. Switch to WRN.
8. Verify again.
9. Restart Windows.
10. Verify again.
11. Apply a catalogue update.
12. Verify again.
13. Apply a WRN Gateway update.
14. Verify again.

Any data loss or unexpected profile reset is a release blocker.

## 4. Mode stress test

Repeat WTW → WRN → WTW for at least 20 cycles in an automated or semi-automated qualification harness.

Assert each time:

- expected config hash/state;
- expected gateway state;
- official Claude path;
- expected profile/mode;
- login remains valid;
- history stores are not written by WRN mode controller;
- Cowork uses normal managed service in both relevant contexts.

## 5. Claude update test

Test switching before and after a managed Claude version update.

If Claude version changes:

- baseline compatibility should be revalidated;
- WRN launcher must not overwrite unknown new configuration blindly;
- safe failure should offer WTW Claude.

## 6. Cowork acceptance

A real Cowork test should:

- attach/select a controlled folder;
- read an input file;
- perform a deterministic calculation;
- create an exact output file;
- independently verify output;
- inspect logs to confirm normal managed VM/service path where expected.

Host-side fallback alone is not sufficient to claim full Cowork VM qualification.

## 7. Gateway protocol tests

Automate:

- health;
- hello/compatibility endpoints;
- non-streaming message;
- streaming SSE;
- tool call;
- tool result continuation;
- unknown model rejection;
- malformed input;
- cancellation;
- timeout;
- upstream retry;
- same-model provider failover.

## 8. Model qualification

For each catalogued model marked qualified:

- basic inference;
- streaming;
- long enough token budget for model behaviour;
- tool call;
- tool continuation;
- representative Cowork action where appropriate;
- ZDR eligibility;
- model alias mapping;
- error handling.

Qualification results should be machine-readable where possible.

## 9. Failure injection matrix

The development gateway should support deterministic fault injection.

Required scenarios and UX:

### Invalid credential

Expected: launcher blocks WRN launch, friendly reconnect message, WTW option.

### Revoked credential

Same as invalid credential.

### Network unavailable

Expected: friendly offline message, cached catalogue retained, WTW option.

### OpenRouter unavailable

Expected: bounded retry, friendly unavailable message, WTW option.

### 429/rate limit

Expected: bounded retry/respect retry guidance; no giant raw error.

### 5xx/provider outage

Expected: same-model provider failover where permitted, then friendly failure.

### Budget/credit error

Expected: friendly budget/support message.

### Model missing

Expected: model unavailable; no repeated raw 404 in Claude.

### No ZDR route

Expected: fail closed; model not used.

### Policy/guardrail block

Expected: friendly policy message.

### Corrupt catalogue

Expected: reject candidate and continue last-known-good.

### Invalid catalogue signature

Expected: reject candidate and continue last-known-good.

### Catalogue host offline

Expected: cached last-known-good; banner only if material.

### Gateway process killed

Expected: launcher detects/restarts before WRN launch; runtime behaviour documented.

### Bad application update

Expected: failed validation/health check triggers rollback.

## 10. Error-UI acceptance

For every predictable failure above, capture the visible user experience.

Release criterion:

Normal users should not see raw stack traces, terminal windows, giant API error blobs, or opaque connection exceptions for failures that WRN AI Gateway can preflight or translate.

## 11. Security acceptance

Assert:

- gateway listens only on loopback;
- plaintext OpenRouter key absent from install/cache/logs;
- diagnostics exclude secrets/chat/file contents;
- catalogue signature required;
- unknown model aliases rejected;
- ZDR requirement cannot be disabled by ordinary catalogue data;
- no admin elevation prompt;
- no system service installed by WRN Gateway;
- no writes to protected Claude package.

## 12. Update acceptance

Catalogue:

- new signed version accepted;
- stale version handled according to policy;
- invalid signature rejected;
- update staged while Claude running;
- update applied at next safe point;
- previous catalogue retained.

Application:

- signed update staged;
- update activates;
- failed update rolls back;
- credential persists;
- Claude history persists.

## 13. Release gates

Do not release to team pilot until:

- history suite passes;
- round-trip switching passes;
- managed Cowork qualification passes;
- no-admin install passes on a colleague-like baseline;
- failure matrix has friendly UX;
- ZDR enforcement is verified;
- diagnostics are redacted;
- rollback is proven.
