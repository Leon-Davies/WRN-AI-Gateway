# Agent and developer instructions

This repository may be worked on by human developers and coding agents with no prior conversational context.

Do not treat this as a generic OpenRouter launcher.

The product has strict safety and UX requirements.

## Required orientation

Read, in order:

1. README.md
2. docs/DEVELOPER_ONBOARDING.md
3. docs/PRODUCT_REQUIREMENTS.md
4. docs/ARCHITECTURE.md
5. docs/PROJECT_CONTEXT.md
6. docs/SECURITY_AND_POLICY.md
7. docs/TESTING_AND_ACCEPTANCE.md
8. docs/ROADMAP.md
9. docs/DECISIONS.md

## Non-negotiable rules

- No local admin dependency.
- Never uninstall, patch, repackage, or modify the managed Claude Desktop package.
- Never use the copied/unpackaged recovery technique as production design.
- Never weaken Cowork's service/pipe ownership or sandbox checks.
- Never manipulate Claude history stores.
- Never store plaintext OpenRouter credentials.
- Never embed an administrative OpenRouter key in clients.
- Never silently fall back to a different model.
- Never apply catalogue/configuration changes underneath a running Claude process.
- Never claim a feature is production-qualified solely because a prototype worked.

## Scope discipline

Implement the smallest coherent roadmap slice.

Before changing architecture, update or propose an ADR in docs/DECISIONS.md.

If repository evidence and this documentation disagree, stop and report the discrepancy rather than guessing.

## Evidence expectations

For mode/history work, provide file/process/config evidence.

For Cowork work, distinguish between host-side tool fallback and the normal managed VM/service path.

For model support, provide ZDR eligibility plus protocol/tool qualification evidence.

For UX error handling, provide screenshots or deterministic fault-injection evidence.

## Public repository

Assume anything committed can be public.

Do not include confidential WTW details, internal endpoints, personal identifiers, secrets, or user data.
