using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LegacyVault.BLL.DTOs;
using Microsoft.Extensions.Configuration;

internal static class TestAuthentication
{
    public static void Configure(IConfigurationBuilder configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        ["Jwt:Issuer"] = "LegacyVaultTest", ["Jwt:Audience"] = "VaultTestClient", ["Jwt:LifetimeMinutes"] = "15"
    });
    public static async Task<TokenDto> UseToken(HttpClient client, HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Login failed: " + (int)response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<TokenDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return token;
    }
}

// In-process HTTPS request scheme with real OIDC cookies, no Windows key store or TLS bypass.
internal sealed class CookieTestHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public CookieContainer CookieContainer { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var cookie = CookieContainer.GetCookieHeader(request.RequestUri!);
        if (cookie.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        var response = await base.SendAsync(request, ct);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var value in values) CookieContainer.SetCookies(request.RequestUri!, value);
        return response;
    }
}
