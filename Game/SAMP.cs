using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NostalgiaAnticheat.Game {
    public static class Gameserver {
        public static Network.State State { get; private set; } = Network.State.None;
        public static ManualResetEventSlim StateUpdated { get; } = new ManualResetEventSlim(false);

        public static event Action ServerOnline;
        public static event Action ServerOffline;

        public static bool IsOnline => State == Network.State.Online;

        public static async Task Monitor() {
            Network.State previousState = State;

            while (true) {
                State = await SAMP.GetServerState("sv.scavengenostalgia.fun", 7777);

                if (previousState != State && previousState != Network.State.None) {
                    if (State == Network.State.Online) {
                        ServerOnline?.Invoke();
                    } else if (State == Network.State.Offline) {
                        ServerOffline?.Invoke();
                    }
                }

                previousState = State;

                StateUpdated.Set();

                await Task.Delay(TimeSpan.FromSeconds(5));

                StateUpdated.Reset();
            }
        }
    }


    internal class SAMP {

        public static string GetGamePath() => Registry.GetValue(@"HKEY_CURRENT_USER\Software\SAMP", "gta_sa_exe", null) as string;

        public static string GetPlayerNickname() => Registry.GetValue(@"HKEY_CURRENT_USER\Software\SAMP", "PlayerName", null) as string;

        public static void SetPlayerNickname(string nickname) => Registry.SetValue(@"HKEY_CURRENT_USER\Software\SAMP", "PlayerName", nickname);

        public static string GetVersion() {
            string dllPath = Path.Combine(Path.GetDirectoryName(GetGamePath()), "samp.dll");

            if (File.Exists(dllPath)) {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(dllPath);
                return fileVersionInfo.ProductPrivatePart.ToString();
            }

            return null;
        }

        public static async Task<bool> Download(string installPath) {
            string downloadFilePath = Path.Combine(installPath, "sa-mp-0.3.7-R5-1-install.exe");

            if (File.Exists(downloadFilePath)) {
                Console.WriteLine(Program.SystemLanguage == Language.PT ? "O arquivo já existe, não é necessário baixar." : "File already exists, no need to download.");
                return false;
            }

            using (HttpClient client = new()) {
                using (HttpResponseMessage response = await client.GetAsync("http://www.scavengenostalgia.fun/baixar/sa-mp-0.3.7-R5-1-install.exe", HttpCompletionOption.ResponseHeadersRead)) {
                    response.EnsureSuccessStatusCode();

                    long contentLength = response.Content.Headers.ContentLength.GetValueOrDefault(0);
                    if (contentLength == 0) throw new Exception("The content length could not be determined.");

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(downloadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)) {
                        var totalRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;

                        do {
                            int read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0)
                                isMoreToRead = false;
                            else {
                                await fileStream.WriteAsync(buffer, 0, read);

                                totalRead += read;
                                ProgressBar.Display(totalRead, contentLength);
                            }
                        } while (isMoreToRead);
                    }

                    return true;
                }
            }
        }

        public static async Task<bool> Install(string installPath) {
            string installerPath = Path.Combine(installPath, "sa-mp-0.3.7-R5-1-install.exe");

            if (!File.Exists(installerPath)) {
                Console.WriteLine("Installer not found at specified path.");
                return false;
            }

            ProcessStartInfo startInfo = new() {
                FileName = installerPath,
                Arguments = "/S",
                UseShellExecute = false
            };

            try {
                Process proc = Process.Start(startInfo);
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0; // NSIS installers generally return 0 on successful install
            } catch (Exception e) {
                Console.WriteLine($"Installation failed: {e.Message}");
                return false;
            }
        }

        private static Network.State lastOnlineResult = Network.State.None;
        private static DateTime lastCheckTime         = DateTime.MinValue;

        public static async Task<Network.State> GetServerState(string serverAddress, int serverPort, int timeout = 3000) {
            if ((DateTime.UtcNow - lastCheckTime).TotalMinutes < 1) return lastOnlineResult;

            try {
                var ipAddresses = await Dns.GetHostAddressesAsync(serverAddress);

                IPAddress serverIpAddress = ipAddresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ?? throw new InvalidOperationException("No IPv4 address found for the specified hostname.");

                using (UdpClient udpClient = new()) {
                    udpClient.Client.SendTimeout    = timeout;
                    udpClient.Client.ReceiveTimeout = timeout;

                    byte[] requestData     = Encoding.ASCII.GetBytes("SAMP");
                    byte[] serverIpBytes   = serverIpAddress.GetAddressBytes();
                    byte[] serverPortBytes = BitConverter.GetBytes((ushort)serverPort);

                    if (BitConverter.IsLittleEndian) Array.Reverse(serverPortBytes);

                    byte[][] arrays = { requestData, serverIpBytes, serverPortBytes, new byte[] { 0x69 } };

                    using (MemoryStream ms = new()) {
                        foreach (byte[] array in arrays) ms.Write(array, 0, array.Length);
                        requestData = ms.ToArray();
                    }

                    udpClient.Send(requestData, requestData.Length, serverAddress, serverPort);

                    IPEndPoint remoteEndpoint = null;
                    byte[] responseData = udpClient.Receive(ref remoteEndpoint);

                    if (responseData.Length > 10 && Encoding.ASCII.GetString(responseData, 0, 4) == "SAMP") {
                        lastCheckTime = DateTime.UtcNow;

                        return lastOnlineResult = Network.State.Online;
                    }
                }
            } catch { }

            return Network.State.Offline;
        }
    }
}
