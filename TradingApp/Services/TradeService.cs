using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TradingApp.Constants;
using TradingApp.Services;
using TradingApp.Models;
using System.Net.Http.Json;
using Grpc.Net.Client;
using System.Collections.Generic;
using System.Linq;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;
using System.Threading;
using Grpc.Core;
using static Google.Rpc.Context.AttributeContext.Types;
using System.IO;

public class TradeService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settings;
    private readonly InvestApiClient _client;

    public TradeService(SettingsService settingsService)
    {
        _httpClient = new HttpClient();
        _settings = settingsService;

        // Добавляем заголовок с токеном при инициализации
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _settings.fToken);
        _client = InvestApiClientFactory.Create(_settings.tToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    // Рабочий тейк профит
    public async Task<bool> PlaceTakeProfitOrderAsync(string board, string code, double activationPrice)
    {
        var endpoint = ApiEndpoints.PlaceStopOrder;

        var requestBody = new
        {
            clientId = _settings.ClientId,
            securityBoard = board,
            securityCode = code,
            buySell = "Buy",
            takeProfit = new
            {
                activationPrice = activationPrice,
                price = 0,
                marketPrice = true,
                quantity = new
                {
                    value = 1,
                    units = "Lots"
                },
                time = 0,
                useCredit = true
            },
            stopLoss = (object)null,
            validBefore = new
            {
                type = "TillEndSession",
                time = "2025-04-24T10:06:40Z"
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        var response = await _httpClient.PostAsync(endpoint, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
            return true;

        Console.ForegroundColor = ConsoleColor.Red;
        System.Diagnostics.Debug.WriteLine($"Ошибка при отправке заявки:");
        System.Diagnostics.Debug.WriteLine($"StatusCode: {response.StatusCode}");
        System.Diagnostics.Debug.WriteLine($"Response: {responseBody}");
        Console.ResetColor();
        return false;
    }


    // получение фотки стакана из тинькоффа (работает)
    public async Task GetOrderBookAsync(string figi)
    {
        var token = _settings.tToken;

        var channel = GrpcChannel.ForAddress("https://invest-public-api.tinkoff.ru");
        var client = new MarketDataService.MarketDataServiceClient(channel);

        var headers = new Grpc.Core.Metadata
    {
        { "Authorization", $"Bearer {token}" }
    };

        try
        {
            var request = new GetOrderBookRequest
            {
                Figi = figi,
                Depth = 10
            };

            var response = await client.GetOrderBookAsync(request, headers);

            foreach (var bid in response.Bids)
            {
                var price = bid.Price.Units + bid.Price.Nano / 1_000_000_000.0;
                System.Diagnostics.Debug.WriteLine($"Bid: {price} x {bid.Quantity}");
            }

            foreach (var ask in response.Asks)
            {
                var price = ask.Price.Units + ask.Price.Nano / 1_000_000_000.0;
                System.Diagnostics.Debug.WriteLine($"Ask: {price} x {ask.Quantity}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при получении стакана: {ex.Message}");
        }
    }
}