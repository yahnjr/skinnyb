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
    private static TaskCompletionSource<string?>? _callbackTcs;
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
            System.Diagnostics.Debug.WriteLine($"[Auth] .env loaded, ClientId: '{ClientId}'");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] Failed to load env: {ex.Message}");
        }
    }

    public static async Task<bool> LoginAsync()
    {
        await EnsureEnvLoadedAsync();
        System.Diagnostics.Debug.WriteLine("[Auth] LoginAsync called");

        // Check network connectivity first
        var current = Connectivity.Current;
        if (current.NetworkAccess != NetworkAccess.Internet)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] No internet connection. NetworkAccess: {current.NetworkAccess}");
            return false;
        }
        System.Diagnostics.Debug.WriteLine($"[Auth] Network connectivity OK: {current.NetworkAccess}");

        // Initialize HttpClient early while network context is stable
        EnsureTokenClientReady();
        System.Diagnostics.Debug.WriteLine("[Auth] HTTP client initialized");

        _codeVerifier = GenerateCodeVerifier();
        string codeChallenge = GenerateCodeChallenge(_codeVerifier);
        string state = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            System.Diagnostics.Debug.WriteLine("[Auth] ERROR: ClientId empty.");
            return false;
        }

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

        System.Diagnostics.Debug.WriteLine("[Auth] Callback URI obtained, parsing code...");

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(callbackUri).Query);
        string? code = query["code"];
        if (string.IsNullOrWhiteSpace(code)) return false;

        System.Diagnostics.Debug.WriteLine($"[Auth] Authorization code extracted: {code.Substring(0, Math.Min(20, code.Length))}...");

        // Longer delay to ensure listener is fully closed and network context restores
        System.Diagnostics.Debug.WriteLine("[Auth] Waiting for network context to stabilize...");
        await Task.Delay(2500);

        // Re-check connectivity after returning from browser
        var connCheck = Connectivity.Current;
        System.Diagnostics.Debug.WriteLine($"[Auth] Connectivity after browser return: {connCheck.NetworkAccess}");
        // Note: Connectivity may show as "Local" during transition, which is OK
        // Only fail if truly offline
        if (connCheck.NetworkAccess == NetworkAccess.None)
        {
            System.Diagnostics.Debug.WriteLine("[Auth] Network access is None - offline");
            return false;
        }
        System.Diagnostics.Debug.WriteLine("[Auth] Network access is acceptable (Internet or Local), proceeding...");

        // Warmup request to re-establish network binding (best effort, don't block if it fails)
        System.Diagnostics.Debug.WriteLine("[Auth] Attempting warmup request to stabilize network...");
        try
        {
            using var warmupCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            var warmupResp = await _tokenClient!.GetAsync("https://www.google.com", HttpCompletionOption.ResponseHeadersRead, warmupCts.Token);
            System.Diagnostics.Debug.WriteLine($"[Auth] Warmup request completed with status {warmupResp.StatusCode}");
        }
        catch (Exception ex)
        {
            // Warmup failure is non-critical, we'll retry the token request anyway
            System.Diagnostics.Debug.WriteLine($"[Auth] Warmup request failed (will retry token exchange): {ex.GetType().Name}");
        }

        System.Diagnostics.Debug.WriteLine("[Auth] Listener cleanup delay complete");

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = _codeVerifier!
        });

        System.Diagnostics.Debug.WriteLine("[Auth] About to POST to oauth2.googleapis.com/token...");

        try
        {
            // Retry logic with exponential backoff (3 attempts)
            int maxRetries = 3;
            int delayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                System.Diagnostics.Debug.WriteLine($"[Auth] Token request attempt {attempt}/{maxRetries}...");
                
                // On retry attempts, recreate the HttpClient to rebind to restored network
                if (attempt > 1)
                {
                    System.Diagnostics.Debug.WriteLine($"[Auth] Attempt {attempt}: Recreating HttpClient to rebind network...");
                    CleanupTokenClient();
                    EnsureTokenClientReady();
                }

                try
                {
                    System.Diagnostics.Debug.WriteLine("[Auth] Making HTTP POST request with client...");
                    var response = await _tokenClient!.PostAsync("https://oauth2.googleapis.com/token", body);
                    System.Diagnostics.Debug.WriteLine("[Auth] HTTP POST completed, reading response...");
                    var json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[Auth] Token response status: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($"[Auth] Token response body: {json}");

                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Auth] HTTP {response.StatusCode}: Request failed");
                        return false;
                    }

                    // Check for error in response
                    string? error = ExtractJsonValue(json, "error");
                    if (!string.IsNullOrEmpty(error))
                    {
                        string? errorDesc = ExtractJsonValue(json, "error_description");
                        System.Diagnostics.Debug.WriteLine($"[Auth] ERROR from Google: {error} - {errorDesc}");
                        return false;
                    }

                    string? refreshToken = ExtractJsonValue(json, "refresh_token");
                    System.Diagnostics.Debug.WriteLine($"[Auth] Extracted refresh token: '{refreshToken?.Substring(0, Math.Min(10, refreshToken?.Length ?? 0))}...'");

                    if (string.IsNullOrWhiteSpace(refreshToken))
                    {
                        System.Diagnostics.Debug.WriteLine("[Auth] No refresh token in response — returning false");
                        return false;
                    }

                    await SecureStorage.Default.SetAsync(TokenKey, refreshToken);
                    System.Diagnostics.Debug.WriteLine("[Auth] Refresh token saved to SecureStorage");
                    return true;
                }
                catch (HttpRequestException ex) when (attempt < maxRetries)
                {
                    System.Diagnostics.Debug.WriteLine($"[Auth] Attempt {attempt} failed: {ex.InnerException?.Message}");
                    System.Diagnostics.Debug.WriteLine($"[Auth] Waiting {delayMs}ms before retry...");
                    await Task.Delay(delayMs);
                    delayMs *= 2;  // Exponential backoff
                    continue;
                }
            }

            // All retries exhausted
            System.Diagnostics.Debug.WriteLine("[Auth] All token request attempts failed");
            return false;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] Network error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Auth] Inner exception: {ex.InnerException?.Message}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] Unexpected error during token exchange: {ex.Message}");
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

        System.Diagnostics.Debug.WriteLine("[Auth] Creating HTTP client and handler...");
        _tokenHandler = new HttpClientHandler();
        #if ANDROID
        // On Android, configure for better network resilience
        _tokenHandler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
        _tokenHandler.ServerCertificateCustomValidationCallback = null;  // Use system cert store
        #endif
        _tokenClient = new HttpClient(_tokenHandler);
        _tokenClient.Timeout = TimeSpan.FromSeconds(15);  // 15 second timeout
        System.Diagnostics.Debug.WriteLine("[Auth] HTTP client created with 15s timeout");
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
            System.Diagnostics.Debug.WriteLine("[Auth] HTTP client cleaned up");
        }
    }

    private static async Task<string?> ListenForCallbackAsync()
    {
        // Kill any previous listener
        try { _activeListener?.Stop(); } catch { }

        var listener = new System.Net.HttpListener();
        _activeListener = listener;
        listener.Prefixes.Add("http://127.0.0.1:5678/");

        try
        {
            listener.Start();
            System.Diagnostics.Debug.WriteLine("[Auth] Listening for OAuth callback...");

            var context = await listener.GetContextAsync();
            string callbackUrl = context.Request.Url?.ToString() ?? "";
            System.Diagnostics.Debug.WriteLine($"[Auth] Callback received: {callbackUrl}");

            string responseHtml = "<html><body><h2>Signed in! You can return to SkinnyB.</h2></body></html>";
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "text/html";
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();

            System.Diagnostics.Debug.WriteLine("[Auth] Response sent, returning callback URL");
            return callbackUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] Listener error: {ex.Message}");
            return null;
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("[Auth] Stopping listener...");
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
            _activeListener = null;
            System.Diagnostics.Debug.WriteLine("[Auth] Listener stopped and cleaned up");
        }
    }

    // Might not be needed below
    public static void HandleCallback(string uri)
    {
        System.Diagnostics.Debug.WriteLine($"[Auth] Callback received: {uri}");
        _callbackTcs?.TrySetResult(uri);
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