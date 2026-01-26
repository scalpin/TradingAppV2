//FinamTradingGateway.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Trading.Core.Interfaces;
using Trading.Core.Models;
using Trading.Infrastructure.FinamV2.FinamGrpc;
using Grpc.Tradeapi.V1.Orders;


namespace Trading.Infrastructure.FinamV2.Trading;

public sealed class FinamTradingGateway : ITradingGateway
{
    public event Action<OrderUpdate>? OrderUpdated;
    public event Action<TradeUpdate>? Trade;

    private readonly OrdersService.OrdersServiceClient _orders;
    private readonly JwtProvider _jwt;

    public FinamTradingGateway(GrpcChannel channel, JwtProvider jwt)
    {
        _orders = new OrdersService.OrdersServiceClient(channel);
        _jwt = jwt;
    }

    public Task StartAsync(string accountId, Action<string> log, CancellationToken ct)
        => RunOrderTradeStreamAsync(accountId, log, ct);

    public async Task<string> PlaceLimitAsync(string accountId, string symbol, Side side, decimal price, decimal qty, string? clientOrderId, CancellationToken ct)
    {
        await _jwt.EnsureJwtAsync(ct);

        var order = new Order
        {
            AccountId = accountId,
            Symbol = symbol,
            ClientOrderId = clientOrderId ?? Guid.NewGuid().ToString("N"),
        };

        // Quantity
        var qProp = typeof(Order).GetProperty(nameof(Order.Quantity))!;
        var qObj = ProtoValueFactory.CreateProtoDecimal(qProp.PropertyType, qty);
        qProp.SetValue(order, qObj);

        // Side
        ProtoEnum.SetByOriginalName(order, nameof(Order.Side), side == Side.Buy ? "SIDE_BUY" : "SIDE_SELL");
        ProtoEnum.SetByOriginalName(order, nameof(Order.Type), "ORDER_TYPE_LIMIT");
        ProtoEnum.SetByOriginalName(order, nameof(Order.TimeInForce), "TIME_IN_FORCE_DAY");

        // LimitPrice
        var pProp = typeof(Order).GetProperty(nameof(Order.LimitPrice))!;
        var pObj = ProtoValueFactory.CreateProtoDecimal(pProp.PropertyType, price);
        pProp.SetValue(order, pObj);

        var resp = await _orders.PlaceOrderAsync(order, headers: _jwt.GetHeaders(), cancellationToken: ct);
        return resp.OrderId;
    }

    public async Task<string> PlaceMarketAsync(string accountId, string symbol, Side side, decimal qty, string? clientOrderId, CancellationToken ct)
    {
        await _jwt.EnsureJwtAsync(ct);

        var order = new Order
        {
            AccountId = accountId,
            Symbol = symbol,
            ClientOrderId = clientOrderId ?? Guid.NewGuid().ToString("N"),
        };

        var qProp = typeof(Order).GetProperty(nameof(Order.Quantity))!;
        var qObj = ProtoValueFactory.CreateProtoDecimal(qProp.PropertyType, qty);
        qProp.SetValue(order, qObj);

        ProtoEnum.SetByOriginalName(order, nameof(Order.Side), side == Side.Buy ? "SIDE_BUY" : "SIDE_SELL");
        ProtoEnum.SetByOriginalName(order, nameof(Order.Type), "ORDER_TYPE_MARKET");
        ProtoEnum.SetByOriginalName(order, nameof(Order.TimeInForce), "TIME_IN_FORCE_DAY");

        var resp = await _orders.PlaceOrderAsync(order, headers: _jwt.GetHeaders(), cancellationToken: ct);
        return resp.OrderId;
    }

    public async Task CancelAsync(string accountId, string orderId, CancellationToken ct)
    {
        await _jwt.EnsureJwtAsync(ct);

        await _orders.CancelOrderAsync(
            new CancelOrderRequest { AccountId = accountId, OrderId = orderId },
            headers: _jwt.GetHeaders(),
            cancellationToken: ct);
    }

    private async Task RunOrderTradeStreamAsync(string accountId, Action<string> log, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _jwt.EnsureJwtAsync(ct);

                using var call = _orders.SubscribeOrderTrade(headers: _jwt.GetHeaders(), cancellationToken: ct);

                var req = new OrderTradeRequest
                {
                    AccountId = accountId
                };

                ProtoEnum.SetByOriginalName(req, nameof(OrderTradeRequest.Action), "ACTION_SUBSCRIBE");
                ProtoEnum.SetByOriginalName(req, nameof(OrderTradeRequest.DataType), "DATA_TYPE_ALL");

                await call.RequestStream.WriteAsync(req);

                await foreach (var msg in call.ResponseStream.ReadAllAsync(ct))
                {
                    foreach (var o in msg.Orders)
                    {
                        // важное: у тебя в логах было o.OrderId и o.Status
                        var orderId = o.OrderId;
                        var symbol = o.Order?.Symbol ?? "";
                        var side = ProtoMapping.MapSide(o.Order?.Side);
                        var status = ProtoMapping.MapStatus(o.Status);

                        var price = ProtoValueFactory.ReadProtoDecimal(o.Order?.LimitPrice);
                        var qty = ProtoValueFactory.ReadProtoDecimal(o.Order?.Quantity);

                        OrderUpdated?.Invoke(new OrderUpdate(
                            orderId,
                            symbol,
                            side,
                            status,
                            price,
                            qty,
                            o.Order?.ClientOrderId
                        ));
                    }

                    foreach (var t in msg.Trades)
                    {
                        // ты уже печатал t.Price?.Value и t.Size?.Value
                        var price = ProtoValueFactory.ReadProtoDecimal(t.Price);
                        var qty = ProtoValueFactory.ReadProtoDecimal(t.Size);

                        if (price is null || qty is null) continue;

                        Trade?.Invoke(new TradeUpdate(
                            t.TradeId,
                            t.Symbol,
                            ProtoMapping.MapSide(t.Side),
                            price.Value,
                            qty.Value,
                            DateTimeOffset.UtcNow
                        ));
                    }
                }
            }
            catch (RpcException ex) when (!ct.IsCancellationRequested)
            {
                log($"ordertrade rpc error: {ex.StatusCode} {ex.Status.Detail}");
                await Task.Delay(300, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log($"ordertrade error: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(300, ct);
            }
        }
    }
}
