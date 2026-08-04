using System.Collections.Concurrent;

namespace QbitAllocator;

public sealed class AllocatorStatusStore
{
    private readonly ConcurrentQueue<AllocationEvent> _decisions = new();
    private readonly ConcurrentQueue<AllocationEvent> _skipped = new();

    public DateTimeOffset? LastReconcileUtc { get; private set; }
    public string? LastReconcileError { get; private set; }

    public void AddDecision(string hash, string? name, string diskLabel, string savePath, string source)
    {
        Enqueue(_decisions, new AllocationEvent(DateTimeOffset.UtcNow, hash, name, "assigned", $"{source}: {diskLabel} -> {savePath}"));
    }

    public void AddSkipped(string hash, string? name, string reason)
    {
        Enqueue(_skipped, new AllocationEvent(DateTimeOffset.UtcNow, hash, name, "skipped", reason));
    }

    public void MarkReconcileSuccess()
    {
        LastReconcileUtc = DateTimeOffset.UtcNow;
        LastReconcileError = null;
    }

    public void MarkReconcileFailure(Exception ex)
    {
        LastReconcileUtc = DateTimeOffset.UtcNow;
        LastReconcileError = ex.Message;
    }

    public IReadOnlyList<AllocationEvent> GetDecisions() => _decisions.Reverse().Take(50).ToList();
    public IReadOnlyList<AllocationEvent> GetSkipped() => _skipped.Reverse().Take(50).ToList();

    private static void Enqueue(ConcurrentQueue<AllocationEvent> queue, AllocationEvent value)
    {
        queue.Enqueue(value);
        while (queue.Count > 100 && queue.TryDequeue(out _))
        {
        }
    }
}

public sealed record AllocationEvent(DateTimeOffset AtUtc, string? Hash, string? Name, string Kind, string Message);
