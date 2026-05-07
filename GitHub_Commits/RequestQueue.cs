using System.Net;

namespace GitHub_Commits;

public class RequestQueue
{
    private readonly Queue<HttpListenerContext> _queue = new();
    private readonly object _lock = new();
    private readonly int _maxSize;

    public RequestQueue(int maxSize = 100)
    {
        _maxSize = maxSize;
    }

    public void Enqueue(HttpListenerContext context)
    {
        lock (_lock)
        {
            while (_queue.Count >= _maxSize)
                Monitor.Wait(_lock);

            _queue.Enqueue(context);
            Monitor.PulseAll(_lock);
        }
    }

    public HttpListenerContext Dequeue()
    {
        lock (_lock)
        {
            while (_queue.Count == 0)
                Monitor.Wait(_lock);

            var context = _queue.Dequeue();
            Monitor.PulseAll(_lock);
            return context;
        }
    }
}
