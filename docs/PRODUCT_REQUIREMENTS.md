# Product requirements

## 1. Product objective

Create a polished per-user Windows application named **WRN AI Gateway** that gives non-technical WRN colleagues a simple choice between:

- baseline WTW Claude; and
- WRN Claude using a centrally managed OpenRouter model catalogue.

The product must work without local administrator privileges and must preserve the user's normal Claude environment and history.

## 2. Primary users

The primary user is a WRN colleague who:

- is not expected to be technical;
- has managed Claude Desktop installed through the organisation;
- has no local administrator rights;
- should not need to understand model IDs, gateways, environment variables, or API plumbing;
- needs a reliable escape route back to normal WTW Claude.

A secondary user is the WRN maintainer, who needs to publish catalogue changes, inspect health, qualify models, issue application updates, and support colleagues.

## 3. Required user journeys

### 3.1 First run

The user opens WRN AI Gateway.

The application:

1. verifies supported Windows/Claude prerequisites;
2. locates the official managed Claude installation without modifying it;
3. records the minimal baseline configuration needed for a safe return to WTW mode;
4. never reads or copies chat/session data;
5. asks the user to connect their individual OpenRouter credential if WRN mode is requested;
6. validates the credential;
7. stores the credential using Windows CurrentUser data protection;
8. downloads and verifies the current signed WRN model catalogue;
9. returns to the home screen.

A failed first-run step must leave baseline WTW Claude usable.

### 3.2 Home screen

The launcher should display:

- Good morning/afternoon/evening, FirstName;
- a large WTW Claude card;
- a large WRN Claude card;
- recommended/current WRN model information;
- connectivity/catalogue status;
- What's new / changelog;
- application version;
- support/model-request contact.

The home screen should be calm, modern, and understandable without documentation.

### 3.3 Launch WTW Claude

The application must:

1. determine whether Claude is already running;
2. avoid unsafe configuration changes while Claude is active;
3. stop or detach WRN-only gateway components;
4. restore/activate the qualified WTW baseline configuration transactionally;
5. validate that the WTW state is coherent;
6. launch the official managed Claude Desktop application.

The launcher must not remove the user's WRN credential when entering WTW mode.

### 3.4 Launch WRN Claude

The application must:

1. determine whether Claude is already running;
2. validate the signed catalogue;
3. decrypt and validate the user's OpenRouter credential;
4. start the local WRN gateway if required;
5. confirm loopback health;
6. confirm the selected/default model is eligible;
7. confirm the required ZDR/data-collection policy;
8. activate the WRN Claude configuration transactionally;
9. validate the resulting profile;
10. launch the official managed Claude Desktop application.

If any preflight fails, Claude should not be launched into a known-bad WRN configuration.

### 3.5 Model catalogue update

A maintainer publishes a new signed catalogue from an authorised maintainer workstation.

Installed clients discover it on launch or periodic check, verify the signature, cache it as last-known-good, and use it for the next safe WRN activation.

A catalogue update must not interrupt an active Claude session.

### 3.6 Application update

WRN AI Gateway can discover a newer signed application release, stage it per-user, verify it, and activate it on a later safe restart.

A failed update must roll back to the last-known-good version.

## 4. History preservation requirements

History preservation is release-blocking.

The application must not intentionally modify:

- Claude conversation history;
- Cowork session history;
- account/session cookies;
- IndexedDB;
- Local Storage;
- user-selected workspace files;
- Claude-created project/session directories except where an explicitly documented Claude configuration file is proven safe to change.

Both WTW and WRN modes must use the same normal Claude user profile.

Installation, switching, updates, Windows restart, and catalogue changes must preserve history.

## 5. Model requirements

The model list must be data-driven and centrally updateable.

Do not bake a permanent shortlist into the launcher binary.

Each catalogue entry should be able to describe:

- stable WRN model key;
- user-facing label;
- upstream/OpenRouter identifier;
- aliases required by Claude compatibility;
- provider/routing requirements;
- ZDR eligibility requirement;
- capabilities such as streaming and tools;
- Cowork compatibility status;
- visibility state;
- recommendation status;
- short user-facing description;
- optional warning/limitation;
- qualification timestamp;
- minimum gateway version.

Exact model names and identifiers belong in the catalogue, not the product requirements.

## 6. Error and resilience requirements

Normal users should not see large raw errors when the system can detect the issue before launch.

The launcher must present friendly failures for at least:

- missing/revoked/invalid credential;
- no network;
- OpenRouter unavailable;
- no eligible ZDR route;
- selected model removed;
- model blocked by policy;
- rate limit;
- provider outage;
- budget/credit error;
- corrupt or unsigned catalogue;
- catalogue host unavailable;
- gateway process failure;
- incompatible gateway version;
- unsupported Claude state.

Transient failures may use bounded retry and same-model provider failover.

Cross-model fallback must not happen silently.

## 7. No-admin requirements

All WRN-owned application state should live within per-user locations such as LocalAppData unless a stronger reason is documented.

The product must not require:

- administrator elevation;
- installation of Windows services;
- editing protected WindowsApps content;
- machine-wide registry changes;
- disabling security controls;
- modifying the managed Claude package;
- WSL sudo;
- privileged scheduled tasks.

## 8. Credential requirements

Each user should use their own OpenRouter credential.

The credential must:

- be entered through a hidden input or later OAuth-style flow;
- be validated before storage;
- be encrypted using Windows CurrentUser data protection;
- never be written to normal logs;
- never be included in diagnostics;
- never be committed to this repository.

Do not embed a shared organisation/master credential in the installer.

Do not embed administrative OpenRouter credentials in the desktop application.

## 9. Update/publishing requirements

A maintainer must be able to publish catalogue changes without rebuilding the application.

Publishing should eventually support:

- model existence validation;
- ZDR eligibility validation;
- basic inference test;
- streaming test;
- tool-call test;
- Claude/Cowork compatibility smoke test;
- signed catalogue generation;
- changelog entry generation;
- atomic publication.

Production distribution storage is deliberately abstract at this stage. It must be accessible from managed user laptops and acceptable under organisational policy.

## 10. Support and diagnostics

The application should expose:

- Test connection;
- Replace OpenRouter credential;
- Check for updates;
- View version/catalogue version;
- Export diagnostics;
- model request / bug report contact.

Diagnostics may contain:

- WRN application version;
- gateway version;
- Claude version;
- catalogue version;
- selected mode;
- model key;
- timestamps;
- health/error codes;
- redacted gateway events.

Diagnostics must not contain:

- prompts;
- responses;
- conversation history;
- user file contents;
- API keys;
- tokens;
- secrets.

## 11. Explicit non-goals for v1

The first release does not need to:

- replace Claude Desktop;
- install Claude itself;
- manage the official Cowork Windows service;
- act as MDM;
- guarantee true instant remote push to offline machines;
- solve VS Code/Claude Code integration;
- provide organisation-wide user management;
- perform manual security review of every new model;
- silently choose models on users' behalf;
- back up Claude conversation history.

## 12. Release-blocking unknowns

Before team rollout the following must be proved on a healthy managed Claude installation:

1. WTW → WRN → WTW → WRN repeated switching.
2. No chat/session history loss.
3. Cowork remains backed by the official packaged VM/service.
4. Claude login/session survives switching.
5. Claude auto-update does not invalidate the switch mechanism.
6. WRN config changes can be constrained to an explicit safe allowlist.
7. Common error cases are contained by WRN UI rather than appearing as large Claude errors.
