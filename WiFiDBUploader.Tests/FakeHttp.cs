using System.Net;
using System.Text;

namespace WiFiDBUploader.Tests;

/// <summary>Answers requests from a function and keeps what was sent, read before the request is disposed.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _answer;

    public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = new();

    public FakeHttp(Func<HttpRequestMessage, string, HttpResponseMessage> answer) => _answer = answer;

    public static FakeHttp Json(Func<string, string> answer) => new((_, body) => Ok(answer(body)));

    public static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/html"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string body = request.Content is null ? "" : Encoding.Latin1.GetString(await request.Content.ReadAsByteArrayAsync(ct));
        Requests.Add((request.Method, request.RequestUri!, body));
        return _answer(request, body);
    }
}
