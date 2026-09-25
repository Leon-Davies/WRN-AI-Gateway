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


## 13. Copy discipline

Visible copy should earn its place.

Keep text when it is actionable, materially informative, or confidence-building. Remove internal development language, placeholder status labels, lifecycle terminology, and repeated explanation.

Do not expose labels such as:

- Phase 1 preview;
- prototype healthy;
- mock connection;
- preview catalogue;
- implementation-stage commentary.

The product UI should read as a product even while implementation is incomplete.

## 14. Action surfaces and motion

Primary home actions should use the full card as the interactive target rather than small nested buttons.

Required behaviour:

- whole-card pointer target;
- visible keyboard focus state;
- restrained hover animation;
- concise action cue such as Open or View models;
- no animation that obscures or delays the action.

The personalised greeting may type in once on launch as a short product flourish. If Windows client-area animation is disabled, show the completed greeting immediately rather than animating.

## 15. External model context

The Models view may link to a reputable external model-comparison source for current benchmark and pricing context.

External links must be clearly labelled and open in the user's normal browser. They should supplement WRN's curated catalogue rather than replace it.


## 16. Rotating WRN hero

The Home greeting banner may rotate through approved WRN brand imagery.

Required behaviour:

- cross-fade rather than abrupt replacement;
- a calm interval of several seconds between images;
- greeting text remains stable and readable;
- if Windows client-area animations are disabled, change images without animation;
- branding assets must only be distributed from an approved repository/package location.

## 17. ZDR mode labelling

Both WTW Claude and WRN Claude are presented as Zero Data Retention experiences.

The WRN card should remain explicit that its ZDR statement applies to model inference through WTW OpenRouter. Do not imply that ZDR means data never leaves WTW or that unrelated tools/services inherit the same retention policy automatically.

## 18. Models is an information surface, not a model picker

The Models page explains the models currently exposed in WRN Claude/Cowork. Clicking a model in WRN AI Gateway must not select or launch that model.

Each model card should show concise, decision-useful information for non-technical users:

- model name;
- a short plain-English role/use cue;
- a current Artificial Analysis intelligence ranking/index;
- an explicitly labelled estimated short-message cost;
- a Details action.

The Details view should explain:

- what the model is;
- what work it is a good choice for;
- benchmark/cost context;
- limitations or important supersession information where relevant;
- a model-specific Artificial Analysis link.

Benchmark ranks must be described as belonging to the relevant Artificial Analysis comparison class, not as a universal league table. Cost-per-message figures are estimates only and must state their token assumption. Cowork may use more context and multiple model/tool calls.

The model reference data shown in the launcher is a dated snapshot until the signed central catalogue becomes authoritative.

## 19. Support ownership and templates

Support should state:

**Built and maintained by the Willis Research Network.**

The primary actions are:

- Request a model;
- Report a problem.

Both actions use the same clickable-card hover/focus treatment as other interactive cards.

Selecting an action opens an in-app template and displays:

**Please send this request to Leon.Davies@wtwco.com**

The problem template must remind users not to include API keys, passwords, or confidential prompt/file contents.
