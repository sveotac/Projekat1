namespace GitHub_Commits;

public class Logger
{
    private static readonly Logger _instance = new();
    private readonly object _lock = new();
    private readonly string _logPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "server.log");

    private Logger() { }

    public static Logger Instance => _instance;

    private void Write(string level, string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] [Thread {Thread.CurrentThread.ManagedThreadId}] [{level}] {message}";
        lock (_lock)
        {
            Console.WriteLine(entry);
            File.AppendAllText(_logPath, entry + Environment.NewLine);
        }
    }

    public void Info(string message)  => Write("INFO ", message);
    public void Warn(string message)  => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);
    public void Cache(string message) => Write("CACHE", message);
}
