using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace SkinnyB.Services;

public static class GoogleAuthService
{
#if ANDROID
    private static string ClientId =>
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "";
    private static string ClientSecret =>
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? "";
    private const string RedirectUri = "http://127.0.0.1:5678/oauth2callback";
    private const string TokenKey = "google_refresh_token";

    private static string? _codeVerifier;
    private static bool _envLoaded = false;
    private static System.Net.HttpListener? _activeListener;
    private static HttpClient? _tokenClient;
    private static HttpClientHandler? _tokenHandler;

    internal static async Task EnsureEnvLoadedAsync()
    {
        if (_envLoaded) return;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("env");
            using var reader = new StreamReader(stream);
            var lines = (await reader.ReadToEndAsync()).Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
            _envLoaded = true;
        }
        catch { }
    }

    public static async Task<bool> LoginAsync()
    {
        await EnsureEnvLoadedAsync();

        var current = Connectivity.Current;
        if (current.NetworkAccess != NetworkAccess.Internet)
            return false;

        EnsureTokenClientReady();

        _codeVerifier = GenerateCodeVerifier();
        string codeChallenge = GenerateCodeChallenge(_codeVerifier);
        string state = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(ClientId))
            return false;

        string authUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=code" +
            "&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fspreadsheets" +
            $"&state={state}" +
            $"&code_challenge={codeChallenge}" +
            "&code_challenge_method=S256" +
            "&access_type=offline" +
            "&prompt=consent";

        // Start local listener before opening browser
        var listenerTask = ListenForCallbackAsync();

        await Launcher.OpenAsync(new Uri(authUrl));

        string? callbackUri = await listenerTask.WaitAsync(TimeSpan.FromMinutes(5));
        if (string.IsNullOrWhiteSpace(callbackUri)) return false;

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(callbackUri).Query);
        string? code = query["code"];
        if (string.IsNullOrWhiteSpace(code)) return false;

        await Task.Delay(2500);

        var connCheck = Connectivity.Current;
        if (connCheck.NetworkAccess == NetworkAccess.None)
            return false;

        try
        {
            using var warmupCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _tokenClient!.GetAsync("https://www.google.com", HttpCompletionOption.ResponseHeadersRead, warmupCts.Token);
        }
        catch { }

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = _codeVerifier!
        });

        try
        {
            int maxRetries = 2;
            int delayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (attempt > 1)
                {
                    CleanupTokenClient();
                    EnsureTokenClientReady();
                }

                try
                {
                    var response = await _tokenClient!.PostAsync("https://oauth2.googleapis.com/token", body);
                    var json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return false;

                    string? error = ExtractJsonValue(json, "error");
                    if (!string.IsNullOrEmpty(error))
                        return false;

                    string? refreshToken = ExtractJsonValue(json, "refresh_token");
                    if (string.IsNullOrWhiteSpace(refreshToken))
                        return false;

                    await SecureStorage.Default.SetAsync(TokenKey, refreshToken);
                    return true;
                }
                catch (HttpRequestException) when (attempt < maxRetries)
                {
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                    continue;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] Error: {ex.Message}");
            return false;
        }
        finally
        {
            CleanupTokenClient();
        }
    }

    private static void EnsureTokenClientReady()
    {
        if (_tokenClient is not null) return;

        _tokenHandler = new HttpClientHandler();
        #if ANDROID
        _tokenHandler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
        #endif
        _tokenClient = new HttpClient(_tokenHandler);
        _tokenClient.Timeout = TimeSpan.FromSeconds(15);
    }

    private static void CleanupTokenClient()
    {
        try
        {
            _tokenClient?.Dispose();
            _tokenHandler?.Dispose();
        }
        catch { }
        finally
        {
            _tokenClient = null;
            _tokenHandler = null;
        }
    }

    private static async Task<string?> ListenForCallbackAsync()
    {
        try { _activeListener?.Stop(); } catch { }

        var listener = new System.Net.HttpListener();
        _activeListener = listener;
        listener.Prefixes.Add("http://127.0.0.1:5678/");

        try
        {
            listener.Start();

            var context = await listener.GetContextAsync();
            string callbackUrl = context.Request.Url?.ToString() ?? "";

            string responseHtml = "<html><body><h2>Signed in! You can return to SkinnyB.</h2></body></html>";
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "text/html";
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();

            return callbackUrl;
        }
        catch { return null; }
        finally
        {
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
            _activeListener = null;
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? ExtractJsonValue(string json, string key)
    {
        string search = $"\"{key}\"";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;
        idx = json.IndexOf(':', idx) + 1;
        while (idx < json.Length && json[idx] is ' ' or '"') idx++;
        int end = json.IndexOfAny(['"', ',', '}'], idx);
        return end < 0 ? null : json[idx..end].Trim('"');
    }
#else
    // Stub for non-Android platforms — auth is handled by GoogleWebAuthorizationBroker
    public static Task<bool> LoginAsync() => Task.FromResult(false);
    public static void HandleCallback(string uri) { }
#endif
}