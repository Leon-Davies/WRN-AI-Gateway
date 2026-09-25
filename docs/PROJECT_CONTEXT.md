# Project context and prototype evidence

This document records the work that predates the production repository. It exists so future developers understand what has already been demonstrated and do not repeat or misinterpret exploratory work.

## 1. Why this project exists

WRN users need access to a changing shortlist of strong, cost-effective models through the organisation's OpenRouter environment while retaining the familiar Claude Desktop/Cowork user experience.

The target colleagues are not expected to be developers. A manual setup involving PowerShell, JSON edits, environment variables, gateway commands, or API model IDs is therefore not acceptable as the finished product.

The laptops are locked down and ordinary users do not have local Windows administrator rights.

## 2. Direct OpenRouter prototype

A local compatibility gateway was built and used successfully with Claude Desktop third-party inference.

The prototype demonstrated:

- direct OpenRouter inference from Claude Desktop;
- local loopback gateway routing;
- model alias mapping;
- mandatory ZDR/data-collection request policy;
- streaming;
- Anthropic Messages compatibility;
- tool calls and tool-result continuation;
- Cowork responses from a non-default model.

A native Windows gateway was then implemented so colleagues would not require WSL, Python, Node, or a .NET SDK.

The native gateway successfully operated as a standard user process.

## 3. Native installer prototype

A no-admin installer prototype demonstrated:

- detection of Claude Desktop;
- hidden key entry;
- OpenRouter key validation;
- Windows CurrentUser DPAPI storage;
- installation of a per-user gateway;
- loopback health check;
- model smoke test;
- Claude profile activation;
- restart into WRN third-party inference mode.

The installed gateway bound to localhost and did not leave the plaintext OpenRouter credential in its install directory.

## 4. Cowork acceptance proof on healthy managed Claude

Before the later uninstall experiment, a real Cowork file/tool workflow was completed successfully through the WRN/OpenRouter path.

Cowork:

- read a file from an attached project/folder;
- performed a calculation;
- created a new file;
- returned the expected content.

Independent inspection confirmed the output file and gateway activity.

This is important: WRN inference through Claude/Cowork is not merely a theoretical API proof.

## 5. Model compatibility findings

Multiple model families were tested through the compatibility gateway.

The important architectural lesson is not the specific names. The shortlist will age quickly.

Observed issues included:

- some models work cleanly with the Claude Messages/tool pattern;
- some need larger token budgets because of reasoning behaviour;
- some upstream routes can be unavailable under mandatory ZDR requirements;
- model/provider availability can change independently from the launcher.

Therefore model selection belongs in a dynamic catalogue with automated qualification, not hard-coded application logic.

## 6. The fresh-install/uninstall experiment

An attempt was made to create a clean-machine simulation by manually removing the managed Claude AppX/MSIX package registration.

This was a mistake and must not be repeated in production development.

Consequences included:

- Company Portal/Intune retained a stale failed-uninstall state;
- Windows kept provisioned/staged package content;
- the user no longer had a normal package registration;
- manual re-registration required admin/SYSTEM because Claude includes a packaged Windows service;
- the managed deployment detection continued to see the provisioned package.

The experiment established a critical product rule:

**WRN AI Gateway must treat the organisation-managed Claude package as immutable.**

Do not uninstall, re-register, patch, copy-over, or otherwise manage the corporate package.

## 7. Recovery investigation and security lesson

For diagnostic purposes, the staged Anthropic-signed Claude files were copied into user space.

The copied app could be made to open and use normal host-side tooling.

It also revealed an internal path heuristic used by the app to recognise an MSIX-like environment.

However, the real Cowork VM path rejected the foreground service because the named pipe was owned by the ordinary user rather than LocalSystem/Administrators.

Claude explicitly refused to trust that pipe.

The session could still complete a file operation using host-side tools, but the normal sandboxed VM path was not restored.

This proved two things:

1. The official managed/package service boundary matters for Cowork security.
2. The production product must never attempt to bypass that boundary.

The copied recovery build is not a deployment strategy and must never be distributed.

## 8. What is already proved

Proved experimentally:

- user-space local gateway is feasible;
- native Windows gateway is feasible without admin;
- loopback Claude compatibility is feasible;
- per-user DPAPI secret storage is feasible;
- OpenRouter model aliases are feasible;
- multiple model families can be routed;
- streaming works;
- tool-call continuation works;
- Cowork file workflows can work through WRN/OpenRouter when the official managed package is healthy;
- a polished no-admin first-run setup is feasible.

## 9. What is not yet proved

Not yet production-qualified:

- clean WTW → WRN → WTW round-trip on the current healthy managed Claude package;
- repeated switching over many cycles;
- history preservation through those cycles;
- behaviour across a Claude auto-update;
- exact minimal allowlist of configuration files required for switching;
- dynamic model discovery versus generated model list;
- signed catalogue distribution backend;
- safe self-update mechanism;
- complete friendly failure UX across error cases.

These are roadmap items, not assumptions.

## 10. Production lessons

Future developers should retain these conclusions:

- Leave the corporate Claude package alone.
- Do not depend on Windows admin.
- Do not depend on developer tooling.
- Keep secrets per-user and encrypted.
- Keep history out of the switcher's ownership.
- Treat model lists as remote data, not source code.
- Treat error UX as a product requirement.
- Do not weaken Cowork security checks to make a prototype work.
- Prefer last-known-good behaviour over aggressive updates.
