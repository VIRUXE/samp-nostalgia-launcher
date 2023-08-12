using System;
using System.Collections.Generic;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NostalgiaAnticheat.Game;

namespace NostalgiaAnticheat {

    public class PingResponse {
        public string Message;
        public List<ResponseAction> Actions { get; set; }
    }

    public class ResponseAction {
        public ResponseType Type { get; set; }
        public string Target { get; set; }
    }

    public enum ResponseType { 
        Screenshot
    }

    internal class Player {
        private const string API_ADDR = "https://api.scavengenostalgia.fun";

        public record struct PingData(IEnumerable<(string FileName, string WindowTitle)> openWindows, List<string> modules);

        private readonly HttpClient _httpClient = new();
        private Timer _pinger;
        private readonly string _hwid;
        public bool LoggedIn => !string.IsNullOrEmpty(Nickname);
        public string Nickname { get; private set; }

        public Player() {
            _hwid = GetHWID();

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NostalgiaLauncher");
        }

        private async void OnPingEvent(object state) {
            if (!GTASA.IsRunning) return;

            try {
                var response = await _httpClient.PostAsJsonAsync(API_ADDR + "/ping", new PingData(OS.GetOpenWindows(), new List<string> { }));

                var responseContent = await response.Content.ReadAsStringAsync();
                var parsedResponse = JsonSerializer.Deserialize<PingResponse>(responseContent);

                if(parsedResponse.Message != null) { // API doesn't like you
                    await Logout(parsedResponse.Message);
                    return;
                }

                // We're good so let's complete Actions if we have any
                foreach (var action in parsedResponse.Actions) {
                    switch (action.Type) {
                        case ResponseType.Screenshot:
                            // Handle ActionType1 targeting action.Target
                            break;
                            // ... Add other cases as needed
                    }
                }

            } catch (HttpRequestException e) {
                await Logout($"API Error: {e.Message}");
            } catch (Exception e) {
                await Logout($"Unexpected Error: {e.Message}");
            }
        }

        public async Task<Dictionary<string, string>> Login(string nickname, string password) {
            try {
                using HttpResponseMessage response = await _httpClient.PostAsync(API_ADDR + "/login", new StringContent(JsonSerializer.Serialize(new {
                    nickname,
                    password,
                    serial    = _hwid,
                    version   = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }), Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode) {
                    string result = await response.Content.ReadAsStringAsync();

                    var resultData = JsonSerializer.Deserialize<Dictionary<string, string>>(result);

                    if (resultData != null && resultData.ContainsKey("token")) {
                        Nickname = nickname; // ? It may be different for some reason?

                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resultData["token"]);

                        // Start the /ping timer
                        _pinger?.Dispose();
                        _pinger = new(OnPingEvent, null, 0, 1000);

                        return resultData;
                    }

                    return new Dictionary<string, string>();
                }

                Dictionary<string, string> errorResult = new();

                switch (response.StatusCode) {
                    case HttpStatusCode.NotAcceptable:
                        errorResult.Add("error", Program.SystemLanguage == Language.PT ? "Não autorizado - Versão incorreta" : "Unauthorized - Incorrect version");
                        break;
                    case HttpStatusCode.Unauthorized:
                        errorResult.Add("error", Program.SystemLanguage == Language.PT ? "Não autorizado - Nome de usuário ou senha inválidos" : "Unauthorized - Invalid username or password");
                        break;
                    case HttpStatusCode.Forbidden:
                        errorResult.Add("error", Program.SystemLanguage == Language.PT ? "Proibido - Você não tem acesso a este recurso" : "Forbidden - You do not have access to this resource");
                        break;
                    case HttpStatusCode.NotFound:
                        errorResult.Add("error", Program.SystemLanguage == Language.PT ? "Não encontrado - O servidor não conseguiu encontrar o recurso solicitado" : "Not Found - The server could not find the requested resource");
                        break;
                    case HttpStatusCode.TooManyRequests:
                        errorResult.Add("error", Program.SystemLanguage == Language.PT ? "Muitas solicitações - Limitação de taxa aplicada" : "Too Many Requests - Rate limiting is applied");
                        break;
                    default:
                        errorResult.Add("error", Program.SystemLanguage == Language.PT ? "Ocorreu um erro desconhecido" : "An unknown error ocurred" + $". Código: {response.StatusCode}");
                        break;
                }

                return errorResult;
            } catch (HttpRequestException e) {
                Console.WriteLine($"Login Error: {e.Message}");
            }

            return new Dictionary<string, string>();
        }

        public async Task<bool> Logout(string message = null) {
            if (!LoggedIn) return false;

            Nickname = null;

            if (!string.IsNullOrEmpty(message)) Console.WriteLine($"Logout Message: {message}");

            try {
                using HttpResponseMessage response = await _httpClient.PostAsync(API_ADDR + "/logout", null);

                _httpClient.DefaultRequestHeaders.Authorization = null;

                if (response.IsSuccessStatusCode) return true; 
            } catch {}

            return false;
        }

        public async Task<bool> IsHwBanned() {
            try {
                using (HttpClient client = new()) {
                    HttpResponseMessage response = await client.GetAsync(API_ADDR + $"/hwid/{_hwid}");

                    if (response.StatusCode == HttpStatusCode.Forbidden) {
                        string result = await response.Content.ReadAsStringAsync();

                        var banData = JsonSerializer.Deserialize<Dictionary<string, string>>(result);

                        foreach (var item in banData) Console.WriteLine(item);

                        return true;
                    } else if (response.StatusCode == HttpStatusCode.NotFound) {
                        return false;
                    } else if (response.StatusCode == HttpStatusCode.BadRequest) {
                        Console.WriteLine("Bad request. HWID parameter is not provided.");
                        return true;
                    } else {
                        Console.WriteLine($"HTTP Error: {response.StatusCode}");
                        return true;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }

            return true;
        }

        private static string GetHWID() {
            var processorId = "";

            // Get processor ID
            foreach (ManagementObject mo in new ManagementObjectSearcher("Select ProcessorId From Win32_processor").Get()) {
                processorId = mo["ProcessorId"].ToString();
                break;
            }

            // Get volume _hwid number
            ManagementObject dsk = new(@"win32_logicaldisk.deviceid=""c:""");
            dsk.Get();

            using (SHA256 sha256Hash = SHA256.Create()) {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(processorId + dsk["VolumeSerialNumber"]));
                StringBuilder builder = new();

                for (var i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));

                return builder.ToString();
            }
        }
    }
}
