# OAuth Resilience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent environment-specific OAuth support failures from crashing the desktop app and add a quick local watch workflow.

**Architecture:** OAuth URL notification is a non-critical UI side effect, so its clipboard operation is isolated and logged. Process-wide WPF exception boundaries use the existing redacting log. A small PowerShell entrypoint delegates incremental rebuild and hot reload to the .NET SDK.

**Tech Stack:** .NET 10, WPF, xUnit, PowerShell.

## Global Constraints

- Never write OAuth tokens, authorization codes, or client secrets to diagnostics.
- Preserve the existing public sign-in and retry interaction.
- Do not require an installer build for local development testing.

---

### Task 1: Make OAuth URL clipboard handling non-fatal

**Files:**
- Modify: `tests/AutoSaveGame.App.Tests/ViewModels/MainViewModelTests.cs`
- Modify: `src/AutoSaveGame.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IClipboard.SetText(string)`
- Produces: OAuth URL notification that does not throw when clipboard access fails.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void AuthUrlGenerated_WhenClipboardIsBlocked_DoesNotThrow()
{
    var runtime = new FakeRuntime([]);
    var sut = new MainViewModel(runtime, new FakePrompts([]), clipboard: new ThrowingClipboard());

    runtime.EmitAuthUrl("https://accounts.google.com/o/oauth2/v2/auth?code=secret");

    Assert.Contains("không thể sao chép", sut.StatusMessage);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AutoSaveGame.App.Tests --filter "AuthUrlGenerated_WhenClipboardIsBlocked_DoesNotThrow"`

Expected: FAIL because the clipboard exception escapes.

- [ ] **Step 3: Write minimal implementation**

```csharp
try { clipboard.SetText(url); }
catch (Exception exception)
{
    diagnosticLog.Write(exception, "Sao chép liên kết đăng nhập");
    StatusMessage = "Không thể sao chép liên kết đăng nhập. Trình duyệt sẽ mở để bạn tiếp tục.";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AutoSaveGame.App.Tests --filter "AuthUrlGenerated_WhenClipboardIsBlocked_DoesNotThrow"`

Expected: PASS.

### Task 2: Record and contain WPF dispatcher faults

**Files:**
- Create: `src/AutoSaveGame.App/Services/ApplicationExceptionHandler.cs`
- Create: `tests/AutoSaveGame.App.Tests/Services/ApplicationExceptionHandlerTests.cs`
- Modify: `src/AutoSaveGame.App/App.xaml.cs`

**Interfaces:**
- Produces: `ApplicationExceptionHandler.HandleDispatcherException(Exception, DispatcherUnhandledExceptionEventArgs)` that logs and sets `Handled`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void HandleDispatcherException_LogsAndMarksExceptionHandled()
{
    var log = new RecordingDiagnosticLog();
    var handler = new ApplicationExceptionHandler(log);
    var handled = handler.HandleDispatcherException(new InvalidOperationException("boom"));
    Assert.True(handled);
    Assert.Equal("Lỗi giao diện không xử lý", log.Operation);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AutoSaveGame.App.Tests --filter "HandleDispatcherException_LogsAndMarksExceptionHandled"`

Expected: FAIL because the handler does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public bool HandleDispatcherException(Exception exception)
{
    diagnosticLog.Write(exception, "Lỗi giao diện không xử lý");
    return true;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AutoSaveGame.App.Tests --filter "HandleDispatcherException_LogsAndMarksExceptionHandled"`

Expected: PASS.

### Task 3: Add the local hot-reload launcher

**Files:**
- Create: `scripts/Run-Dev.ps1`
- Modify: `docs/public-release-runbook.md`

**Interfaces:**
- Produces: `scripts/Run-Dev.ps1`, invoking `dotnet watch run --project src/AutoSaveGame.App`.

- [ ] **Step 1: Create the development launcher**

```powershell
& dotnet watch run --project (Join-Path $repositoryRoot 'src\AutoSaveGame.App')
exit $LASTEXITCODE
```

- [ ] **Step 2: Verify the command resolves the project**

Run: `powershell -ExecutionPolicy Bypass -File scripts/Run-Dev.ps1`

Expected: the app starts under `dotnet watch`; stop it manually after observing the watcher banner.

