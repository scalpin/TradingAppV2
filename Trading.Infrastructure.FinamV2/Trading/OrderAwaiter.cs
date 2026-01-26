//OrderAwaiter.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Trading.Core.Models;

public sealed class OrderAwaiter
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OrderUpdate>> _final = new();

    public Task<OrderUpdate> WaitFinalAsync(string orderId, CancellationToken ct)
    {
        var tcs = _final.GetOrAdd(orderId, _ =>
            new TaskCompletionSource<OrderUpdate>(TaskCreationOptions.RunContinuationsAsynchronously));

        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public void OnOrderUpdate(OrderUpdate u)
    {
        if (!IsFinal(u.Status)) return;
        if (_final.TryRemove(u.OrderId, out var tcs))
            tcs.TrySetResult(u);
    }

    private static bool IsFinal(OrderStatus s) =>
        s is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected or OrderStatus.Expired;
}
