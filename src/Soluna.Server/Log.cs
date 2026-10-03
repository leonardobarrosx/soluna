namespace Soluna.Server;

internal static class Log
{
    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    private static void Write(string level, string message) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {level} {message}");
}
