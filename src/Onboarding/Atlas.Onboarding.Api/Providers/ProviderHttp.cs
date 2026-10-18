using System.Net.Http.Json;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Atlas.Onboarding.Api.Providers;

/// <summary>Shared HTTP plumbing for provider adapters: every way a call can fail becomes <see cref="ProviderUnavailableException"/>.</summary>
internal static class ProviderHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sends, deserializes and maps inside one guard, so a response that breaks the contract is a retry, not a crash.</summary>
    public static async Task<TResult> SendAsync<TResponse, TResult>(
        string provider,
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        Func<TResponse, TResult> map,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderUnavailableException(provider, $"HTTP {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                ?? throw new ProviderUnavailableException(provider, "Empty response body");
            return map(body);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw new ProviderUnavailableException(provider, ex.Message, ex);
        }
    }

    public static Task<HttpResponseMessage> PostJsonAsync<TBody>(HttpClient http, string path, TBody body, CancellationToken cancellationToken) =>
        http.PostAsJsonAsync(path, body, Json, cancellationToken);

    private static bool IsProviderFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or TimeoutRejectedException or BrokenCircuitException or JsonException or ProviderContractException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}
