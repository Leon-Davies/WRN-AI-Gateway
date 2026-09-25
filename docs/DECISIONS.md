# Architecture decisions

This file records decisions that should not be casually revisited without new evidence.

## ADR-001 — Official managed Claude is immutable

**Status:** Accepted

WRN AI Gateway will not install, uninstall, patch, repackage, or manage the organisation's Claude Desktop package.

Reason:

Exploratory uninstall/re-registration work demonstrated that Claude includes privileged packaged service state and that managed Intune/AppX detection can become inconsistent if user-space tooling manipulates it.

Production should only interact through supported user-space configuration and launch behaviour.

## ADR-002 — No local administrator dependency

**Status:** Accepted

The application must run entirely as a standard user.

Reason:

Target users do not have local admin and obtaining elevation is not a viable rollout dependency.

## ADR-003 — History is outside WRN ownership

**Status:** Accepted

WRN AI Gateway must preserve history by not owning or migrating it.

Both modes use the same normal managed Claude profile.

Reason:

Conversation/session loss is unacceptable, and creating a second history store would fragment user experience.

## ADR-004 — Dynamic model catalogue

**Status:** Accepted

The model shortlist is remotely published signed data, not hard-coded application logic.

Reason:

Model quality and availability changes too quickly for binary releases.

## ADR-005 — Catalogue and application updates are separate

**Status:** Accepted

Model/changelog changes use a lightweight signed catalogue channel.

Application code changes use a separate signed release/update channel.

Reason:

They have different cadence and risk.

## ADR-006 — Pull-on-launch/periodic update, not pretend push

**Status:** Accepted

Without MDM/admin, the system cannot force changes onto offline laptops.

Clients pull signed updates on launch and conservative periodic checks.

Reason:

This is technically accurate while still delivering near-push behaviour.

## ADR-007 — Per-user credentials encrypted with Windows user protection

**Status:** Accepted

Each colleague uses their own OpenRouter credential.

Store it using Windows CurrentUser data protection.

Do not distribute a shared master key.

## ADR-008 — ZDR is enforced, not merely displayed

**Status:** Accepted

WRN requests must require eligible Zero Data Retention routing.

Where possible, enforce at both local gateway and OpenRouter workspace/organisation policy.

No eligible ZDR route means the request/model is unavailable.

## ADR-009 — No manual per-model security committee in normal flow

**Status:** Accepted subject to organisational policy

WRN's normal publication process uses automatic privacy eligibility and technical compatibility qualification.

A maintainer chooses what is visible/recommended based on usefulness.

If organisational policy later mandates additional approval, add it without redesigning the catalogue.

## ADR-010 — No silent cross-model fallback

**Status:** Accepted

Same-model provider failover can be transparent.

Switching to a different model requires visible user choice/notification.

## ADR-011 — Friendly failure is a release requirement

**Status:** Accepted

Predictable failures should be caught before Claude launches where possible and rendered by WRN AI Gateway.

Raw upstream/API errors are not an acceptable normal-user UX.

## ADR-012 — Signed catalogue with last-known-good

**Status:** Accepted

Clients verify catalogue authenticity independently of transport and retain the previous valid release.

A broken or tampered update must not break existing users.

## ADR-013 — Do not ship recovery hacks

**Status:** Accepted

The copied Claude recovery build, user-owned WindowsApps path heuristic, and foreground Cowork service experiments were diagnostic only.

They are not production architecture.

Reason:

The real Cowork VM correctly rejected an unprivileged named-pipe owner. Production must preserve that security boundary.

## ADR-014 — Switching happens only while Claude is closed

**Status:** Accepted

WRN AI Gateway does not rewrite mode configuration beneath an active Claude process.

The launcher should ask for a normal close and transition only after process exit.

## ADR-015 — Distribution backend is pluggable

**Status:** Accepted

Do not bind catalogue semantics to GitHub, SharePoint, or a specific web host.

The production backend can be chosen later based on access and policy while clients keep the same signed-catalogue contract.


## ADR-016 — Native WPF shell on built-in .NET Framework

**Status:** Accepted

The initial WRN AI Gateway Windows application uses WPF on the .NET Framework already present on managed Windows.

Reason:

- no separate runtime installation for colleagues;
- no administrator requirement;
- native Windows accessibility and shortcut behaviour;
- small deployment footprint;
- sufficient control to deliver a polished internal application;
- avoids making Electron, Node.js, Python, WSL, or a developer SDK a user dependency.

The production code may be modernised later if a replacement preserves the same no-admin and no-runtime-install guarantees.

## ADR-017 — WRN inference goes directly to OpenRouter

**Status:** Accepted

WRN mode uses the local WRN compatibility gateway to call the OpenRouter API directly with the current user's OpenRouter credential.

The WTW Common AI endpoint is not a production dependency of WRN AI Gateway.

Where an OpenRouter workspace or organisation policy is available, it may provide an additional policy layer, but the local gateway must independently enforce the required request-level routing policy.

Reason:

The owner confirmed that the beta/service should use the OpenRouter credential path already proved by the native loopback gateway. This keeps the product independent of the separate Common AI access point and preserves the proven Claude/Cowork compatibility layer.

## ADR-018 — Remote model control is a beta prerequisite

**Status:** Accepted

The signed dynamic model catalogue and maintainer publishing path must exist before live WTW ↔ WRN switching is released to beta users.

Production mode-transition logic and gateway policy must not depend on a permanently hard-coded set of model names or upstream OpenRouter IDs.

Reason:

The owner must be able to add, withdraw, re-describe, or change the recommended model from the maintainer laptop without rebuilding or manually reinstalling every colleague's application. Model changes have a materially faster cadence than application releases.


## ADR-021 — Application updates use a separate signed trust channel

**Status:** Accepted

Launcher/gateway application releases use a dedicated signed application-release manifest and key, separate from the signed model catalogue.

Candidates are staged, self-checked and activated by an external WRN updater helper outside the version folders. The previous healthy installation is retained and restored automatically if post-activation health fails.

Reason:

Application code and model-catalogue data have different release cadence and risk. A broken application update must not invalidate the model-update contract or require administrator recovery.

## ADR-022 — Public app artifacts exclude local corporate hero assets

**Status:** Accepted

The public application-update artifact contains no local wrn-hero PNG corporate images.

An installed client may copy its existing allowlisted local hero assets into a verified staged candidate before activation.

Reason:

The repository/update transport is public, while the internal WRN hero imagery is intentionally untracked. Preserving branding client-side avoids publishing private local assets and prevents ordinary updates from stripping the internal presentation.
