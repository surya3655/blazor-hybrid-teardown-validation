using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HybridTearDown.Services;

public static class EvidenceLog
{
    private static readonly object SyncRoot = new();

    public static string FilePath { get; } = Path.Combine(FileSystem.AppDataDirectory, "evidence.log");
    public static string RuntimeDescription { get; } = RuntimeInformation.FrameworkDescription;

    public static void StartSession()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Write("Evidence", LogLevel.Information, $"Session started. Runtime: {RuntimeDescription}. Evidence file: {FilePath}");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Evidence] Failed to initialize {FilePath}: {exception}");
            Console.Error.WriteLine($"[Evidence] Failed to initialize {FilePath}: {exception}");
        }
    }

    public static void Write(string category, LogLevel level, string message, Exception? exception = null)
    {
        var entry = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" | ")
            .Append(level)
            .Append(" | ")
            .Append(category)
            .Append(" | ")
            .Append(message);

        if (exception is not null)
        {
            entry.AppendLine().Append(exception);
        }

        var text = entry.AppendLine().ToString();
        Debug.Write(text);
        Console.Write(text);

        try
        {
            lock (SyncRoot)
            {
                File.AppendAllText(FilePath, text, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class EvidenceLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new EvidenceLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class EvidenceLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                EvidenceLog.Write(categoryName, logLevel, formatter(state, exception), exception);
            }
        }
    }
}