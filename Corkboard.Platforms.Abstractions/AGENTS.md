# Corkboard.Platforms.Abstractions/ AGENTS.md

## Scope

This project defines app-internal, platform-neutral contracts. It is intentionally outside `Corkboard.Core` and `Corkboard.Shared`, and it is not a plugin API.

## Rules

- Keep this project free of Avalonia `Window`/lifetime types, native handles beyond `PlatformWindowHandle`, Win32/X11/AppKit APIs, desktop services, and `IServiceProvider`.
- Model a requested capability explicitly through `WindowFeatureRequest` and report each feature as applied, unsupported, or failed through `WindowFeatureApplyResult`.
- New capabilities must first be expressed here, then implemented only in the matching `Corkboard.Platforms.<OS>` project. Do not claim support in a platform root before the implementation exists.
- Platform projects that invoke system commands must use a bounded output reader/timeout and terminate timed-out child processes; do not call synchronous `ReadToEnd()` before waiting for process exit.
- `TopmostMode.UiAccess` remains a desktop process-token startup concern and must not be represented as a generic window feature.
