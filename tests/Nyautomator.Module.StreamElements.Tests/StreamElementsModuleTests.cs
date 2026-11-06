using Nyautomator;
using Nyautomator.Modules.StreamElements;
using Nyautomator.StreamElements;
using Xunit;
using static Nyautomator.Module.StreamElements.Tests.TestSupport;

namespace Nyautomator.Module.StreamElements.Tests;

public sealed class StreamElementsModuleTests
{
    [Fact]
    public async Task Credentials_PreferRefreshedOAuthToken()
    {
        var credentials = new StreamElementsCredentials(
            () => new StreamElementsModuleOptions { Jwt = "jwt" },
            () => new IntegrationToken { AccessToken = "old" },
            (_, _) => Task.FromResult<string?>("fresh"));

        var (token, usesOAuth) = await credentials.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("fresh", token);
        Assert.True(usesOAuth);
    }

    [Fact]
    public async Task Credentials_FallBackToJwtWithoutOAuthToken()
    {
        var credentials = new StreamElementsCredentials(
            () => new StreamElementsModuleOptions { Jwt = " jwt " },
            () => null,
            (_, _) => throw new InvalidOperationException("should not refresh"));

        var (token, usesOAuth) = await credentials.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("jwt", token);
        Assert.False(usesOAuth);
        Assert.True(credentials.HasAnyCredential);
    }

    [Fact]
    public async Task Credentials_ReturnNullWhenNothingConfigured()
    {
        var credentials = new StreamElementsCredentials(() => new StreamElementsModuleOptions(), () => null);

        var (token, _) = await credentials.GetAccessTokenAsync(CancellationToken.None);

        Assert.Null(token);
        Assert.False(credentials.HasAnyCredential);
    }

    [Fact]
    public async Task Status_ReportsLegacyJwtAndScopesFromMetadata()
    {
        var token = new IntegrationToken
        {
            AccessToken = "a",
            AccountDisplayName = "Streamer",
            Metadata = new Dictionary<string, string> { ["scopes"] = "channel:read tips:read" }
        };
        var handler = CreateHandler(new StreamElementsModuleOptions { Jwt = "jwt" }, () => token);

        var response = await handler.HandleAsync(Request("streamelements", "GET", "/status"), CancellationToken.None);

        var json = ToJson(response.Body);
        Assert.True(json.GetProperty("connected").GetBoolean());
        Assert.True(json.GetProperty("hasLegacyJwt").GetBoolean());
        Assert.Equal(2, json.GetProperty("scopes").GetArrayLength());
        Assert.Equal("Streamer", json.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Authorize_WithoutClientCredentials_DoesNotStartOAuth()
    {
        var started = false;
        var options = new StreamElementsModuleOptions();
        var credentials = new StreamElementsCredentials(() => options, () => null);
        var handler = new StreamElementsModuleApiHandler(
            credentials,
            new StreamElementsRealtimeService(credentials),
            (_, _) => { started = true; return Task.FromResult(StreamElementsOAuthResult.CreateFailure("unexpected")); },
            () => null,
            () => { });

        var response = await handler.HandleAsync(Request("streamelements", "POST", "/authorize"), CancellationToken.None);

        Assert.False(started);
        Assert.False(ToJson(response.Body).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task RealtimeConnect_WithoutCredentials_ReportsReason()
    {
        var handler = CreateHandler(new StreamElementsModuleOptions(), () => null);

        var response = await handler.HandleAsync(Request("streamelements", "POST", "/realtime/connect"), CancellationToken.None);

        var json = ToJson(response.Body);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("JWT", json.GetProperty("message").GetString());
    }

    private static StreamElementsModuleApiHandler CreateHandler(StreamElementsModuleOptions options, Func<IntegrationToken?> getToken)
    {
        var credentials = new StreamElementsCredentials(() => options, getToken, (_, _) => Task.FromResult<string?>(getToken()?.AccessToken));
        return new StreamElementsModuleApiHandler(
            credentials,
            new StreamElementsRealtimeService(credentials),
            (_, _) => throw new InvalidOperationException(),
            getToken,
            () => { });
    }
}
