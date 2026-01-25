using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trading.Infrastructure.FinamV2.FinamGrpc;

public static class FinamChannelFactory
{
    public static GrpcChannel Create()
    {
        var handler = new SocketsHttpHandler
        {
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true
        };

        return GrpcChannel.ForAddress("https://api.finam.ru", new GrpcChannelOptions
        {
            HttpHandler = handler
        });
    }
}
