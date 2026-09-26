# Phase 2C — Maintainer publisher

## Purpose

Phase 2C turns signed catalogue publication into a bounded maintainer workflow.

The publisher is maintainer-only. Colleague installations do not receive the private catalogue signing key and do not need Git, WSL, PowerShell, or repository access.

The current beta publisher is a CLI workflow running on the maintainer laptop. A graphical maintainer surface may be added later without changing the catalogue/signature protocol.

## Workflow

1. Prepare a draft from the current remote catalogue.
2. Edit model metadata in the draft.
3. If a model is new, its upstream ID changes, or a hidden model is enabled, create a fresh OpenRouter qualification report.
4. Run publisher preview.
5. Review release number, visible models and catalogue SHA-256.
6. Re-run with explicit Publish.
7. Publisher signs and verifies the exact candidate bytes.
8. Publisher writes catalogue JSON + signature in one Git commit and pushes the catalogue branch.
9. Publisher fetches the public distribution endpoint and verifies the exact SHA-256, signature and release number.
10. Only then does it report publication success.

## Commands

Prepare a draft:

powershell -ExecutionPolicy Bypass -File scripts\New-WRNCatalogueDraft.ps1

Preview a draft:

powershell -ExecutionPolicy Bypass -File scripts\Publish-WRNCatalogue.ps1 -DraftPath <path>

Publish after review:

powershell -ExecutionPolicy Bypass -File scripts\Publish-WRNCatalogue.ps1 -DraftPath <path> -Publish

When model content changes, ChangeTitle and ChangeBody are mandatory so the user-visible changelog stays truthful.

## Model qualification

Test-WRNOpenRouterModel.ps1 performs the current direct-OpenRouter qualification gate.

It checks:

- model exists in OpenRouter;
- at least one ZDR-eligible route exists;
- request policy forces ZDR and denies data collection;
- normal Anthropic Messages inference succeeds;
- streaming succeeds;
- tool invocation succeeds;
- tool-result continuation succeeds.

The resulting report contains no API key.

Gateway mapping and live Claude/OpenRouter inference are qualified in Phase 6. Physical managed-Cowork qualification remains pending.

## Security boundary

The private RSA signing key remains outside the repository under the maintainer Windows profile and is protected with CurrentUser DPAPI.

Before signing, the publisher derives the public key from that private key and checks that it exactly matches the public verification key compiled into WRN clients.

Publishing defaults to preview. A remote mutation occurs only when the maintainer supplies the explicit Publish switch.

The distribution repository workspace is isolated from the development worktree.

For the current maintainer beta, Git publication is performed through the maintainer's already-authenticated WSL Git environment. This is a maintainer dependency only and does not apply to team installations.

## Qualification evidence — 25 September 2026

GPT-6 Luna was qualified through the direct OpenRouter API using an external key file.

Passing checks:

- model discovery;
- ZDR route discovery;
- normal inference;
- streaming SSE reconstruction;
- forced tool invocation;
- tool-result continuation.

The qualifier initially exposed two harness issues: fragmented SSE text and insufficient output ceilings for reasoning models. Both were corrected before the passing report was accepted.

The publisher also proved that an upstream model-ID change without a fresh qualification report is rejected before signing/publication.

## First atomic publication proof

Remote catalogue release 3 was used as the starting authority.

The publisher generated release 4, validated it with the same catalogue runtime used by clients, signed the exact bytes, verified the signature locally, and created one Git commit containing both catalogue.json and catalogue.sig.

Published commit:

424be2886099a0f01c2696994523975de9d2496f

The publisher then fetched the public raw files and verified:

- release = 4;
- remote SHA-256 exactly matched the local signed candidate;
- RSA signature verified;
- no manual GitHub edit was required.

An ordinary WRN client then independently refreshed:

current = 4
previous = 3
status = CATALOGUE_PROMOTED

No application rebuild or reinstall occurred.

## Maintainer audit

Each successful/attempted publish appends local audit metadata under:

%LOCALAPPDATA%\WRN-AI-Gateway-Maintainer\publication-audit.jsonl

The audit includes release, commit, catalogue SHA-256, remote-verification status, visible model names and any models requiring fresh qualification. It contains no private signing key or OpenRouter key.

## Provider-neutral route qualification — 26 September 2026

Claude Desktop 2.9939.2 rejected `claude-wrn-deepseek` from `inferenceModels` with a configuration warning and removed DeepSeek from the picker.

An owner-only qualification changed only the internal route identity:

```text
DeepSeek V4.1 Flash
claude-wrn-deepseek -> claude-wrn-m004
deepseek/deepseek-v4.1-flash unchanged
```

Observed result:

- no Claude configuration-warning banner;
- DeepSeek V4.1 Flash remained truthfully labelled in the model picker;
- DeepSeek selection succeeded;
- Claude UI inference returned successfully;
- gateway mapped `claude-wrn-m004` to `deepseek/deepseek-v4.1-flash`;
- upstream HTTP status = 200;
- prompt/response/API credentials were absent from gateway logs;
- WTW restoration returned to the original clean baseline.

Signed catalogue release 6 published this single model-field migration.

Published commit:

`5d3368263be028ec181f5ba26b0546f0f290f818`

An ordinary current client promoted 5 -> 6 without reinstalling the application.

## Compatibility-version qualification — 26 September 2026

Development qualification exposed that older pre-migration clients and current clients were both advertising catalogue compatibility `0.2.0`.

The Phase-6 compatibility version is now `0.6.0`.

Signed release 7 changed only:

```text
minimumAppVersion: 0.2.0 -> 0.6.0
```

The model array was identical to release 6.

The first publication attempt pushed the Git commit but the raw distribution endpoint did not verify within the publisher's bounded retry window, so the publisher correctly refused to announce success. Post-propagation verification then confirmed:

- release = 7;
- minimumAppVersion = 0.6.0;
- remote SHA-256 matched the signed candidate;
- RSA signature verified.

Observed client behaviour:

- old client `0.2.0`: `CATALOGUE_APP_TOO_OLD`, last-known-good retained;
- current client `0.6.0`: release 7 accepted/promoted.

This is the intended contract for future catalogue changes that require a newer WRN application.

