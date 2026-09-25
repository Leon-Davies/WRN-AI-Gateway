# WRN AI Gateway

WRN AI Gateway is a planned user-space launcher and configuration layer for non-technical WRN users who need a simple way to choose between the organisation's standard Claude Desktop experience and a WRN-managed OpenRouter model experience.

The project is intentionally designed around several hard constraints:

- **No local administrator rights required.**
- **The organisation-managed Claude Desktop installation remains untouched.**
- **Claude/Cowork conversation history and workspace history must be preserved.**
- **WRN model choices must be centrally updateable without reinstalling the application.**
- **OpenRouter Zero Data Retention requirements must be enforced for WRN inference.**
- **Raw gateway/API errors must not become the normal user experience.**
- **Users must always have a clear path back to baseline WTW Claude.**

This repository is currently in the documentation and design phase. The detailed requirements, architecture, safety boundaries, UX specification, testing strategy and development roadmap will be added through the initial documentation PR.

> **Security note:** Never commit API keys, access tokens, private signing keys, user chat data, internal credentials or machine-specific secrets to this repository.
