using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class AuthService
{
   
    public const string ClientAuth_Switch = "Basic NzlhOTMxYjM3NTMzNDU3MGFjMzY5MjM0ZjVkYTA1ZWM6ZWU3MzM1ZGYzYzRhNDEyY2I1NzA1NWFiN2FkZTY5M2U=";
    public const string ClientAuth_PC = "Basic M2Y2OWU1NmM3NjQ5NDkyYzhjYzI5ZjFhZjA4YThhMTI6YjUxZWU5Y2IxMjIzNGY1MGE2OWVmYTY3ZWY1MzgxMmU=";
    public const string ClientAuth_iOS = "Basic M2UxM2M1YzU3ZjU5NGE1NzhhYmU1MTZlZWNiNjczZmU6NTMwZTMxNmMzMzdlNDA5ODkzYzU1ZWM0NGYyMmNkNjI=";

    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FortniteMapTool"
    );

    private static readonly string DeviceAuthPath = Path.Combine(ConfigDirectory, "deviceAuth.json");
    public static async Task<AuthData?> RefreshTokenAsync(AuthData savedAuth)
    {
        if (string.IsNullOrEmpty(savedAuth.DeviceId) || string.IsNullOrEmpty(savedAuth.Secret))
            return null;

        var bodyString = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "device_auth"),
            new KeyValuePair<string, string>("account_id", savedAuth.AccountId),
            new KeyValuePair<string, string>("device_id", savedAuth.DeviceId),
            new KeyValuePair<string, string>("secret", savedAuth.Secret),
            new KeyValuePair<string, string>("token_type", "eg1")
        }).ReadAsStringAsync().Result;

        var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
        {
            Content = new StringContent(bodyString, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        tokenReq.Headers.Add("Authorization", ClientAuth_PC);

        try
        {
            var tokenResponse = await HttpService.SendJsonAsync<JsonElement>(tokenReq);

            if (tokenResponse.TryGetProperty("access_token", out var accessToken))
            {
                savedAuth.AccessToken = accessToken.GetString() ?? savedAuth.AccessToken;
            }

            return savedAuth;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: 認証リフレッシュ失敗。再ログインが必要です。({ex.Message})");
            Console.ResetColor();
            return null;
        }
    }

    public static async Task<AuthData?> LoginAsync()
    {
        if (!Directory.Exists(ConfigDirectory)) Directory.CreateDirectory(ConfigDirectory);

        var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
        {
            Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        tokenReq.Headers.Add("Authorization", ClientAuth_Switch);

        var tokenResponse = await HttpService.SendJsonAsync<JsonElement>(tokenReq);
        var accessToken = tokenResponse.GetProperty("access_token").ToString();

        var deviceReq = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/deviceAuthorization")
        {
            Content = new StringContent("prompt=login", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        deviceReq.Headers.Add("Authorization", $"Bearer {accessToken}");
        var device = await HttpService.SendJsonAsync<JsonElement>(deviceReq);

        var url = device.GetProperty("verification_uri_complete").ToString();
        Console.WriteLine($"以下のURLでログインしてください:");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(url);
        Console.ResetColor();

        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }

        JsonElement switchToken = default;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(int.Parse(device.GetProperty("expires_in").ToString()));
        var interval = int.Parse(device.GetProperty("interval").ToString());

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(interval * 1000);
            try
            {
                var body = $"grant_type=device_code&device_code={device.GetProperty("device_code").ToString()}";
                var req = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
                };
                req.Headers.Add("Authorization", ClientAuth_Switch); // Switchクライアント
                switchToken = await HttpService.SendJsonAsync<JsonElement>(req);
                break;
            }
            catch { continue; }
        }

        if (switchToken.ValueKind == JsonValueKind.Undefined) throw new Exception("Login timed out.");

        string displayName = switchToken.GetProperty("displayName").ToString();
        Console.WriteLine($"Logged in as: {displayName}");

        var exchangeReq = new HttpRequestMessage(HttpMethod.Get, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/exchange");
        exchangeReq.Headers.Add("Authorization", $"Bearer {switchToken.GetProperty("access_token")}");
        var exchangeData = await HttpService.SendJsonAsync<JsonElement>(exchangeReq);
        string exchangeCode = exchangeData.GetProperty("code").ToString();

        var pcTokenReq = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
        {
            Content = new StringContent($"grant_type=exchange_code&exchange_code={exchangeCode}", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        pcTokenReq.Headers.Add("Authorization", ClientAuth_PC);
        var pcToken = await HttpService.SendJsonAsync<JsonElement>(pcTokenReq);

        string finalAccessToken = pcToken.GetProperty("access_token").ToString();
        string accountId = pcToken.GetProperty("account_id").ToString();

        var authReq = new HttpRequestMessage(HttpMethod.Post, $"https://account-public-service-prod.ol.epicgames.com/account/api/public/account/{accountId}/deviceAuth");
        authReq.Headers.Add("Authorization", $"Bearer {finalAccessToken}");

        var deviceAuth = await HttpService.SendJsonAsync<JsonElement>(authReq);

        var authData = new AuthData
        {
            DisplayName = displayName,
            AccountId = accountId,
            DeviceId = deviceAuth.GetProperty("deviceId").ToString(),
            Secret = deviceAuth.GetProperty("secret").ToString(),
            AccessToken = finalAccessToken
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(DeviceAuthPath, JsonSerializer.Serialize(authData, jsonOptions));

        Console.WriteLine($"認証情報を保存しました: {DeviceAuthPath}");
        return authData;
    }

    public static async Task<AuthData?> LoadDeviceAuthAsync()
    {
        if (!File.Exists(DeviceAuthPath)) return null;
        try
        {
            var authData = JsonSerializer.Deserialize<AuthData>(await File.ReadAllTextAsync(DeviceAuthPath));
            if (authData == null) return null;
            return await RefreshTokenAsync(authData);
        }
        catch { return null; }
    }
}