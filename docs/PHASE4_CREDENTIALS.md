# Phase 4 — OpenRouter credential onboarding

## Purpose

Phase 4 removes technical credential setup from colleague onboarding.

A colleague should be able to install WRN AI Gateway, open Settings, paste an OpenRouter inference API key once, and continue without PowerShell, JSON, environment variables, WSL, Git, or administrator rights.

The credential UI does not enable live Claude switching. The clean managed-Claude qualification gate remains unchanged.

## Product flow

Settings is intentionally minimal for internal WRN use.

The visible surface contains only the connection actions:

- **Connect** / **Replace**;
- **Test**;
- **Remove**.

Connection state is expressed through which actions are available and short success/failure toasts rather than explanatory panels or technical status text.

The WRN Claude Home tile checks only credential readiness at this checkpoint. If no usable key is configured, it takes the user to Settings. If a key is ready, the tile still explains that Claude switching remains disabled pending managed-Claude qualification.

## Key entry

The key-entry dialog uses a native WPF `PasswordBox`.

The application never redisplays a saved key.

When a replacement is attempted:

1. the existing stored key remains untouched;
2. the candidate is validated with OpenRouter;
3. only a passing candidate is DPAPI-protected and atomically swapped into place;
4. the local gateway identity is preserved;
5. an owned running gateway is stopped so the next start uses the new credential.

An invalid replacement cannot overwrite the working encrypted credential.

## Validation

OpenRouter credential validation uses the current-key endpoint:

`GET https://openrouter.ai/api/v1/key`

with bearer authentication.

The app records only safe validation metadata such as validation time, tier flag, remaining limit metadata, reset metadata, and expiry metadata.

Management/provisioning keys are rejected for this user-facing flow; WRN AI Gateway requires an inference credential.

The .NET Framework validation client explicitly uses TLS 1.2.

A manual **Test connection** revalidates the stored key. A failed test does not overwrite or delete the saved credential.

## Windows credential protection

The credential is stored at:

`%LOCALAPPDATA%\WRN-AI-Gateway\credentials\openrouter.key.dpapi`

The plaintext is a versioned envelope protected with Windows CurrentUser DPAPI.

The envelope contains:

- schema version;
- OpenRouter key;
- last successful validation metadata.

The key and metadata are written as one encrypted atomic unit so they cannot drift across separate files.

Normal application status/report surfaces never return the key.

## Gateway configuration

The first successfully saved credential also ensures a local gateway configuration exists.

The generated configuration contains:

- a cryptographically random local Claude-facing bearer token;
- a currently available loopback port.

Replacing the OpenRouter key preserves this gateway identity.

Removing the OpenRouter key intentionally leaves the gateway configuration in place so a later reconnect can reuse the same local identity/port.

The local bearer token is not the OpenRouter key and is never forwarded upstream.

## Backward compatibility

The packaged gateway accepts both:

- the Phase 4 `WRN-CRED-V1` encrypted envelope;
- the earlier development raw-DPAPI key format.

This compatibility allows existing development state to continue working while beta installations use the versioned envelope.

## Automated qualification

The deterministic credential-store fixture proves:

- DPAPI credential file creation;
- plaintext key absent from stored ciphertext;
- decryptable ready status;
- validation metadata retention;
- gateway config automatic creation;
- random local bearer generation;
- valid loopback port selection;
- gateway identity preserved across key replacement;
- replacement key actually stored;
- stale/mismatched validation blocked;
- rejected replacement leaves original ciphertext byte-for-byte unchanged;
- corrupt credential reports unreadable;
- remove deletes the OpenRouter credential but preserves gateway config;
- packaged gateway starts from the new versioned credential envelope;
- legacy raw-DPAPI gateway startup still works.

## Live OpenRouter qualification — 25 September 2026

A separate disposable live qualification read the already-authorised OpenRouter key file by **path**. The key was not passed as a command-line value and was not printed.

Observed:

- initial C# key validation: `KEY_VALID`;
- key stored into disposable CurrentUser-DPAPI state;
- stored key retest: `KEY_VALID`;
- disposable state deleted after the test.

The first live run exposed a .NET Framework TLS-default mismatch and failed safely with `KEY_VALIDATION_NETWORK_ERROR`. No key was stored. After explicitly aligning the validator with the gateway's TLS 1.2 transport, the same qualification passed.

## Distribution rule

ADR-007 remains authoritative:

- each colleague uses their own OpenRouter credential;
- store it with Windows CurrentUser protection;
- do **not** distribute a shared master key in the installer/package.

If central provisioning is added later, it must provision a per-user credential into the same protected store rather than embedding a reusable organisation secret in application files.

## Current UI status

The Settings page is intentionally reduced to three centered actions: Connect/Replace, Test, and Remove.

A small info button sits beside the action group. Optional connection/security details are available there instead of occupying the main Settings surface.

The key dialog is also compact: title, masked key field, action buttons, and error/progress text only when needed.

The normal user path avoids explanatory/security panels, API IDs, JSON, ports, DPAPI terminology, or other developer concepts.

Live Claude transition execution remains hard-disabled.
