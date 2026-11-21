using System.Collections.Concurrent;

namespace GDI.Core.Events;

public sealed class BroadcastEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        lock (_lock)
        {
            var list = _handlers.GetOrAdd(typeof(TEvent), _ => []);
            list.Add(handler);
        }
    }

    public void Publish<TEvent>(TEvent @event)
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            Delegate[] targets;
            lock (_lock)
            {
                targets = list.ToArray();
            }

            foreach (var target in targets)
            {
                try
                {
                    ((Action<TEvent>)target)(@event);
                }
                catch
                {
                    // Ignore listener exceptions to preserve event propagation
                }
            }
        }
    }
}
