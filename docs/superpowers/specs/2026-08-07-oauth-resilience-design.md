# OAuth resilience design

## Goal

Google sign-in must never terminate the WPF application when browser launch,
clipboard access, the OAuth loopback callback, or an unhandled UI exception
fails on a locked-down public computer.

## Design

`MainViewModel` treats copying the OAuth URL as optional. Clipboard failures
are caught at the callback boundary, logged with a correlation id, and replace
the success hint with a safe instruction; they do not affect the sign-in task.

`App` registers WPF dispatcher, application-domain, and unobserved-task
exception handlers. Each handler writes a redacted diagnostic record to the
existing session log and prevents the UI process from being torn down where
the platform permits. Normal sign-in exceptions remain handled by
`RunBusyAsync`, which preserves the retryable user-facing error state.

The development workflow uses a PowerShell script that runs the WPF project
with `dotnet watch run`. Source edits trigger the .NET hot-reload/watch loop;
edits that cannot be hot reloaded cause an automatic restart. This is only for
local testing and does not replace the signed installer build.

## Acceptance criteria

- A clipboard exception during OAuth URL generation does not escape the UI
  callback, is logged, and leaves a usable sign-in status.
- A dispatcher exception produces a redacted diagnostic log and is marked
  handled.
- The existing normal OAuth error/retry behavior remains unchanged.
- `scripts/Run-Dev.ps1` starts the app through `dotnet watch run`.
