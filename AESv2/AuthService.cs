using System;
using System.Collections.Generic;
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

    private static async Task SaveDeviceAuthAsync(AuthData authData)
    {
        if (!Directory.Exists(ConfigDirectory)) Directory.CreateDirectory(ConfigDirectory);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(DeviceAuthPath, JsonSerializer.Serialize(authData, jsonOptions));
    }
    public static async Task<AuthData?> RefreshTokenAsync(AuthData savedAuth)
    {
        var body = new Dictionary<string, string>
        {
            {"grant_type", "device_auth"},
            {"account_id", savedAuth.AccountId},
            {"device_id", savedAuth.DeviceId},
            {"secret", savedAuth.Secret},
            {"token_type", "eg1"}
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
        {
            Content = new FormUrlEncodedContent(body)
        };
        req.Headers.Add("Authorization", ClientAuth_PC);

        try
        {
            var res = await HttpService.SendJsonAsync<JsonElement>(req);
            savedAuth.AccessToken = res.GetProperty("access_token").GetString()!;
           
            await SaveDeviceAuthAsync(savedAuth);
            return savedAuth;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] 自動ログイン失敗: {ex.Message}");
            return null;
        }
    }
    public static async Task<AuthData?> LoginAsync()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") })
        };
        req1.Headers.Add("Authorization", ClientAuth_Switch);
        var res1 = await HttpService.SendJsonAsync<JsonElement>(req1);
        string switchToken = res1.GetProperty("access_token").GetString()!;

        var req2 = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/deviceAuthorization")
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("prompt", "login") })
        };
        req2.Headers.Add("Authorization", $"Bearer {switchToken}");
        var device = await HttpService.SendJsonAsync<JsonElement>(req2);

        string url = device.GetProperty("verification_uri_complete").GetString()!;
        string deviceCode = device.GetProperty("device_code").GetString()!;
        int interval = device.GetProperty("interval").GetInt32();

        Console.WriteLine($"\n以下のURLでログインしてください:\n{url}\n");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }

        JsonElement tokenData;
        while (true)
        {
            await Task.Delay(interval * 1000);
            try
            {
                var reqPoll = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
                {
                    Content = new FormUrlEncodedContent(new[] {
                        new KeyValuePair<string, string>("grant_type", "device_code"),
                        new KeyValuePair<string, string>("device_code", deviceCode)
                    })
                };
                reqPoll.Headers.Add("Authorization", ClientAuth_Switch);
                tokenData = await HttpService.SendJsonAsync<JsonElement>(reqPoll);
                break;
            }
            catch {}
        }

        string accountId = tokenData.GetProperty("account_id").GetString()!;
        string displayName = tokenData.GetProperty("displayName").GetString()!;
        var exchangeReq = new HttpRequestMessage(HttpMethod.Get, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/exchange");
        exchangeReq.Headers.Add("Authorization", $"Bearer {tokenData.GetProperty("access_token").GetString()}");
        var exchangeData = await HttpService.SendJsonAsync<JsonElement>(exchangeReq);
        string exchangeCode = exchangeData.GetProperty("code").GetString()!;

        var reqPC = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
        {
            Content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("grant_type", "exchange_code"),
                new KeyValuePair<string, string>("exchange_code", exchangeCode)
            })
        };
        reqPC.Headers.Add("Authorization", ClientAuth_PC);
        var resPC = await HttpService.SendJsonAsync<JsonElement>(reqPC);
        string pcAccessToken = resPC.GetProperty("access_token").GetString()!;
        var reqAuth = new HttpRequestMessage(HttpMethod.Post, $"https://account-public-service-prod.ol.epicgames.com/account/api/public/account/{accountId}/deviceAuth");
        reqAuth.Headers.Add("Authorization", $"Bearer {pcAccessToken}");
        var deviceAuth = await HttpService.SendJsonAsync<JsonElement>(reqAuth);

        var finalData = new AuthData
        {
            DisplayName = displayName,
            AccountId = accountId,
            DeviceId = deviceAuth.GetProperty("deviceId").GetString()!,
            Secret = deviceAuth.GetProperty("secret").GetString()!,
            AccessToken = pcAccessToken
        };

        await SaveDeviceAuthAsync(finalData);
        Console.WriteLine($"\nLogged in as: {displayName}");
        Console.WriteLine($"認証情報を保存しました。次回から自動ログインします。");

        return finalData;
    }
    public static async Task<AuthData?> LoadDeviceAuthAsync()
    {
        if (!File.Exists(DeviceAuthPath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(DeviceAuthPath);
            var authData = JsonSerializer.Deserialize<AuthData>(json);
            if (authData == null) return null;

            Console.WriteLine($"{authData.DisplayName} として自動ログイン中...");
            return await RefreshTokenAsync(authData);
        }
        catch { return null; }
    }
}