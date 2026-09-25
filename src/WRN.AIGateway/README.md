# Phase 1 application shell

The first implementation slice is intentionally UI-only.

It provides:

- the WRN AI Gateway native Windows shell;
- WTW Claude and WRN Claude launch cards;
- greeting/personalisation;
- model catalogue preview;
- changelog;
- support and settings surfaces;
- non-destructive toast feedback;
- optional local WRN hero branding.

It does **not** yet:

- write Claude configuration;
- start/stop the production OpenRouter gateway;
- store OpenRouter credentials;
- switch WTW/WRN modes;
- change any Claude history or session data.

## Local build

From Windows PowerShell:

`powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Run`

The build uses only the .NET Framework compiler and WPF assemblies already present on managed Windows.

No administrator rights are required.
