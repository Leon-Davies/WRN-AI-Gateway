# WRN AI Gateway

WRN AI Gateway is a user-space launcher and configuration layer for WRN colleagues who need a simple, non-technical way to choose between the organisation's standard Claude Desktop experience and a WRN-managed OpenRouter model experience.

The product is not a replacement for Claude Desktop. It is a small companion application that launches the organisation-managed Claude installation in one of two clearly separated modes:

- **WTW Claude** — the baseline, organisation-managed Claude experience.
- **WRN Claude** — the same managed Claude application, configured to route model inference through the WTW OpenRouter organisation and the WRN-published model catalogue.

The intended user experience is a dedicated WRN AI Gateway application with a friendly home screen, a time-appropriate greeting, two large launch cards, current model recommendations, a changelog, update status, and support information.

## Non-negotiable requirements

1. No local administrator rights are required.
2. The managed Claude Desktop package is never replaced, patched, uninstalled, repackaged, or modified by WRN AI Gateway.
3. Existing Claude and Cowork history must survive installation, mode switching, application updates, model catalogue updates, Windows restarts, and Claude updates.
4. WRN model choices are centrally updateable without redistributing the launcher.
5. OpenRouter Zero Data Retention requirements are enforced for WRN inference.
6. API keys are encrypted for the current Windows user and are never written to logs or the repository.
7. Raw API errors, stack traces, and gateway failures should not be the normal user experience.
8. WTW Claude remains a clear escape route whenever WRN Claude cannot launch safely.
9. Mode switching is transactional and only changes an explicitly allowlisted set of configuration.
10. The project must remain compatible with locked-down Windows laptops and standard user permissions.

## Documentation reading order

A new developer should read these files in order:

1. [Developer onboarding](docs/DEVELOPER_ONBOARDING.md)
2. [Product requirements](docs/PRODUCT_REQUIREMENTS.md)
3. [Architecture](docs/ARCHITECTURE.md)
4. [Project context and prototype evidence](docs/PROJECT_CONTEXT.md)
5. [Security, privacy and policy](docs/SECURITY_AND_POLICY.md)
6. [Model catalogue and updates](docs/MODEL_CATALOGUE_AND_UPDATES.md)
7. [User experience](docs/UX_SPECIFICATION.md)
8. [Testing and acceptance](docs/TESTING_AND_ACCEPTANCE.md)
9. [Roadmap](docs/ROADMAP.md)
10. [Architecture decisions](docs/DECISIONS.md)

## Current project state

Phase 0 architecture/documentation and the Phase 1 native Windows application shell are complete. Earlier prototype work demonstrated native Windows gateway feasibility, OpenRouter routing, DPAPI key storage, Claude Desktop third-party inference, model aliases, and Cowork file/tool workflows; that evidence is documented in PROJECT_CONTEXT.md.

The Phase 1 native Windows application shell and no-admin per-user installer are implemented and owner-accepted. The shell remains deliberately non-destructive: WTW/WRN launch actions are placeholders and do not yet write Claude configuration or credentials.

Phase 2A safe Claude discovery is implemented and merged. It is read-only with respect to Claude and fails closed on the current recovery/unsupported development state; the healthy managed-Claude positive path remains a release-blocking qualification gate before live switching. Phase 2B now provides the signed dynamic model catalogue runtime, last-known-good cache and remote propagation path. The maintainer publisher remains the next beta prerequisite before live switching.

The most important unqualified behaviour is the complete round-trip on a healthy managed Claude installation:

WTW Claude → WRN Claude → WTW Claude → WRN Claude

with history, login state, Cowork, and the managed VM service preserved throughout. This is a release-blocking qualification item.

## Security note

This repository must never contain API keys, access tokens, private signing keys, user chat data, internal credentials, private organisation identifiers, confidential WTW endpoints, or machine-specific secrets.

The repository is currently public. Documentation and examples must therefore remain safe for public disclosure unless repository visibility changes.
