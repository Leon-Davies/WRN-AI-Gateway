# Security, privacy and policy

## 1. Security objective

WRN AI Gateway should reduce operational risk compared with manual configuration.

It should centralise model policy, protect per-user credentials, constrain configuration writes, preserve the organisation-managed Claude package, and fail safely.

## 2. OpenRouter Zero Data Retention

The project assumes the organisation has an enterprise OpenRouter arrangement with Zero Data Retention requirements for model inference.

WRN mode must enforce that requirement technically.

At minimum:

- requests must require ZDR-compatible routing;
- data-collection/training routes must be denied where the OpenRouter interface supports that control;
- a model without an eligible route must become unavailable rather than bypassing the policy;
- workspace/organisation guardrails should also enforce the same policy when available.

The product must not claim that data remains inside WTW. ZDR is a retention property of the inference route, not a statement that processing never leaves the organisation.

Suggested user wording:

**WTW OpenRouter — Zero Data Retention enforced for model inference.**

## 3. Model governance

Model availability on OpenRouter is not described in the UI as security approval.

The WRN process should instead use clear statuses:

- technically eligible;
- compatibility tested;
- visible;
- recommended.

A separate manual per-model WRN security review is not part of the default workflow unless organisational policy later requires one.

The application should automate what can be automated:

- eligible ZDR route;
- successful inference;
- streaming;
- tool calls;
- compatibility with Claude/Cowork usage;
- gateway version compatibility.

The maintainer decides whether an eligible/qualified model is useful enough to expose or recommend.

## 4. Credentials

End-user OpenRouter credentials are user-specific.

Requirements:

- hidden entry;
- validate before accepting;
- encrypt using Windows CurrentUser data protection;
- no plaintext persistence;
- no logs;
- no diagnostics;
- no repository storage.

Administrative or organisation-management OpenRouter credentials must never be shipped in the desktop application.

If a future publishing service needs privileged credentials, keep them server-side or in a maintainer-only secured environment.

## 5. Local gateway security

The gateway should:

- bind only to loopback;
- use a narrowly scoped API surface;
- reject unknown/disabled model identifiers;
- validate configuration/catalogue versions;
- redact secrets from logs;
- use TLS for upstream OpenRouter traffic;
- apply ZDR/data-collection routing requirements;
- avoid exposing a general unauthenticated proxy to the LAN.

## 6. Claude package boundary

Production code must not:

- uninstall Claude;
- re-register Claude;
- modify protected package files;
- fake packaged identity;
- run copied Claude binaries as the normal user product;
- weaken named-pipe owner/signature checks;
- replace or install Cowork's privileged service.

The managed Claude installation is an external dependency, not a component owned by this repository.

## 7. History/data protection

WRN AI Gateway must not collect or copy user conversation data.

Diagnostics must not contain:

- prompts;
- model responses;
- transcript bodies;
- workspace files;
- filenames when avoidable;
- API keys;
- cookies/tokens.

History preservation should be achieved by leaving Claude data stores untouched.

## 8. Logging

Logs should use structured event codes and redact sensitive values.

Recommended log content:

- timestamp;
- app/gateway/catalogue version;
- mode;
- health state;
- stable error category;
- model key, not secret;
- upstream HTTP status when useful;
- retry/failover decision.

Avoid logging complete upstream request/response bodies.

## 9. Public repository constraint

At the time this specification was written, the repository is public.

Do not commit:

- WTW-private URLs;
- internal tenant identifiers;
- user IDs;
- private model policy documents;
- private support channels;
- corporate secrets;
- machine names;
- personal data.

If the repository becomes private, secret-management rules still apply.

## 10. Terms and supported behaviour

Production should use supported configuration and extension points where possible.

Undocumented behaviour discovered during reverse-engineering may be useful for diagnosis, but should not automatically become a production dependency.

If a feature can only work by:

- patching Claude;
- spoofing package identity;
- bypassing a security check;
- weakening sandbox ownership;
- altering protected installation state;

it is out of scope for release.

## 11. Distribution and signing

Catalogue and update artifacts must be verifiable independently from the transport that hosts them.

Use detached signatures or equivalent public-key verification.

A checksum stored beside an artifact is useful for corruption detection but is not sufficient as an authenticity mechanism if the same location can be modified by an attacker.

Clients should contain only the public verification key.

## 12. Secure failure

If policy cannot be confirmed, fail closed.

Examples:

- no ZDR route → do not send request;
- invalid signature → reject update;
- unknown model → reject;
- incompatible catalogue → use last-known-good;
- OpenRouter key cannot be decrypted → ask user to reconnect, do not silently create another credential path.
