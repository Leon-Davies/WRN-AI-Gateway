# Phase 2B — Signed dynamic catalogue runtime

## Purpose

Phase 2B makes the WRN model service remotely maintainable before live Claude mode switching is enabled.

Model names, OpenRouter IDs, recommendations, benchmark metadata, user-facing guidance, changelog entries and qualification metadata are signed catalogue data rather than permanent application logic.

The owner can therefore publish a new model catalogue without rebuilding or reinstalling WRN AI Gateway on colleague laptops.

## Trust boundary

The catalogue is signed with RSA-SHA256 over the exact UTF-8 bytes of `catalogue.json`.

Ordinary clients contain only the RSA public verification key.

The private signing key is maintained outside the repository and outside end-user installations. The current development signing key is encrypted for the maintainer's Windows account with CurrentUser DPAPI.

A client verifies the signature before parsing or activating the catalogue.

The runtime also validates:

- schema version;
- monotonically increasing release number;
- minimum compatible application version;
- unique WRN keys, Claude aliases and upstream model IDs;
- exactly one recommended/default visible model;
- mandatory `zdrRequired = true`;
- required AA metadata for visible model cards;
- HTTPS benchmark reference URLs.

## Local storage and last-known-good

The client ships with a signed bundled catalogue so the application remains usable when the network is unavailable.

Validated remote releases are stored under:

`%LOCALAPPDATA%\WRN-AI-Gateway\catalogue\releases\<release>`

The store maintains:

- `current.txt` — active validated release;
- `previous.txt` — previous validated release;
- the signed JSON/signature pair for each promoted release.

A newer release is staged, verified again from disk, then promoted. A signed rollback to an older release is rejected. Reusing an existing release number with different bytes is also rejected.

If the remote endpoint is unavailable or a candidate is invalid, the last-known-good catalogue remains active.

## Refresh behaviour

The WPF launcher checks for catalogue updates when it starts and then every 30 minutes while it remains open.

A catalogue change never rewrites a running Claude session. In later phases, changes that affect the generated WRN Claude profile/gateway mapping will be staged for the next safe WRN activation while Claude is closed.

The Models page, Home model spotlight and catalogue changelog are all rendered from the same validated catalogue.

## Development distribution backend

The current development backend is a dedicated `catalogue-beta` branch in the WRN AI Gateway GitHub repository, served over HTTPS.

This is an implementation of the pluggable distribution abstraction, not a permanent catalogue semantic. The catalogue format, signature rules and local storage do not depend on GitHub.

GitHub Raw CDN caching is bypassed with no-cache headers and a per-refresh query nonce. Signature verification remains authoritative even if transport caching or a publication race returns mismatched JSON/signature bytes.

## Remote propagation evidence — 25 September 2026

The first bundled catalogue was release 1.

A signed release 2 was published remotely with a changed GPT-6 Luna tagline and a test changelog entry.

Without rebuilding or reinstalling the application, the client:

1. downloaded release 2;
2. verified the signature and schema;
3. promoted it to the local cache;
4. retained release 1 as previous;
5. rendered the remotely changed Home/Models/Updates content.

A release 3 was then published with production-style wording.

During publication testing, two deliberately useful failure conditions were observed:

- a JSON/signature byte mismatch caused signature validation to fail and the client stayed safely on release 2;
- GitHub Raw briefly served stale release-2 content after release 3 was published, so the client treated it as already current rather than downgrading or corrupting state.

After adding cache-busting and signing the exact bytes served by the distribution endpoint, the same application binary promoted release 2 → 3. Its executable SHA-256 was unchanged before and after the catalogue update.

This proves the model service can change independently of application binaries.

## Publisher requirement exposed by testing

Phase 2C must publish catalogue JSON and signature atomically in one Git commit/object publication step.

The publisher must sign the exact bytes that will be served by the distribution backend, then verify the just-published remote bytes before declaring publication successful.

A temporary transport mismatch is safe because clients fail closed, but the maintainer workflow should avoid creating that mismatch intentionally.

## Current catalogue

The first candidate catalogue contains four visible models:

- GPT-6 Luna — recommended everyday work;
- Claude Opus 5.5 — difficult and important work;
- GPT-6 Astra — deep technical and document work;
- DeepSeek V4.1 Flash — fast, high-volume alternative.

These are catalogue data, not compiled application rules.

The current Cowork qualification state remains `pending-clean-managed-qualification` until a clean Company Portal-managed Claude laptop completes the deferred positive-path release gate.
