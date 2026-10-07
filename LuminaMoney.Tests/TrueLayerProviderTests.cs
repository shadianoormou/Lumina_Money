using System.Net;
using System.Text;
using LuminaMoney.Api.Services;
using Microsoft.Extensions.Configuration;

namespace LuminaMoney.Tests;

public sealed class TrueLayerProviderTests
{
    [Fact]
    public async Task Data_v3_accounts_and_async_transactions_are_parsed()
    {
        var connectionId = Guid.NewGuid().ToString();
        var handler = new TrueLayerContractHandler(connectionId);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Banking:TrueLayer:Enabled"] = "true", ["Banking:TrueLayer:ClientId"] = "client", ["Banking:TrueLayer:ClientSecret"] = "secret",
            ["Banking:TrueLayer:ReturnUri"] = "https://api.example.test/banking/truelayer/return", ["Banking:TrueLayer:AuthBaseUrl"] = "https://auth.example.test",
            ["Banking:TrueLayer:ApiBaseUrl"] = "https://api.example.test", ["Banking:TrueLayer:PollAttempts"] = "1"
        }).Build();
        var provider = new TrueLayerBankProvider(new SingleClientFactory(new HttpClient(handler)), configuration);

        var result = await provider.ImportAsync(connectionId, "203.0.113.8", CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Single(result.Accounts);
        Assert.Single(result.Transactions);
        Assert.Equal(-4200, result.Transactions[0].AmountInMinor);
        Assert.Equal("Post Office", result.Transactions[0].MerchantName);
        Assert.True(handler.SawConnectionHeader);
        Assert.True(handler.SawUserIpHeader);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TrueLayerContractHandler(string connectionId) : HttpMessageHandler
    {
        public bool SawConnectionHeader { get; private set; }
        public bool SawUserIpHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/connect/token") return Json(HttpStatusCode.OK, """{"access_token":"server-token"}""");
            SawConnectionHeader |= request.Headers.TryGetValues("Connection-Id", out var values) && values.Single() == connectionId;
            SawUserIpHeader |= request.Headers.TryGetValues("Tl-User-IP", out var ips) && ips.Single() == "203.0.113.8";
            if (request.Method == HttpMethod.Get && path == "/v3/connected-accounts") return Json(HttpStatusCode.OK,
                """{"items":[{"id":"account-1","type":"account","account_type":"current","currency":"GBP","account_holder_names":["Test User"]}]}""");
            if (request.Method == HttpMethod.Post && path.EndsWith("/transactions/requests", StringComparison.Ordinal)) return Json(HttpStatusCode.Accepted,
                """{"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","status":"pending"}""");
            if (request.Method == HttpMethod.Get && path.Contains("/transactions/requests/", StringComparison.Ordinal)) return Json(HttpStatusCode.OK,
                """{"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","status":"completed","result":{"items":[{"id":"transaction-1","timestamp":"2026-08-24T12:34:56Z","description":"POST OFFICE","currency":"GBP","amount_in_minor":-4200,"status":"settled","enrichment":{"merchant_name":"Post Office","transaction_category":{"category_name":"Shopping"}}}],"pagination":{"next_cursor":null}}}""");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}
