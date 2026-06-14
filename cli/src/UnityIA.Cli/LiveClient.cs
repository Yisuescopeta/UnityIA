using System.Net.Http.Headers;
using System.Text;
using UnityIA.Protocol;

namespace UnityIA.Cli;

internal sealed class LiveClient : IDisposable
{
    private readonly HttpClient client;

    public LiveClient(LiveSessionDescriptor session)
    {
        client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{session.Port}/"),
            Timeout = TimeSpan.FromSeconds(35)
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.Token);
    }

    public Task<HttpResponseMessage> GetStatusAsync()
    {
        return client.GetAsync("status");
    }

    public Task<HttpResponseMessage> GetCommandsAsync()
    {
        return client.GetAsync("commands");
    }

    public Task<HttpResponseMessage> ExecuteAsync(string json)
    {
        return client.PostAsync(
            "execute",
            new StringContent(json, Encoding.UTF8, "application/json"));
    }

    public void Dispose()
    {
        client.Dispose();
    }
}

