using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NostalgiaAnticheat.Game {
    public static class Gameserver {
        public static Network.State LastState { get; private set; } = Network.State.None;
        public static Network.State State { get; private set; } = Network.State.None;

        public static event Action ServerOnline;
        public static event Action ServerOffline;

        public static bool IsOnline => State == Network.State.Online;

        public static async Task Monitor() {
            while (true) {
                State = await SAMP.GetServerState("sv.scavengenostalgia.fun", 7777);

                if (LastState != State && LastState != Network.State.None) {
                    if (State == Network.State.Online) {
                        ServerOnline?.Invoke();
                    } else if (State == Network.State.Offline) {
                        ServerOffline?.Invoke();
                    }
                }

                LastState = State;

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    internal class SAMP {
        public static string GamePath {
            get {
                string path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\SAMP", "gta_sa_exe", null) as string;
                return path?.Replace("\\gta_sa.exe", "");
            }
            set {
                string newValue = Path.Combine(value, "gta_sa.exe");
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\SAMP", "gta_sa_exe", newValue);
            }
        }

        public static string PlayerName {
            get => Registry.GetValue(@"HKEY_CURRENT_USER\Software\SAMP", "PlayerName", null) as string;
            set => Registry.SetValue(@"HKEY_CURRENT_USER\Software\SAMP", "PlayerName", value);
        }

        public static string Version {
            get {
                var dllPath = Path.Combine(Settings.SelectedSAMPInstallation.Location, "samp.dll");

                if (File.Exists(dllPath)) return FileVersionInfo.GetVersionInfo(dllPath).FileVersion?.Replace(", ", ".");

                return null;
            }
        }

        public static bool IsInstalled => GamePath != null;

        public static async Task<bool> Install() {
            try {
                string installerUrl = "http://scavengenostalgia.fun/baixar/sa-mp-0.3.7-R5-1-install.exe";
                string installerName = Path.GetFileName(new Uri(installerUrl).AbsolutePath);
                string installerPath = Path.Combine(Directory.GetCurrentDirectory(), installerName);

                if (!File.Exists(installerPath)) {
                    Console.Write("Downloading SA-MP installer... ");
                    try {
                        byte[] installerBytes = await new HttpClient().GetByteArrayAsync(installerUrl);
                        await File.WriteAllBytesAsync(installerPath, installerBytes);
                        Console.WriteLine("Done.");
                    } catch (Exception downloadEx) {
                        Console.WriteLine("Failed.");
                        Console.WriteLine("Could not download the installer: " + downloadEx.Message);
                        return false;
                    }
                }

                using Process installProcess = new() {
                    StartInfo = new ProcessStartInfo {
                        FileName = installerPath,
                        Verb = "runas"
                    }
                };

                Console.Write("Installing SA-MP... ");

                while (true) {
                    try {
                        installProcess.Start();
                    } catch (Win32Exception) {
                        // Checking if the application is running with admin privileges

                        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) {
                            Console.WriteLine("Failed.");
                            Console.WriteLine(
                                "The application is not running with administrator privileges.\n" +
                                "You need to either install it manually or run the Launcher as Administrator."
                            );

                            // Opening the folder where the installer was downloaded
                            Process.Start("explorer.exe", $"/select,\"{installerPath}\"");

                            while (true) {
                                Console.WriteLine("Press 'Space' to retry if you've installed it or 'Esc' to cancel.");
                                ConsoleKeyInfo key = Console.ReadKey(true);

                                if (key.Key == ConsoleKey.Spacebar) {
                                    if (IsInstalled) {
                                        Console.WriteLine("Installation successful.");
                                        return true;
                                    } else
                                        Console.WriteLine("SA-MP is not installed yet.");
                                } else if (key.Key == ConsoleKey.Escape) {
                                    Console.WriteLine("Installation canceled.");
                                    return false;
                                }
                            }
                        }

                        throw; // Re-throwing the exception if it wasn't due to a lack of admin privileges
                    }

                    installProcess.WaitForExit();

                    if (IsInstalled) {
                        Console.WriteLine("Successful.");
                        return true;
                    } else {
                        Console.WriteLine("Failed.\nInstallation was not successful.");
                        return false;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("Failed.");
                Console.WriteLine($"An error occurred: {ex.Message}");
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

                using UdpClient udpClient = new();
                
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
            } catch { }

            return Network.State.Offline;
        }
    }
}
