using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using System.Threading.Channels;

// using подставь под свои namespace из генерации
using Grpc.Tradeapi.V1.Auth;

namespace Trading.Infrastructure.FinamV2.FinamGrpc;

public sealed class JwtProvider
{
    private readonly AuthService.AuthServiceClient _auth;
    private readonly string _secret;

    private string? _jwt; // читаем/пишем только через Volatile
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public JwtProvider(GrpcChannel channel, string secret)
    {
        _auth = new AuthService.AuthServiceClient(channel);
        _secret = secret;
    }

    public Metadata GetHeaders()
    {
        var jwt = Volatile.Read(ref _jwt);
        if (string.IsNullOrWhiteSpace(jwt))
            throw new InvalidOperationException("JWT not initialized. Call EnsureJwtAsync/StartRenewalLoopAsync first");

        return new Metadata { { "authorization", $"Bearer {jwt}" } };
    }

    public async Task<string> EnsureJwtAsync(CancellationToken ct)
    {
        var jwt = Volatile.Read(ref _jwt);
        if (!string.IsNullOrWhiteSpace(jwt))
            return jwt;

        await _authLock.WaitAsync(ct);
        try
        {
            jwt = Volatile.Read(ref _jwt);
            if (!string.IsNullOrWhiteSpace(jwt))
                return jwt;

            var resp = await _auth.AuthAsync(new AuthRequest { Secret = _secret }, cancellationToken: ct);
            Volatile.Write(ref _jwt, resp.Token);
            return resp.Token;
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<string> GetAccountIdAsync(CancellationToken ct)
    {
        var jwt = await EnsureJwtAsync(ct);
        var details = await _auth.TokenDetailsAsync(new TokenDetailsRequest { Token = jwt }, cancellationToken: ct);
        if (details.AccountIds.Count == 0)
            throw new InvalidOperationException("No accountIds in token");

        return details.AccountIds[0];
    }

    public async Task StartRenewalLoopAsync(Action<string> log, CancellationToken ct)
    {
        await EnsureJwtAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var call = _auth.SubscribeJwtRenewal(
                    new SubscribeJwtRenewalRequest { Secret = _secret },
                    cancellationToken: ct
                );

                await foreach (var msg in call.ResponseStream.ReadAllAsync(ct))
                {
                    if (!string.IsNullOrWhiteSpace(msg.Token))
                    {
                        Volatile.Write(ref _jwt, msg.Token);
                        log($"jwt renewed, len={msg.Token.Length}");
                    }
                }
            }
            catch (RpcException ex) when (!ct.IsCancellationRequested)
            {
                log($"jwt renewal rpc error: {ex.StatusCode} {ex.Status.Detail}");
                await Task.Delay(500, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log($"jwt renewal error: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(500, ct);
            }
        }
    }
}
