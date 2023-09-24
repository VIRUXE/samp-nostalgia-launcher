using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
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
        public static string Path {
            get {
                string path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\SAMP", "gta_sa_exe", null) as string;
                return path?.Replace("\\gta_sa.exe", "");
            }
            set {
                string newValue = System.IO.Path.Combine(value, "gta_sa.exe");
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\SAMP", "gta_sa_exe", newValue);
            }
        }

        public static string PlayerName {
            get => Registry.GetValue(@"HKEY_CURRENT_USER\Software\SAMP", "PlayerName", null) as string;
            set => Registry.SetValue(@"HKEY_CURRENT_USER\Software\SAMP", "PlayerName", value);
        }

        public static string Version {
            get {
                var dllPath = System.IO.Path.Combine(Path, "samp.dll");

                if (File.Exists(dllPath)) return FileVersionInfo.GetVersionInfo(dllPath).FileVersion?.Replace(", ", ".");

                return null;
            }
        }

        public static bool IsInstalled {
            get {
                if (Path == null) return false;

                return (new[] { "samp.exe", "samp.dll" }).All(file => File.Exists(System.IO.Path.Combine(Path, file)));
            }
        }

        public static async Task<bool> Install() {
            try {
                string installerUrl = "http://scavengenostalgia.fun/baixar/sa-mp-0.3.7-R5-1-install.exe";
                string installerName = System.IO.Path.GetFileName(new Uri(installerUrl).AbsolutePath);
                string installerPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), installerName);

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
                        Console.WriteLine("Failed.");
                        Console.WriteLine("Installation was not successful.");
                        return false;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("Failed.");
                Console.WriteLine($"An error occurred: {ex.Message}");
                return false;
            }
        }

        public static async Task ChangeGamePath() {
            while (true) {
                List<string> paths = new(Settings.InstallationPaths);

                if (!paths.Contains(Path)) paths.Insert(0, Path);

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Select or Add a GTA San Andreas Installation InstallationPath:");
                Console.ResetColor();
                for (int i = 0; i < paths.Count; i++) {
                    var path = paths[i];
                    var (pathValid, _) = GTASA.ValidateInstallationPath(path);
                    var displayText = $"{i + 1}. {path}";

                    if (path == Path) displayText += " (Current)";

                    if (!pathValid) {
                        Console.ForegroundColor = ConsoleColor.Red;
                        displayText += " (invalid)";
                    }

                    Console.WriteLine(displayText);
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{paths.Count + 1}. Select an Existing Installation InstallationPath");
                Console.WriteLine($"{paths.Count + 2}. Download and Install a Clean GTA San Andreas");
                Console.ResetColor();

                int choice;
                while (true) {
                    Console.Write("Choose an option: ");

                    var keyInfo = Console.ReadKey();
                    Console.WriteLine();  // Move to next line as ReadKey doesn't do this automatically
                    if (int.TryParse(keyInfo.KeyChar.ToString(), out choice) && choice >= 1 && choice <= paths.Count + 2)
                        break;
                    else
                        Console.WriteLine("Invalid choice, please try again.");
                }
                Console.WriteLine();

                if (choice == paths.Count + 2) {
                    await GTASA.Install();
                } else if (choice == paths.Count + 1) {
                    AskForInstallationPath();
                    break;
                } else {
                    var path = paths[choice - 1];

                    // Check if directory exists
                    if (!Directory.Exists(path)) {
                        Console.WriteLine($"The directory '{path}' no longer exists. Removing it from the list...");
                        Settings.InstallationPaths.Remove(path);
                        Settings.Save();
                        Console.WriteLine("InstallationPath has been removed from the list. Relisting available paths...");
                        continue;  // Continue the while loop to relist paths
                    }

                    var (pathValid, invalidReasons) = GTASA.ValidateInstallationPath(path);

                    if (path == Path) {
                        Console.WriteLine("You have selected the current installation path.");
                    } else { // User selected a different path from what is the current one
                        if (!pathValid) {
                            Console.WriteLine($"This Installation InstallationPath '{path}' is Invalid. Would you like to remove it from the list? (y/n)");
                            char removeChoice = Console.ReadKey().KeyChar;
                            Console.WriteLine();
                            if (removeChoice == 'y' || removeChoice == 'Y') {
                                Settings.InstallationPaths.Remove(path);
                                Settings.Save();
                                Console.WriteLine("InstallationPath has been removed from the list. Relisting available paths...");
                                continue;
                            }
                        }

                        Settings.OldInstallationPath = Path; // Save old path before updating
                        Settings.Save();

                        Path = path;
                        Console.WriteLine($"Game path changed to: {path}");
                    }

                    if (!pathValid && invalidReasons.Count > 0) {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"The path '{path}' is invalid for the following reasons:");
                        foreach (var reason in invalidReasons) Console.WriteLine($"- {reason}");
                        Console.ResetColor();
                    }

                    break;
                }
            }
        }

        public static void AskForInstallationPath() {
            while (true) {
                StringBuilder inputBuilder = new();
                int currentPosition = 0;

                Console.WriteLine("Please enter the installation path (Press ESC to cancel): ");

                while (true) {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

                    if (keyInfo.Key == ConsoleKey.Enter) {
                        break;
                    } else if (keyInfo.Key == ConsoleKey.Escape) {
                        return;
                    } else if (keyInfo.Key == ConsoleKey.Backspace) {
                        if (currentPosition > 0) {
                            inputBuilder.Remove(currentPosition - 1, 1);
                            Console.Write("\b \b");  // Handle backspace visually
                            currentPosition--;
                        }
                    } else if (keyInfo.Key == ConsoleKey.Home) {
                        Console.CursorLeft = 0;
                        currentPosition = 0;
                    } else {
                        inputBuilder.Insert(currentPosition, keyInfo.KeyChar);
                        Console.Write(keyInfo.KeyChar);  // Echo character to console
                        currentPosition++;
                    }
                }

                Console.WriteLine("\n");  // Move to the next line after Enter is pressed

                string installationPath = inputBuilder.ToString();

                if (string.IsNullOrEmpty(installationPath)) {
                    Console.WriteLine("InstallationPath cannot be empty. Please try again.");
                    continue;
                }

                if (File.Exists(System.IO.Path.Combine(installationPath, "gta_sa.exe"))) {
                    Console.WriteLine("Installation InstallationPath added.");
                    //InstallationPath = installationPath;

                    Settings.AddInstallationPath(installationPath);

                    break;
                } else
                    Console.WriteLine("Invalid path. Please try again.");

                Console.WriteLine();
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
