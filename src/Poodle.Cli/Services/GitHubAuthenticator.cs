using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Octokit;
using Spectre.Console;

namespace Poodle.Cli.Services;

internal sealed class GitHubAuthenticator
{
    private const string ClientId = "Ov23lijbhH0pf9ewYsGd";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IAnsiConsole _console;
    private readonly string _tokenFilePath;

    public GitHubAuthenticator(IAnsiConsole console)
    {
        _console = console;
        _tokenFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "poodle",
            "auth.json");
    }

    public async Task<AuthenticatedGitHubClient> AuthenticateAsync(CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var storedToken = await LoadStoredTokenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(storedToken?.RefreshToken))
        {
            _console.MarkupLine("[blue]Using stored refresh token to authenticate with GitHub...[/]");
            var refreshed = await TryRefreshAccessTokenAsync(storedToken.RefreshToken!, cancellationToken);
            if (refreshed is not null)
            {
                var session = await CreateSessionAsync(client, refreshed);
                await SaveTokenAsync(refreshed, cancellationToken);
                return session;
            }

            _console.MarkupLine("[yellow]Stored refresh token could not be used. Falling back to device flow.[/]");
        }
        else
        {
            _console.MarkupLine("[blue]No stored refresh token found. Starting GitHub device flow...[/]");
        }

        var token = await RunDeviceFlowAsync(client, cancellationToken);
        var authenticatedSession = await CreateSessionAsync(client, token);
        await SaveTokenAsync(token, cancellationToken);

        return authenticatedSession;
    }

    private static GitHubClient CreateClient()
    {
        return new GitHubClient(new Octokit.ProductHeaderValue("poodle"));
    }

    private async Task<AuthenticatedGitHubClient> CreateSessionAsync(
        GitHubClient client,
        TokenExchangeResponse token)
    {
        client.Credentials = new Credentials(token.AccessToken);
        var user = await client.User.Current();

        _console.MarkupLine($"[green]Authenticated as[/] [bold]{Markup.Escape(user.Login)}[/].");
        return new AuthenticatedGitHubClient(client, user, token.AccessToken);
    }

    private async Task<TokenExchangeResponse> RunDeviceFlowAsync(
        GitHubClient client,
        CancellationToken cancellationToken)
    {
        var request = new OauthDeviceFlowRequest(ClientId);
        request.Scopes.Add("repo");
        request.Scopes.Add("workflow");

        var response = await client.Oauth.InitiateDeviceFlow(request, cancellationToken);

        var panel = new Panel(
                $"Visit [link]{Markup.Escape(response.VerificationUri)}[/] and enter code [bold yellow]{Markup.Escape(response.UserCode)}[/].")
            .Header("GitHub Device Login")
            .Border(BoxBorder.Rounded)
            .Expand();
        _console.Write(panel);

        if (_console.Confirm("Open the verification page in your browser now?"))
        {
            TryOpenBrowser(response.VerificationUri);
        }

        _console.MarkupLine("[blue]Waiting for GitHub authorization to complete...[/]");
        var token = await client.Oauth.CreateAccessTokenForDeviceFlow(ClientId, response, cancellationToken);

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException($"GitHub device flow failed: {token.ErrorDescription ?? token.Error ?? "unknown error"}");
        }

        return TokenExchangeResponse.FromOauthToken(token);
    }

    private async Task<TokenExchangeResponse?> TryRefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("poodle");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            })
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = JsonSerializer.Deserialize<TokenExchangeResponse>(payload, JsonOptions);

        if (!response.IsSuccessStatusCode || token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return null;
        }

        return token;
    }

    private async Task<StoredToken?> LoadStoredTokenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_tokenFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_tokenFilePath);
        return await JsonSerializer.DeserializeAsync<StoredToken>(stream, JsonOptions, cancellationToken);
    }

    private async Task SaveTokenAsync(TokenExchangeResponse token, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_tokenFilePath);
        if (directory is null)
        {
            throw new InvalidOperationException("Could not determine the auth token directory.");
        }

        Directory.CreateDirectory(directory);

        var now = DateTimeOffset.UtcNow;
        var storedToken = new StoredToken
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            AccessTokenExpiresAtUtc = token.ExpiresIn > 0 ? now.AddSeconds(token.ExpiresIn) : null,
            RefreshTokenExpiresAtUtc = token.RefreshTokenExpiresIn > 0 ? now.AddSeconds(token.RefreshTokenExpiresIn) : null
        };

        await using var stream = File.Create(_tokenFilePath);
        await JsonSerializer.SerializeAsync(stream, storedToken, JsonOptions, cancellationToken);

        _console.MarkupLine($"[green]Saved refresh token to[/] [grey]{Markup.Escape(_tokenFilePath)}[/].");
    }

    private void TryOpenBrowser(string verificationUri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = verificationUri,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _console.MarkupLine($"[yellow]Could not open the browser automatically:[/] {Markup.Escape(exception.Message)}");
        }
    }

    internal sealed record AuthenticatedGitHubClient(IGitHubClient Client, User User, string AccessToken);

    private sealed class StoredToken
    {
        public string? AccessToken { get; set; }

        public string? RefreshToken { get; set; }

        public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }

        public DateTimeOffset? RefreshTokenExpiresAtUtc { get; set; }
    }

    private sealed class TokenExchangeResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token_expires_in")]
        public int RefreshTokenExpiresIn { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }

        public static TokenExchangeResponse FromOauthToken(OauthToken token)
        {
            return new TokenExchangeResponse
            {
                AccessToken = token.AccessToken,
                ExpiresIn = token.ExpiresIn,
                RefreshToken = token.RefreshToken,
                RefreshTokenExpiresIn = token.RefreshTokenExpiresIn,
                Error = token.Error,
                ErrorDescription = token.ErrorDescription
            };
        }
    }
}
