using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NostalgiaAnticheat {
    internal class Player {
        private readonly HttpClient httpClient = new();
        public string Nickname;
        public string Serial;
        private string Token;

        public Player() {
            Serial = GetHWID();

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NostalgiaLauncher");
        }

        public bool LoggedIn => !string.IsNullOrEmpty(Token);

        public async Task<bool> IsHwBanned() {
            try {
                using (HttpClient client = new()) {
                    HttpResponseMessage response = await client.GetAsync($"https://api.scavengenostalgia.fun/hwid?hwid={Serial}");

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

        public async Task<Dictionary<string, string>> Login(string nickname, string password) {
            try {
                using HttpResponseMessage response = await httpClient.PostAsync("https://api.scavengenostalgia.fun/login", new StringContent(JsonSerializer.Serialize(new {
                    nickname,
                    password,
                    serial    = Serial,
                    version   = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }), Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode) {
                    string result = await response.Content.ReadAsStringAsync();

                    var resultData = JsonSerializer.Deserialize<Dictionary<string, string>>(result);

                    if (resultData != null && resultData.ContainsKey("token")) {
                        Token    = resultData["token"];
                        Nickname = nickname; // ? It may be different for some reason?

                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

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

        public async Task<bool> Logout() {
            if (!LoggedIn) return false;

            Token    = null;
            Nickname = null;

            try {
                using HttpResponseMessage response = await httpClient.PostAsync("https://api.scavengenostalgia.fun/logout", null);

                httpClient.DefaultRequestHeaders.Authorization = null;

                if (response.IsSuccessStatusCode) return true; 
            } catch {}

            return false;
        }

        private static string GetHWID() {
            var processorId = "";

            // Get processor ID
            foreach (ManagementObject mo in new ManagementObjectSearcher("Select ProcessorId From Win32_processor").Get()) {
                processorId = mo["ProcessorId"].ToString();
                break;
            }

            // Get volume serial number
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
