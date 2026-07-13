using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace OmarchyThemeCreator;

/// <summary>
/// Central Serilog bootstrap. Configures the global <see cref="Log.Logger"/> to write to a
/// rolling daily file under the XDG state directory (plus stderr when the app is launched from a
/// terminal), and installs process-wide handlers so no crash goes unrecorded — the same class of
/// bug that previously vanished silently unless the app happened to be run from a shell.
///
/// Serilog is used through its static <see cref="Log"/> facade rather than dependency injection:
/// this app wires everything by hand and has no DI container, so each class grabs a contextual
/// logger with <c>Log.ForContext&lt;T&gt;()</c> instead of receiving one via its constructor.
/// Call <see cref="Initialize"/> once at startup and <see cref="Shutdown"/> once on exit.
/// </summary>
public static class AppLog
{
    /// <summary>Absolute path of the directory logs are written to (surfaced to the user).</summary>
    public static string LogDirectory { get; private set; } = "";

    /// <summary>Absolute path of this run's log file (<c>YYYYMMDDHHMM.log</c>).</summary>
    public static string LogFilePath { get; private set; } = "";

    public static void Initialize()
    {
        LogDirectory = ResolveLogDir();
        Directory.CreateDirectory(LogDirectory);

        // One log file per run, named for the minute the app started (e.g. 202607122229.log).
        LogFilePath = Path.Combine(LogDirectory, $"{DateTime.Now:yyyyMMddHHmm}.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            // Console sink is only visible when launched from a terminal; keep it terse.
            .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.File(
                LogFilePath,
                // Write each event straight through (buffered: false) and force an fsync every
                // second, so the log reflects what happened right up to a hard crash.
                buffered: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Last-resort nets for exceptions that escape all try/catch blocks. The delete-dialog
        // NullReferenceException travelled this path — an unhandled exception on the UI dispatcher
        // ends up here before the process aborts.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception,
                "Unhandled exception (terminating: {Terminating})", e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        Log.Information("Omarchy Theme Creator starting. Logging to {LogFilePath}", LogFilePath);
    }

    public static void Shutdown()
    {
        Log.Information("Omarchy Theme Creator exiting.");
        Log.CloseAndFlush();
    }

    private static string ResolveLogDir()
    {
        string? xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        string baseDir = string.IsNullOrWhiteSpace(xdgState)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state")
            : xdgState;
        return Path.Combine(baseDir, "omarchy-theme-creator", "logs");
    }
}
