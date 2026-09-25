# Model catalogue and updates

## 1. Why the catalogue is separate

The best model choices change faster than desktop software should be released.

The WRN model list must therefore be centrally managed data.

Adding a model, removing a model, changing the recommended model, or publishing a short note should not require rebuilding the WRN AI Gateway application.

## 2. Catalogue responsibilities

The signed catalogue is the source of truth for what WRN Claude should present.

It should contain:

- catalogue schema version;
- catalogue release/version;
- publication timestamp;
- minimum compatible gateway/application version;
- default/recommended model key;
- visible model entries;
- disabled/withdrawn model metadata where needed;
- changelog entries;
- optional support/announcement text.

## 3. Suggested model entry

Illustrative fields:

- key: stable WRN identifier;
- label: user-facing name;
- upstream_model: OpenRouter model ID;
- claude_alias: stable alias used by compatibility layer if needed;
- visible: boolean;
- recommended: boolean;
- status: eligible / qualified / experimental / withdrawn;
- zdr_required: true;
- streaming: supported/unsupported;
- tools: supported/unsupported;
- cowork: qualified/unqualified;
- description: short plain-English guidance;
- limitations: optional short note;
- minimum_gateway_version;
- qualified_at;
- qualification_version.

Do not encode permanent business logic around individual model names.

## 4. Catalogue signing

The publisher signs the canonical catalogue payload.

Clients:

1. download payload and signature;
2. verify against embedded public key;
3. validate schema;
4. validate compatibility;
5. persist as candidate;
6. run local sanity checks;
7. atomically promote to current;
8. retain last-known-good.

The private signing key stays with the maintainer/publishing environment.

## 5. Publishing workflow

Desired maintainer experience:

1. Open WRN Model Publisher.
2. Add/edit model.
3. Run qualification.
4. Review results.
5. Add release note.
6. Publish.

Qualification should eventually test:

- upstream model exists;
- an eligible ZDR route is available under the organisation policy;
- basic Messages-style inference succeeds;
- streaming succeeds;
- tool-call round trip succeeds;
- selected token settings are sane;
- gateway mapping succeeds;
- Claude/Cowork smoke test succeeds where automation permits.

The publisher should block publication if mandatory checks fail.

## 6. Near-push behaviour

Without MDM, clients cannot receive true forced software push while offline.

Required behaviour:

- check catalogue at WRN AI Gateway launch;
- optionally check on user login;
- periodically check on a conservative interval such as 30 minutes;
- do not interrupt a running Claude session;
- stage changes for the next safe WRN activation.

This should feel like push operationally while remaining user-space.

## 7. Removing a broken model

A model may become unavailable because:

- upstream ID disappears;
- no ZDR route exists;
- organisation policy blocks it;
- repeated compatibility failures occur;
- Cowork/tool behaviour regresses.

Publishing a catalogue that hides/withdraws the model should make it disappear at the next safe refresh.

If Claude is already using the model in an active session, do not rewrite the running profile underneath it. Apply the change at the next safe transition/restart.

## 8. Recommended model

The catalogue may identify one recommended default.

The UI should explain recommendations rather than automatically changing an active user's selected model during a session.

A new catalogue may change the next-launch default without silently replacing the model mid-session.

## 9. Model fallbacks

Same-model provider failover is acceptable when policy allows.

Cross-model fallback must not be silent.

If model A is unavailable, the UI can recommend model B, but the user should know the selected model changed.

## 10. Changelog

Catalogue changes should generate a concise user-facing changelog.

Examples:

- Added GPT family model for complex analytical work.
- Updated recommended fast model.
- Removed model because no eligible ZDR route is currently available.
- Improved tool compatibility for model X.

Do not expose raw technical model IDs in the normal changelog unless useful.

## 11. Distribution backend

The design must not couple catalogue semantics to one host.

The distribution implementation should be replaceable.

Possible development backends include Git-based hosting or simple HTTPS object hosting.

Production selection must satisfy organisational access/policy requirements.

## 12. Application self-update

Application updates are separate from model catalogue updates.

Requirements:

- signed release manifest;
- signed/verified artifact;
- per-user install location;
- staged download;
- atomic activation;
- last-known-good rollback;
- never update while the launcher/Claude transition is in a critical section;
- never make an active Claude session restart solely to obtain an update.
