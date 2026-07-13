# 12 — Logging & Diagnostics

> Serilog via a static facade (no DI), a rolling per-run log file, and process-wide crash handlers.

Related: [[Home]] · [[02-Bootstrap-and-Composition-Root]]

## Why a static facade, not injected loggers

The app wires everything by hand and has no DI container (see
[[02-Bootstrap-and-Composition-Root]]), so there's nothing to inject an `ILogger` through. Instead,
each class grabs its own contextual logger from Serilog's static `Log`:

```csharp
private static readonly ILogger Log = Serilog.Log.ForContext<MainWindowViewModel>();
```

`ForContext<T>()` stamps every event with `SourceContext = <full type name>`, which the file template
prints — so a log line tells you which class emitted it without any extra work.

## Bootstrap — `AppLog.Initialize()`

Called as the very first thing in `Program.Main`, before any Avalonia code, so even startup failures
are captured.

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    // Console is only visible when launched from a terminal; keep it terse.
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
    .WriteTo.File(
        LogFilePath,
        buffered: false,                              // write each event straight through
        flushToDiskInterval: TimeSpan.FromSeconds(1), // fsync every second
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

Design choices worth noting:

- **Two sinks, two levels.** The file captures everything from `Debug` up; the console only shows
  `Information`+ so a terminal launch isn't flooded.
- **Unbuffered + 1s fsync.** So the log reflects what happened right up to a hard crash — the class of
  bug that used to vanish silently.
- **One file per run.** `LogFilePath` is named for the minute the app started
  (`YYYYMMDDHHMM.log`).

### Where logs go

`ResolveLogDir()` honors `XDG_STATE_HOME`, falling back to `~/.local/state`:

```
$XDG_STATE_HOME/omarchy-theme-creator/logs/     (or ~/.local/state/omarchy-theme-creator/logs/)
```

The resolved `AppLog.LogDirectory` and `AppLog.LogFilePath` are public so the UI can point users at
their logs.

## Crash safety

Two process-wide last-resort handlers are installed during `Initialize`, catching exceptions that
escape every `try/catch` (including on the Avalonia UI dispatcher):

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Log.Fatal(e.ExceptionObject as Exception,
        "Unhandled exception (terminating: {Terminating})", e.IsTerminating);

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Unobserved task exception");
    e.SetObserved();
};
```

Combined with the `try/catch/finally` in [[02-Bootstrap-and-Composition-Root|Program.Main]] (which
logs `Fatal` and always calls `AppLog.Shutdown()` → `Log.CloseAndFlush()`), nothing should terminate
the process without leaving a record.

## Logging conventions (from `CLAUDE.md`)

Keep these consistent when adding logs:

| Level | Use for |
|---|---|
| `Information` | Significant state changes in services; user-facing command outcomes. |
| `Warning` | Swallowed/handled failures (graceful-degradation paths). |
| `Error` | VM command handlers logging a user-facing failure in their `catch`. |
| `Fatal` | Only the crash handlers / `Program.Main`. |
| `Debug` / none | Per-frame or per-slider paths (image render, palette extraction) — **don't** flood these. |

Example of the Warning convention, from
[[09-Live-Theming-and-Self-Sync|LiveOmarchyThemeService]]:

```csharp
catch (Exception ex)
{
    // Watching is best-effort; the initial colors were still read at construction.
    Log.Warning(ex, "Failed to watch live theme directory {Dir}", _currentDir);
}
```
