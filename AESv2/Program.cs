using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class Program
{
    [STAThread]
    static async Task Main()
    {
        try
        {
            var savedAuth = await AuthService.LoadDeviceAuthAsync();
            if (savedAuth == null)
                savedAuth = await AuthService.LoginAsync();

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Credit:Krowe moh");
            Console.WriteLine("C# Port by Landmark");
            Console.ResetColor();

            string contentAccessToken = savedAuth.AccessToken; 

            try
            {
               
                var exchangeReq = new HttpRequestMessage(HttpMethod.Get, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/exchange");
                exchangeReq.Headers.Add("Authorization", $"Bearer {savedAuth.AccessToken}");
                var exchangeData = await HttpService.SendJsonAsync<JsonElement>(exchangeReq);
                string code = exchangeData.GetProperty("code").ToString();

                var iosTokenReq = new HttpRequestMessage(HttpMethod.Post, "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token")
                {
                    Content = new StringContent($"grant_type=exchange_code&exchange_code={code}", Encoding.UTF8, "application/x-www-form-urlencoded")
                };
                iosTokenReq.Headers.Add("Authorization", AuthService.ClientAuth_iOS); // iOSクライアント
                var iosToken = await HttpService.SendJsonAsync<JsonElement>(iosTokenReq);

                contentAccessToken = iosToken.GetProperty("access_token").ToString();
               
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token exchange warning: {ex.Message} - PCトークンで続行します");
            }

            Console.Write("マップコードを入力してください (例: 1234-5678-9012): ");
            var mapCode = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(mapCode)) throw new Exception("Map code cannot be empty");

            var mappingsData = await HttpService.GetJsonAsync<JsonElement>("https://fortnitecentral.genxgames.gg/api/v1/mappings");
            string versionStr = mappingsData.GetProperty("version").GetString();
            Console.WriteLine($"Version: {versionStr}");

            var match = Regex.Match(versionStr, @"Release-(\d+)\.(\d+)-CL-(\d+)");
            if (!match.Success) throw new Exception($"Failed to parse version: {versionStr}");
            var major = match.Groups[1].Value;
            var minor = match.Groups[2].Value;
            var cl = match.Groups[3].Value;

        RetryContent:
           
            var contentUrl = $"https://content-service.bfda.live.use1a.on.epicgames.com/api/content/v2/link/{mapCode}/cooked-content-package?role=client&platform=windows&major={major}&minor={minor}&patch={cl}";
            var request = new HttpRequestMessage(HttpMethod.Get, contentUrl);
            request.Headers.Add("Authorization", $"bearer {contentAccessToken}");

            try
            {
                var contentData = await HttpService.SendJsonAsync<JsonElement>(request);

                if (contentData.TryGetProperty("errorCode", out var errorCode) &&
                    errorCode.GetString() == "errors.com.epicgames.content-service.unexpected_link_type")
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Warning: 1.0 maps have no encryption and can't be downloaded");
                    Console.ResetColor();
                    return;
                }

                if (contentData.GetProperty("isEncrypted").GetBoolean())
                {
                    var moduleId = contentData.GetProperty("resolved").GetProperty("root").GetProperty("moduleId").ToString();
                    var version = contentData.GetProperty("resolved").GetProperty("root").GetProperty("version").ToString();

                    var payload = $"[{{\"moduleId\":\"{moduleId}\",\"version\":\"{version}\"}}]";
                    var keyReq = new HttpRequestMessage(HttpMethod.Post, "https://content-service.bfda.live.use1a.on.epicgames.com/api/content/v4/module/key/batch")
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };

                    keyReq.Headers.Add("Authorization", $"bearer {contentAccessToken}");

                    var keyData = await HttpService.SendJsonAsync<JsonElement[]>(keyReq);
                    var key = keyData[0].GetProperty("key").GetProperty("Key").ToString();
                    var guid = keyData[0].GetProperty("key").GetProperty("Guid").ToString();

                    var aesKey = "0x" + BitConverter.ToString(Convert.FromBase64String(key)).Replace("-", "");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"AES Key: {aesKey}");

                    ClipboardHelper.SetText(aesKey);
                    Console.WriteLine("AESキーをコピーしました");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"GUID: {guid}");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("Map is not encrypted");
                }
            }
            catch (InvalidTokenException)
            {
                Console.WriteLine("Token expired, retrying login...");
                savedAuth = await AuthService.LoginAsync();
                goto RetryContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving content: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Error: {ex.Message}");
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}