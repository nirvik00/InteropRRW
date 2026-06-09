using System;
using System.IO;

namespace CharisRevitConnector;

/// <summary>
/// Minimal file logger. Revit add-ins have no console, so connection activity
/// is appended to %AppData%\Charis\charis.log. Logging must never throw.
/// </summary>
internal static class Log
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Charis",
        "charis.log");

    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Never let logging break the add-in.
        }
    }
}
