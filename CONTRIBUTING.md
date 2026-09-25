# Contributing

## Start here

Before changing code, read:

1. docs/DEVELOPER_ONBOARDING.md
2. docs/PRODUCT_REQUIREMENTS.md
3. docs/ARCHITECTURE.md
4. docs/PROJECT_CONTEXT.md
5. docs/TESTING_AND_ACCEPTANCE.md
6. docs/DECISIONS.md

## Development principles

- Keep changes small and reviewable.
- Do not broaden a slice without evidence.
- Preserve the no-admin requirement.
- Preserve Claude history by default.
- Never modify the managed Claude package.
- Treat security boundaries as product constraints, not obstacles to bypass.
- Keep model-specific logic in catalogue data where possible.
- Add failure-path tests with happy-path tests.
- Prefer explicit rollback over clever mutation.
- Record important architecture changes in DECISIONS.md.

## Repository safety

Never commit:

- API keys;
- access tokens;
- private signing keys;
- WTW-private endpoints;
- user chats;
- user workspace files;
- logs containing prompts/responses;
- machine-specific secrets;
- private organisation identifiers.

## Pull requests

A PR should state:

- problem being solved;
- authorised scope;
- files/components changed;
- tests run;
- history/security impact;
- rollback behaviour;
- screenshots for user-visible changes;
- known limitations;
- follow-up work explicitly out of scope.

For configuration/mode work, include evidence that no unexpected Claude data paths were written.

## Definition of done

A feature is not done merely because it works once.

It is done when:

- tests cover the intended behaviour;
- failure behaviour is acceptable;
- no-admin operation is preserved;
- history boundary is preserved;
- documentation is updated;
- rollback/recovery is understood.
