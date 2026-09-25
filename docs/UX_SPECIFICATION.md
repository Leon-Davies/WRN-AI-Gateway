# User experience specification

## 1. Experience goal

WRN AI Gateway is intended for colleagues who should not need technical knowledge.

The product should feel like a small polished internal application, not an infrastructure control panel.

## 2. Home screen

The desired hierarchy is:

WRN AI Gateway
Good morning/afternoon/evening, FirstName
Choose how you'd like to work

Two primary cards:

### WTW Claude

- Standard WTW-managed Claude
- Clear statement that this is the baseline experience
- Open WTW Claude button

### WRN Claude

- WTW OpenRouter
- current recommended model
- Zero Data Retention statement
- Open WRN Claude button

Secondary content:

- Recommended today
- What's new
- OpenRouter connection status
- Catalogue/update status
- Application version
- Support/model request contact

The UI should avoid dense technical terminology.

## 3. Greeting

Use local time to select Good morning, Good afternoon, or Good evening.

Use a locally obtained/displayed first name where reliable.

The greeting is cosmetic. Failure to resolve a first name must not block operation.

## 4. Models view

The Models view should present human-readable model cards.

Each model may show:

- name;
- short purpose;
- speed/cost positioning if maintained;
- tools/Cowork qualification;
- recommended badge;
- limitations.

Do not overwhelm users with provider IDs or routing implementation details.

## 5. Updates/changelog view

Show:

- date/version;
- model additions/removals;
- recommendation changes;
- important bug fixes;
- application updates.

The changelog should help users understand why the list changed.

## 6. Support view

Provide:

- Report a problem;
- Request a model;
- Export diagnostics;
- named/team support contact configured by deployment.

Diagnostics must be privacy-safe.

## 7. Error UX

Errors should be WRN-owned, concise, and actionable.

Example:

**WRN Claude isn't available right now**

We couldn't reach WTW OpenRouter. Your normal Claude installation is unaffected.

[Try again] [Open WTW Claude]

Avoid:

- raw JSON;
- giant red stack traces;
- HTTP response bodies;
- unexplained model IDs;
- terminal windows.

## 8. Preflight UX

Normal healthy launches should feel instant.

Technical checks should be silent unless they fail.

A short progress state is acceptable:

Checking WRN connection...
Preparing model catalogue...
Opening Claude...

Do not display a verbose checklist by default.

A Diagnostics screen may expose detailed health checks for support.

## 9. WTW escape route

Every WRN failure screen should provide a clear path to WTW Claude unless baseline WTW state itself is unsafe or unknown.

WRN failure must not make normal Claude unusable.

## 10. Mode clarity

Users must be able to tell which mode they are about to launch.

The launcher should use consistent visual treatment and text.

Do not rely only on colour for mode distinction.

## 11. Accessibility

Target:

- keyboard navigable;
- sensible focus order;
- high contrast;
- scalable text;
- no colour-only status information;
- restrained animation;
- reduced-motion support where practical;
- concise labels.

## 12. No terminal UX

Normal users should never need to:

- open PowerShell;
- paste commands;
- edit JSON;
- start a gateway manually;
- inspect logs;
- know a localhost port.

Those are developer/support concerns.
