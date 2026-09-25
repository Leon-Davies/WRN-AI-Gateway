# Phase 5 — Signed application updates

## Purpose

Phase 5 gives WRN AI Gateway a separate, signed application-update channel for launcher/gateway code changes.

It is intentionally independent of the signed model catalogue: model catalogue changes can continue without rebuilding the app, while application releases can update launcher/gateway code without changing the catalogue publication contract.

The update path remains standard-user only and does not manage the corporate Claude package.

## Client flow

The colleague-facing What's new page now exposes one compact application-update card.

Its normal state progression is: Check for updates → Download update → Install update.

The launcher also performs a quiet check when an installed build opens. The UI only shows teammate-facing states such as You're up to date, Version X is available, Update ready to install, Couldn't check for updates, or Close Claude before installing the update.

Release IDs, hashes, raw URLs, signing details and updater internals are not surfaced to normal users.

Development builds are explicitly not treated as installed builds and cannot activate the LocalAppData WRN-AI-Gateway current installation.

## Trust and verification

Application updates use a dedicated RSA-SHA256 application-release signing key. The private key is maintainer-only and protected with Windows CurrentUser DPAPI; the public key is embedded in the client.

The client validates the signature before accepting manifest semantics. It then verifies the supported schema, monotonic release number, HTTPS artifact URL, artifact byte size, artifact SHA-256, packaged release identity, required binaries/UI/catalogue files, and bundled signed model catalogue. Signed downgrades are rejected.

## Safe staging

Downloaded updates are extracted only under the WRN install root's staged-update directory. The extractor enforces bounded artifact size, bounded archive entry count, bounded total expanded size, path-traversal rejection, and create-new semantics while extracting.

A candidate is self-checked before it is eligible for activation. Durable WRN state remains outside version folders, including credentials, catalogue cache and transition state.

## Activation and rollback

Activation is performed by WRN-AI-Gateway-Updater.exe, copied first into the persistent WRN-owned updater directory. The helper therefore executes outside both current and previous while those folders are rotated.

Activation only proceeds after the launcher has closed and defers if Claude is running, the WRN gateway is still running, Claude transition recovery is pending, or another updater/transition lock is held.

The sequence is current → previous, then staged candidate → current. The new current then runs a headless self-check.

If that post-activation check fails, the helper restores the old current. A failed candidate is retained outside current for diagnosis. If a late safety condition prevents activation after the launcher closes, the known-good current launcher is reopened.

## Corporate branding boundary

The repository and application-update distribution branch are public, while local WRN hero images are not repository assets.

Remote application artifacts therefore intentionally contain zero wrn-hero*.png files.

During staging, the client preserves only the existing installed machine's allowlisted local hero assets: top-level assets/wrn-hero*.png, maximum 20 files, maximum 10 MB per file, and maximum 50 MB total.

This means a public signed update cannot publish local corporate images, while an already branded internal installation keeps its WRN presentation after an update.

## Maintainer publication

scripts/Publish-WRNAppUpdate.ps1 is maintainer-only. It uses the same bounded publication pattern as the model catalogue: fetch clean app-update-beta authority; determine the next release; build/package; self-check; create a public-safe payload; remove local hero images; ZIP; create and sign the manifest; verify client-equivalently; preview by default; on explicit Publish commit manifest, signature and artifact atomically; push; re-fetch public bytes; require exact hashes and valid signature; then write local publication audit metadata.

The publication branch is transport only. Signature verification remains authoritative.

## Qualification — 25 September 2026

### Deterministic updater suite

PASS: signed manifest accepted; tampered manifest rejected; artifact size/hash verification; tampered artifact rejected; signed downgrade rejected; ZIP traversal rejected; expanded archive bounded; candidate identity must match signed manifest; healthy staged update activates; old current becomes previous; failed post-activation health rolls back; pending Claude recovery defers activation; updater helper executes outside current/previous; development builds cannot trigger installed-app activation; late Claude/gateway races defer safely; package contains no credential state; Updates UI is wired to check/download/install.

### Publisher preview

Real DPAPI app-release signing key: client trust-root match PASS; preview signing PASS; client-equivalent manifest verification PASS; artifact hash/size match PASS; public artifact local WRN hero assets 0.

### First remote publication

app-update-beta contains signed application release 1 / 0.5.0-beta.1.

The published object set is updates/release.json, updates/release.sig and updates/artifacts/release-00000001.zip.

### Real remote propagation

A disposable installed-app fixture downloaded the real public release 1 and proved: remote signature PASS; remote artifact size/hash PASS; public artifact contains no local WRN hero images PASS; remote release stages PASS; local WRN branding restored during staging PASS; activation PASS; release 1 becomes current PASS; old install becomes previous PASS; local WRN branding survives activation PASS; durable credential/catalogue/transition state survives activation PASS.

The real development laptop installation and Claude configuration were not modified by this propagation test.

## Exit

Phase 5 is implementation-complete when application releases are signed independently of model catalogues; a maintainer can preview/publish from their laptop; exact public bytes are verified after publication; clients can check/download/stage/install without admin; current/previous rollback is proven; local durable state survives; local private branding survives without being included in public artifacts; and a real published release propagates successfully into a disposable installed fixture.
