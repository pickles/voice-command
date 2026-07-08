# AGENTS.md

## Project Overview

Voice Chat Launcher is a Windows tray application that listens for a wake word and opens the ChatGPT Windows app voice mode. The main app is a .NET Framework WinForms executable built directly with `csc.exe`; there is no application `.csproj`. A small dependency `.csproj` exists only for NuGet restore.

Important paths:

- `src/VoiceChatLauncher/Program.cs`: main application, tray UI, settings UI, config parsing, ChatGPT window automation.
- `config.example.ini`: default user-facing configuration copied to `bin/config.ini` on first build.
- `build.ps1`: canonical build command.
- `diagnose.ps1`: local diagnostics for speech recognizers and ChatGPT app registration.

## Commands

Use these commands from the repository root:

```powershell
.\setup_openwakeword.ps1
.\build.ps1
.\diagnose.ps1
```

`.\build.ps1` writes `bin\VoiceChatLauncher.exe`. If the app is already running, the build can fail because the exe is locked. Stop `VoiceChatLauncher.exe` before rebuilding when needed.

`.\setup_openwakeword.ps1` restores ONNX Runtime packages and downloads the OpenWakeWord feature models required by the C# runtime.

## Working Tree Rules

- Do not commit generated or local runtime files: `bin/`, `.venv/`, `.pip-cache/`, logs, `.out`, `.err`, `__pycache__/`, or `.pyc`.
- Do not overwrite a user's `bin\config.ini`; `build.ps1` intentionally preserves it when it already exists.
- Keep `config.example.ini`, `AppConfig.DefaultConfigText()`, and any settings UI fields consistent when adding or renaming settings.
- Treat `models/*.onnx` as large binary assets. Do not replace or regenerate them unless explicitly requested.

## Coding Guidelines

- Preserve the current single-file C# app structure unless a task clearly requires a larger refactor.
- Target .NET Framework 4.x and APIs available to `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- Keep UI changes in WinForms. The app already references WPF types for UI Automation, so qualify ambiguous types such as `System.Drawing.Size` when necessary.
- Prefer explicit, defensive error handling around process launch, UI Automation, file IO, and speech recognition because these depend on local OS state.
- Keep user-facing Japanese text consistent with the existing README and app messages.
- Avoid broad refactors in `Program.cs`; many behaviors are coupled to Windows process state, tray lifetime, and config reloads.

## Validation

Before finishing code changes, run:

```powershell
.\build.ps1
```

If `bin\VoiceChatLauncher.exe` is locked by a running app and stopping it is not appropriate, compile to a temporary output path with the same references as `build.ps1` and report that the normal output was locked.

For changes that affect wake-word behavior, run `.\build.ps1` and prefer manual app testing with `OpenWakeWordLogScores=true` because microphone capture and UI Automation depend on local OS state. For changes that affect ChatGPT window detection or button clicking, prefer validating with `diagnose.ps1` and manual app testing because UI Automation depends on the installed ChatGPT app and current UI.

## Git and GitHub

- Do development in issue-sized units. Create or identify one GitHub issue per feature or fix before starting implementation so changes can be reverted or isolated quickly when problems appear.
- After implementation and validation are complete, open a PR, review it, and merge it before considering the work complete.
- Stage only files related to the task.
- Use focused commits with concise messages.
- If closing an issue after implementation, confirm the feature has been built or manually verified first.
- This repository's GitHub remote is `pickles/voice-command`.
