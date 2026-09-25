# Phase 6 — Failure containment qualification

## Status

Mechanical failure containment is qualified.

The remaining Phase 6 exit work is:

- owner screenshot / UX review of the final colleague-facing surfaces;
- managed-Claude switching UX qualification after the clean Company Portal lab establishes the supported managed baseline.

Live Claude writes remain disabled until that managed qualification is complete.

## Qualification contract

Expected failures must:

1. fail closed;
2. preserve the current working installation or configuration;
3. show a short non-technical next action;
4. never expose raw upstream bodies, stack traces, HRESULTs, JSON payloads, credentials, or internal status tokens;
5. remain diagnosable through WRN-owned, privacy-safe metadata.

## Failure matrix

| Failure family | Injected / deterministic proof | Colleague-facing containment |
| --- | --- | --- |
| OpenRouter not connected / rejected credential | Runtime failure matrix + credential fixtures | Connect or reconnect OpenRouter in Settings |
| OpenRouter network / transient service failure | Runtime failure matrix | Check connection or try again |
| Usage / rate limit | Runtime failure matrix | Clear usage-limit or wait-and-retry message |
| No approved ZDR route / unavailable model | Runtime failure matrix + gateway policy tests | Model temporarily unavailable; choose another WRN model or retry later |
| Rejected model request | Runtime failure matrix + gateway sanitizer | Generic request-rejected message; no upstream body |
| Gateway missing / unhealthy / start failure | Gateway lifecycle + runtime failure matrix | Repair, close/retry, or contact WRN AI support |
| Invalid / tampered catalogue | Signed catalogue tamper tests | Candidate rejected; existing signed catalogue remains authoritative |
| Catalogue rollback / release reuse | Catalogue runtime tests | Candidate rejected; current signed release retained |
| Recovery / unsupported Claude installation | Mode coordinator fixtures | Mode planning blocked; no Claude writes |
| Pending interrupted transition | Coordinator + transition recovery fixtures | New plan blocked until rollback-first recovery completes |
| Application update network failure | App-update runtime + friendly-message matrix | Try again; current app untouched |
| Invalid app signature / hash / manifest / identity / archive | App-update fixture suite | Update rejected; current version kept |
| App downgrade / rollback attempt | App-update fixture suite | Update rejected; current version kept |
| Post-activation app health failure | Updater activation fixtures | Automatic rollback restores last-known-good |
| Claude or gateway running during update | App-update activation fixtures | Update deferred with a direct close-and-retry action |
| Pending Claude recovery during update | App-update activation fixtures | Update deferred until recovery completes |
| Support diagnostics collection failure | Diagnostics UI path | Friendly retry / support message; no raw exception |

## No-raw-error acceptance

`FailureContainmentMatrixTests` enumerates the credential, gateway, upstream and app-update failure families.

For every colleague-visible failure title/message it asserts:

- non-empty friendly text;
- no internal all-caps underscore status tokens;
- no exception / HRESULT wording;
- no raw JSON framing;
- no internal API-path fragments.

The gateway sanitizer separately proves that HTTP error bodies and SSE/HTTP-200 embedded error payloads are converted to Anthropic-compatible safe errors rather than relayed verbatim.

## Retry boundary

Retries remain deliberately narrow.

Credential validation may retry once for safe idempotent transient failures.

Inference transport is not automatically replayed after a request may have reached the model.

OpenRouter may perform same-model provider fallback under the enforced ZDR/data-collection policy. The gateway never silently changes to a different model.

## Diagnostics bundle

Support now exposes **Collect diagnostics**.

The ZIP is written under:

`Documents\WRN AI Gateway\Diagnostics`

It contains only allowlisted WRN-owned support data:

- `diagnostics.json` with app/version, Claude discovery classification, catalogue release/source, credential readiness booleans, gateway state, transition-recovery presence and update state;
- a bounded, sanitized tail of the WRN gateway log when present;
- the last WRN application-update activation audit when present.

It explicitly does not collect:

- OpenRouter credentials;
- local gateway bearer values;
- prompt or response text;
- Claude chats/history;
- cookies or browser/session stores;
- Claude databases;
- user file contents.

The deterministic fixture plants fake credentials, prompt text and Claude-history sentinels in the state tree and proves none appear in the produced ZIP.

## Sagansen clean managed-device lab

A dedicated Windows lab now exists independently of the damaged development laptop.

Baseline:

- Windows 11 Enterprise Evaluation 25H2, build 26200.6584;
- EFI;
- Secure Boot enabled;
- TPM 2.0 present, initialized and ready for storage;
- VirtualBox Guest Additions 7.2.2;
- Microsoft connectivity verified;
- no WTW registration, Claude or WRN state at the clean baseline.

Offline rollback snapshots:

- `00-clean-enterprise-secureboot-ga` — clean Enterprise baseline;
- `01-company-portal-pre-enrollment` — Company Portal installed, before WTW sign-in/enrollment.

Company Portal:

- Microsoft Company Portal 11.2.2037.0;
- installed from the Microsoft Store listing;
- the VM is currently ready for the owner to perform WTW sign-in / MFA.

Important VM limitations:

- the virtual TPM reports that it is not capable of attestation, so a WTW policy requiring hardware-backed enrollment attestation may reject the VM;
- VirtualBox is currently running through the Windows Hyper-V backend and does not expose nested hardware virtualization, so this VM is useful for Company Portal / managed-package / configuration qualification but is not yet authoritative for Claude Cowork;
- live VirtualBox snapshots are not used on this host after a Hyper-V-backed live-snapshot deadlock was observed and safely recovered; offline snapshots are the lab standard.

If WTW enrollment succeeds, the next checkpoint is a managed Claude installation from Company Portal, followed by read-only WRN discovery before any switching is enabled.

If WTW enrollment is policy-blocked, the VM still establishes the clean Windows/package baseline and the programme falls back to either:

- a native clean Windows environment with the same enterprise Claude package for development comparison; and
- one untouched physical WTW-managed colleague laptop as the final release authority.

## Verification

Primary command:

`scripts\verify-phase6-failure-containment.ps1`

It composes the already-qualified lower-level fault injectors and adds the Phase 6 matrix:

- signed catalogue integrity / rollback;
- mode coordinator recovery;
- runtime friendly failures;
- application updater failure / rollback;
- diagnostics privacy;
- cross-system no-raw-error matrix;
- live-Claude-write release gate.

Expected terminal marker:

`PHASE6_FAILURE_CONTAINMENT_VERIFY_PASS`
