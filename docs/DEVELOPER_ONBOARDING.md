# Developer onboarding

## Purpose

WRN AI Gateway exists to make advanced model access usable by non-technical WRN colleagues on locked-down corporate Windows laptops.

The product should feel like a small internal application, not a collection of scripts. A colleague should be able to open WRN AI Gateway, choose either WTW Claude or WRN Claude, and continue working without understanding OpenRouter, API keys, gateway URLs, JSON configuration, model identifiers, or PowerShell.

## The core mental model

There is one official, organisation-managed Claude Desktop installation.

WRN AI Gateway does not install another Claude. It does not patch Claude. It does not own chat history.

Its job is to manage a narrow user-space layer around the official application:

- choose the intended mode;
- perform safety/preflight checks;
- start or stop the WRN OpenRouter gateway;
- activate only the configuration required for that mode;
- launch official Claude Desktop;
- maintain a centrally published model catalogue;
- present update and support information;
- contain failures before they reach Claude where possible.

The two modes are:

### WTW Claude

Baseline organisation-managed Claude.

WRN components should be inactive. The WRN gateway should not be required for normal WTW Claude use.

### WRN Claude

The same managed Claude executable, using WRN third-party inference configuration and a local loopback gateway that routes eligible requests into the WTW OpenRouter organisation.

## Hard constraints

Assume all target laptops:

- run managed Windows;
- do not grant users local administrator rights;
- install Claude through the organisation's managed software channel;
- can run ordinary per-user executables and scripts;
- can write within the user's profile;
- may be restrictive about system-wide services, registry changes, and protected application directories.

Do not design around local admin privileges.

Do not ask users to edit configuration manually.

Do not design a solution that requires WSL, Python, Node.js, Azure CLI, a .NET SDK, or other developer tooling on colleagues' machines.

## History ownership boundary

Claude owns Claude data.

WRN AI Gateway must not migrate, copy, reset, delete, back up, or re-home conversation history, Cowork session history, IndexedDB, Local Storage, account cookies, or user workspace contents.

The correct way to preserve history is to leave it alone and use the same official Claude user profile in both modes.

Configuration writes must eventually be implemented through a strict allowlist. Anything outside that allowlist is considered user/application data and must not be modified.

## Model lifecycle mental model

Model availability changes quickly. The launcher must therefore not hard-code a permanent model list.

A model becomes visible to WRN users because a signed central catalogue says it is visible.

The catalogue can change independently from the application binary.

The intended model lifecycle is:

discovered → technically eligible → compatibility tested → WRN visible/recommended

There is no separate manual per-model security review in the WRN application process unless organisational policy later requires one. Privacy eligibility should be enforced automatically through OpenRouter ZDR-compatible routing and organisation/workspace controls.

## Updates mental model

There are two update channels:

1. **Catalogue updates** — frequent and lightweight. These add/remove models, change recommendations, and publish changelog entries.
2. **Application updates** — less frequent. These change WRN AI Gateway or its local gateway.

Without MDM, this is not true remote push. Clients pull signed updates on launch and on a conservative periodic schedule. Operationally, this should feel close to push.

Updates must never interrupt an active Claude session.

## Failure philosophy

WRN AI Gateway should fail closed and fail politely.

If WRN preflight fails, do not launch Claude into a known-broken WRN state. Show a concise WRN-owned message and offer retry or Open WTW Claude.

Never expose raw upstream HTTP bodies, stack traces, or opaque connection exceptions to normal users when the launcher can detect the problem first.

Provider failover for the same model is acceptable. Silent cross-model fallback is not: if a user selects one model, the product must not secretly substitute another model.

## Before implementing anything

Read PRODUCT_REQUIREMENTS.md and ARCHITECTURE.md.

Then read PROJECT_CONTEXT.md. It records what the prototype has already proved and, equally importantly, what the prototype taught us not to do.

Finally read TESTING_AND_ACCEPTANCE.md. Many behaviours in this project are release-blocking rather than best-effort.
