using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NostalgiaAnticheat {
    internal class Server {
        public static async Task<bool> Available(int timeout = 3000) {
            using (TcpClient tcpClient = new()) {
                try {
                    Task connectTask = tcpClient.ConnectAsync("sv.scavengenostalgia.fun", 80);
                    Task completedTask = await Task.WhenAny(connectTask, Task.Delay(timeout));

                    return completedTask == connectTask && tcpClient.Connected;
                } catch {
                    return false;
                }
            }
        }

        public static async Task<bool> SendFiles(string nickname, IEnumerable<(string Path, long Length)> files) {
            using HttpClient client = new();

            var jsonFiles = new Dictionary<string, object>();

            foreach ((string Path, long Length) file in files) {
                string[] filePathParts = file.Path.Split(Path.DirectorySeparatorChar);
                var dict = jsonFiles;

                for (var i = 0; i < filePathParts.Length - 1; i++) {
                    string key = filePathParts[i];

                    if (!dict.ContainsKey(key)) dict.Add(key, new Dictionary<string, object>());

                    dict = (Dictionary<string, object>)dict[key];
                }

                string fileName = filePathParts.Last();
                dict[fileName] = file.Length;
            }

            string jsonContent = JsonSerializer.Serialize(new { nickname, files = jsonFiles });

            StringContent content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.PostAsync("https://webhook.site/b8000aeb-624a-4f80-bb76-8cca75011512", content);

            return response.IsSuccessStatusCode;
        }
    }
}
