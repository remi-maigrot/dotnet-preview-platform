using System.Net;

namespace Devctl.Cli.Tests.TestSupport;

/// <summary>Records requests and answers with a canned response, so no test ever reaches a real endpoint.</summary>
internal sealed class FakeHttpHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "{}") : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
        return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
