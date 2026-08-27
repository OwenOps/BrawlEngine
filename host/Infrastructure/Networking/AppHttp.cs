using System.Net.Http.Headers;

namespace BrawlEngine.Host.Infrastructure.Networking;

public static class AppHttp
{
    public static HttpClient Shared { get; } = Create();

    private static HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BrawlEngine", "0.1"));
        return http;
    }
}
