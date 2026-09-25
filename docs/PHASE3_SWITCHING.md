# Phase 3 — Transactional Claude switching engine

## Safety status

Live Claude configuration writes remain hard-disabled in code:

`TransitionSafety.LiveClaudeWritesEnabled = false`

The user-facing WTW/WRN launch buttons are not connected to the transition executor.

The current recovery-state development laptop is used only to prove that the live-path guard refuses to mutate its actual Claude configuration files.

## Current checkpoint — fixture WTW → WRN activation

The first fixture-qualified transition compiler uses:

- a synthetic healthy managed-Claude discovery snapshot;
- fixture LocalAppData/RoamingAppData roots;
- a real valid signed WRN catalogue;
- a synthetic random local gateway bearer token;
- a synthetic loopback gateway port.

It compiles exactly three mutations:

1. write the WRN-owned inference profile;
2. update config-library metadata so it selects that profile;
3. set desktop `deploymentMode = "3p"` last.

Writing the activation trigger last reduces the chance of Claude observing an incomplete third-party profile if a transition is interrupted.

## Generated WRN profile

The WRN profile is generated from signed catalogue data.

It contains:

- loopback gateway URL;
- local gateway bearer token;
- bearer authentication scheme;
- `modelDiscoveryEnabled = false`;
- visible catalogue aliases and labels;
- `inferenceProvider = "gateway"`;
- static local inference credential mode;
- chat tab enabled.

It does not contain upstream OpenRouter model IDs.

The catalogue default/recommended model is explicitly placed first in the Claude model list rather than relying on incidental catalogue ordering.

## Preservation rules

The compiler parses and modifies the existing fixture desktop/meta objects rather than replacing them with templates.

Fixture tests prove preservation of:

- unrelated desktop configuration fields;
- unrelated desktop preferences;
- unrelated config-library metadata fields;
- pre-existing non-WRN config-library entries.

The compiler refuses activation if a WRN profile/profile-ID collision already exists at the WTW baseline.

## Transaction execution

Before any mutation, the executor verifies:

- the plan contains exactly the allowlisted three paths;
- desired content hashes are valid;
- source-file existence still matches compilation time;
- source-file hashes still match compilation time.

All desired files are staged before the first target write.

Existing target bytes are backed up before mutation.

Each applied write is verified by SHA-256.

If a normal exception is injected after one or more writes, the executor restores the exact original fixture bytes in reverse order and verifies the rollback.

## Fixture qualification — 25 September 2026

Passing cases include:

- signed catalogue accepted;
- exactly three allowlisted mutations;
- profile → meta → deployment-trigger ordering;
- catalogue default alias appears first;
- generated profile contains no upstream model IDs;
- successful synthetic WTW → WRN transition;
- unrelated preferences/metadata preserved;
- correct loopback URL/local bearer profile fields;
- exact visible model count;
- injected failure rollback;
- exact desktop/meta byte restoration;
- newly-created profile removal on rollback;
- stale source hash rejection before writes;
- recovery diagnostic source rejected;
- running-Claude source rejected;
- pre-existing WRN profile collision rejected;
- actual Claude config paths hard-disabled and byte-for-byte unchanged.

## Not yet claimed

This checkpoint does not yet claim:

- durable recovery after process/power loss mid-transaction;
- field-preserving WRN → WTW restoration;
- live mode switching on a real managed Claude installation;
- login/history/Cowork preservation on a current managed build.

Those remain required before the launch buttons can be enabled.

## Next checkpoint

Add a minimal persisted activation baseline and a fixture WRN → WTW compiler that:

- deactivates third-party mode first;
- restores only WRN-owned/controlled fields;
- removes only the WRN config-library entry;
- removes the WRN-owned profile;
- preserves preferences and metadata changed while WRN mode was active;
- supports transactional rollback;
- remains hard-disabled for actual Claude paths.
