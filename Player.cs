using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    #region Ping Response
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
    #endregion

    public static class Player {
        private const string API_ADDR = "https://api.scavengenostalgia.fun";

        public record struct PingData(IEnumerable<(string FileName, string WindowTitle)> openWindows, List<string> modules);

        private static readonly HttpClient _httpClient = new();
        private static Timer _pinger;
        private static readonly string _hwid;
        private static readonly string _gpci;
        public static bool LoggedIn => !string.IsNullOrEmpty(Nickname);
        public static string Nickname { get; private set; }

        static Player() {
            _gpci = gpci();
            _hwid = OS.GenerateHWID();

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NostalgiaLauncher");
        }

        private static async void OnPingEvent(object state) {
            if (!GTASA.IsRunning) return;

            try {
                var response = await _httpClient.PostAsJsonAsync(API_ADDR + "/player/ping", new PingData(OS.GetOpenWindows(), new List<string> { }));

                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode) {
                    if (!string.IsNullOrEmpty(responseContent)) {
                        var jsonResponse = JsonSerializer.Deserialize<PingResponse>(responseContent);

                        if (jsonResponse.Message != null)
                            await Logout(jsonResponse.Message);
                        else {
                            foreach (var action in jsonResponse.Actions) {
                                switch (action.Type) {
                                    case ResponseType.Screenshot:
                                        // Handle ActionType1 targeting action.Target
                                        break;
                                        // ... Add other cases as needed
                                }
                            }
                        }
                    }
                } else {
                    string errorMessage = $"[HEARTBEAT] HTTP Error: {response.StatusCode}";

                    try {
                        var jsonResponse = JsonSerializer.Deserialize<dynamic>(responseContent);

                        if (jsonResponse?.error != null) errorMessage += $", Error: {jsonResponse.error}";
                    } catch (JsonException) {
                        // The response was not JSON, no additional error message to add
                    }
                    Console.WriteLine(errorMessage);
                }
            } catch (HttpRequestException e) {
                Console.WriteLine($"[HEARTBEAT] HTTP Request Error: {e.Message}");
            } catch (JsonException e) {
                Console.WriteLine($"[HEARTBEAT] JSON Deserialization Error: {e.Message}");
            } catch (Exception e) {
                Console.WriteLine($"[HEARTBEAT] Unexpected Error: {e.Message}");
            }

            return;
        }

        public static async Task<Dictionary<string, string>> Login(string nickname, string password) {
            try {
                using HttpResponseMessage response = await _httpClient.PostAsync(API_ADDR + "/player/login", new StringContent(JsonSerializer.Serialize(new {
                    nickname,
                    password,
                    serial = _hwid,
                    gpci = _gpci,
                    version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }), Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode) {
                    string result = await response.Content.ReadAsStringAsync();

                    var resultData = JsonSerializer.Deserialize<Dictionary<string, string>>(result);

                    if (resultData != null && resultData.ContainsKey("token")) {
                        Nickname = nickname; // ? It may be different for some reason?

                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resultData["token"]);

                        resultData.Remove("token");

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

        public static async Task<bool> Logout(string message = null) {
            if (!LoggedIn) return false;

            _pinger?.Dispose();

            Nickname = null;

            if (!string.IsNullOrEmpty(message)) Console.WriteLine($"Logout Message: {message}");

            try {
                using HttpResponseMessage response = await _httpClient.PostAsync(API_ADDR + "/player/logout", null);

                _httpClient.DefaultRequestHeaders.Authorization = null;

                if (response.IsSuccessStatusCode) return true;
            } catch { }

            return false;
        }

        public static async Task<bool?> IsHwBanned() {
            try {
                HttpResponseMessage response = await _httpClient.GetAsync(API_ADDR + $"/hwid/{_hwid}");

                if (response.StatusCode == HttpStatusCode.Forbidden) {
                    string result = await response.Content.ReadAsStringAsync();

                    var banData = JsonSerializer.Deserialize<Dictionary<string, string>>(result);

                    foreach (var item in banData) Console.WriteLine(item);

                    return true;
                } else if (response.StatusCode == HttpStatusCode.NotFound) {
                    return false;
                }
            } catch { }

            return null;
        }

        private static string gpci() {
            byte[] GPCI_SBOX = {
                0,   4,   8,   12,  4,   5,   9,   13,  8,   9,   10,  14,  12,  13,  14,  15,
                64,  68,  72,  76,  68,  69,  73,  77,  72,  73,  74,  78,  76,  77,  78,  79,
                128, 132, 136, 140, 132, 133, 137, 141, 136, 137, 138, 142, 140, 141, 142, 143,
                192, 196, 200, 204, 196, 197, 201, 205, 200, 201, 202, 206, 204, 205, 206, 207,
                64,  68,  72,  76,  68,  69,  73,  77,  72,  73,  74,  78,  76,  77,  78,  79,
                80,  84,  88,  92,  84,  85,  89,  93,  88,  89,  90,  94,  92,  93,  94,  95,
                144, 148, 152, 156, 148, 149, 153, 157, 152, 153, 154, 158, 156, 157, 158, 159,
                208, 212, 216, 220, 212, 213, 217, 221, 216, 217, 218, 222, 220, 221, 222, 223,
                128, 132, 136, 140, 132, 133, 137, 141, 136, 137, 138, 142, 140, 141, 142, 143,
                144, 148, 152, 156, 148, 149, 153, 157, 152, 153, 154, 158, 156, 157, 158, 159,
                160, 164, 168, 172, 164, 165, 169, 173, 168, 169, 170, 174, 172, 173, 174, 175,
                224, 228, 232, 236, 228, 229, 233, 237, 232, 233, 234, 238, 236, 237, 238, 239,
                192, 196, 200, 204, 196, 197, 201, 205, 200, 201, 202, 206, 204, 205, 206, 207,
                208, 212, 216, 220, 212, 213, 217, 221, 216, 217, 218, 222, 220, 221, 222, 223,
                224, 228, 232, 236, 228, 229, 233, 237, 232, 233, 234, 238, 236, 237, 238, 239,
                240, 244, 248, 252, 244, 245, 249, 253, 248, 249, 250, 254, 252, 253, 254, 255
            };

            string userHomePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string gtaSanAndreasUserFiles = Path.Combine(userHomePath, "GTA San Andreas User Files");

            byte[] hash = new SHA1Managed().ComputeHash(Encoding.UTF8.GetBytes(gtaSanAndreasUserFiles[4..]));
            byte[] swappedHash = Enumerable.Range(0, hash.Length / 4).SelectMany(i => hash.Skip(i * 4).Take(4).Reverse()).ToArray();
            byte[] byteArray = swappedHash.Select(b => GPCI_SBOX[b]).ToArray();

            return BitConverter.ToString(byteArray).Replace("-", "").TrimStart('0').ToUpper();
        }
    }
}