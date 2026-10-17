using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SmartDocs.GigaChat.Auth;
using SmartDocs.GigaChat.Tests.TestHelpers;
using Xunit;

namespace SmartDocs.GigaChat.Tests;

public class GigaChatAuthClientTests
{
    [Fact]
    public async Task GetAccessTokenAsync_AcquiresAndCachesToken()
    {
        int callCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            callCount++;
            Assert.Equal("https://ngw.devices.sberbank.ru:9443/api/v2/oauth", req.RequestUri?.ToString());
            Assert.True(req.Headers.Contains("RqUID"));
            Assert.Equal("Basic", req.Headers.Authorization?.Scheme);
            Assert.Equal("dGVzdC1hdXRoLWtleQ==", req.Headers.Authorization?.Parameter);

            var json = "{\"access_token\":\"mock-token-xyz\",\"expires_at\":" + (DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()) + "}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var options = new GigaChatClientOptions
        {
            AuthorizationKey = "dGVzdC1hdXRoLWtleQ==",
            Scope = GigaChatScope.Personal
        };

        using var authClient = new GigaChatAuthClient(options, httpClient);

        // First call - acquires token
        var token1 = await authClient.GetAccessTokenAsync();
        Assert.Equal("mock-token-xyz", token1);
        Assert.Equal(1, callCount);

        // Second call - should return cached token without making another HTTP request
        var token2 = await authClient.GetAccessTokenAsync();
        Assert.Equal("mock-token-xyz", token2);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithClientIdAndClientSecret_EncodesBasicAuthHeader()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("my-client-id:my-client-secret"));
            Assert.Equal("Basic", req.Headers.Authorization?.Scheme);
            Assert.Equal(expectedBase64, req.Headers.Authorization?.Parameter);

            var json = "{\"access_token\":\"token-from-client-credentials\",\"expires_at\":" + (DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()) + "}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var options = new GigaChatClientOptions
        {
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret",
            Scope = GigaChatScope.Corporate
        };

        using var authClient = new GigaChatAuthClient(options, httpClient);
        var token = await authClient.GetAccessTokenAsync();

        Assert.Equal("token-from-client-credentials", token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithAccessToken_BypassesOAuth()
    {
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        using var httpClient = new HttpClient(handler);

        const string accessToken = "direct-bearer-token-123";
        var options = new GigaChatClientOptions
        {
            AccessToken = accessToken
        };

        using var authClient = new GigaChatAuthClient(options, httpClient);

        var token = await authClient.GetAccessTokenAsync();
        Assert.Equal(accessToken, token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_DirectJwtToken_BypassesOAuth()
    {
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        using var httpClient = new HttpClient(handler);

        const string jwtToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.t-IDcSemACt8x4iTMC6Y53iMTGeWJaFFT2zJyvRwK44";
        var options = new GigaChatClientOptions
        {
            AuthorizationKey = jwtToken
        };

        using var authClient = new GigaChatAuthClient(options, httpClient);

        var token = await authClient.GetAccessTokenAsync();
        Assert.Equal(jwtToken, token);
    }
}
