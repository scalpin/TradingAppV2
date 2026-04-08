//OrderAwaiter.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Trading.Core.Models;


namespace Trading.Core.Trading;

public sealed class OrderAwaiter
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OrderUpdate>> _waiters = new();
    private readonly ConcurrentDictionary<string, OrderUpdate> _finalCache = new();

    // чтобы не раздувать память финалками
    private readonly ConcurrentQueue<string> _finalQueue = new();
    private const int MaxFinalCache = 5000;

    public void OnOrderUpdate(OrderUpdate u)
    {
        if (!IsFinal(u.Status))
            return;

        _finalCache[u.OrderId] = u;
        _finalQueue.Enqueue(u.OrderId);

        while (_finalQueue.Count > MaxFinalCache && _finalQueue.TryDequeue(out var old))
            _finalCache.TryRemove(old, out _);

        if (_waiters.TryRemove(u.OrderId, out var tcs))
            tcs.TrySetResult(u);
    }

    public Task<OrderUpdate> WaitFinalAsync(string orderId, CancellationToken ct)
    {
        // финал уже пришёл до ожидания
        if (_finalCache.TryRemove(orderId, out var ready))
            return Task.FromResult(ready);

        var tcs = _waiters.GetOrAdd(orderId, _ =>
            new TaskCompletionSource<OrderUpdate>(TaskCreationOptions.RunContinuationsAsynchronously));

        // ещё раз проверим, вдруг финал прилетел в гонке между TryRemove и GetOrAdd
        if (_finalCache.TryRemove(orderId, out ready))
        {
            _waiters.TryRemove(orderId, out var _);
            tcs.TrySetResult(ready);
            return tcs.Task;
        }

        if (ct.CanBeCanceled)
            ct.Register(() => tcs.TrySetCanceled(ct));

        return tcs.Task;
    }

    private static bool IsFinal(OrderStatus s) =>
        s is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected or OrderStatus.Expired;
}
