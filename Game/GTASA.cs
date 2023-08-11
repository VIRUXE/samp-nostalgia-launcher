using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NostalgiaAnticheat.Game {
    internal class GTASA {

        private static readonly string[] validHashes = {
            "8C609F108AD737DEFFBD0D17C702F5974D290C4379DE742277B809F80350DA1C",
            "A559AA772FD136379155EFA71F00C47AAD34BBFEAE6196B0FE1047D0645CBD26",
            "403EB9EC0BE348615697363033C1166BBA8220A720D71A87576A6B2737A9B765",
            "f01a00ce950fa40ca1ed59df0e789848c6edcf6405456274965885d0929343ac" // EXE do Mosby
        };

        private readonly string[] Files;
        public readonly string Path;
        public Process Process;
        public bool Valid = true;

        public GTASA(string exePath) {
            string gamePath = System.IO.Path.GetDirectoryName(exePath);

            if (!Directory.Exists(gamePath)) throw new DirectoryNotFoundException();

            if (!exePath.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase)) throw new Exception();

            if (!File.Exists(exePath)) {
                Console.WriteLine(Program.SystemLanguage == Language.PT ? "Essa pasta não contem 'gta_sa.exe'." : "That folder doesn't contain a 'gta_sa.exe'.");
                return;
            }

            using (SHA256 sha256Hash = SHA256.Create()) {
                if (!Array.Exists(validHashes, element => element.Equals(GetHash(sha256Hash, exePath), StringComparison.OrdinalIgnoreCase))) {
                    Console.WriteLine(Program.SystemLanguage == Language.PT ? "Executável inválido." : "Invalid Executable.");
                    return;
                }
            }

            Path = gamePath;
            Files = Directory.GetFiles(Path, "*", SearchOption.AllDirectories);

            foreach (Process process in FindGTAProcesses()) {
                try {
                    if (ComparePath(process.MainModule.FileName) == 1) {
                        Process = process;
                        break;
                    }
                } catch { }
            }
        }

        public bool IsResponding => Process.Responding;

        public bool IsRunning => Process != null && GetWindowHandle() != IntPtr.Zero;

        public bool IsFocused {
            get {
                IntPtr windowHandle = GetWindowHandle();

                return IsRunning && windowHandle != IntPtr.Zero && windowHandle == GetForegroundWindow();
            }
        }

        public bool Connected { get; internal set; }

        public static string GetInstallPath() {
            try {
                var path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\GTA San Andreas\Installation", "ExePath", null) as string;

                return path?.Replace("\"", "");
            } catch { }

            return null;
        }

        public static async Task<string> Download() {
            string directoryPath;
            var isDirectoryConfirmed = false;

            do {
                Console.WriteLine(Program.SystemLanguage == Language.PT ? "Por favor, insira um diretório para instalar o GTA:" : "Please enter a directory to install GTA:");
                directoryPath = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(directoryPath)) {
                    Console.WriteLine(Program.SystemLanguage == Language.PT ? "Caminho do diretório inválido." : "Invalid directory path.");
                    continue;
                }

                try {
                    string testFilePath = System.IO.Path.Combine(directoryPath, "tempfile.txt");
                    using (File.Create(testFilePath)) { }
                    File.Delete(testFilePath);
                } catch (UnauthorizedAccessException) {
                    Console.WriteLine(Program.SystemLanguage == Language.PT ? "O diretório não é gravável. Por favor, escolha outro diretório." : "Directory is not writable. Please choose another directory.");
                    continue;
                }

                DriveInfo driveInfo = new(System.IO.Path.GetPathRoot(directoryPath));
                if (driveInfo.AvailableFreeSpace < 574L * 1024 * 1024) {
                    Console.WriteLine(Program.SystemLanguage == Language.PT ? "Não há espaço livre suficiente em disco. Por favor, escolha outro diretório." : "Not enough free disk space. Please choose another directory.");
                    continue;
                }

                Console.WriteLine(Program.SystemLanguage == Language.PT ? $"Você deseja instalar o GTA neste diretório: {directoryPath}? (s/n)" : $"Do you want to install GTA in this directory: {directoryPath}? (y/n)");
                string confirmation = Console.ReadLine();
                if (confirmation.ToLower() == (Program.SystemLanguage == Language.PT ? "s" : "y")) {
                    isDirectoryConfirmed = true;

                    // Create the directory if it doesn't exist
                    if (!Directory.Exists(directoryPath)) {
                        try {
                            Directory.CreateDirectory(directoryPath);
                            Console.WriteLine(Program.SystemLanguage == Language.PT ? "Diretório criado com sucesso." : "Directory successfully created.");
                        } catch (Exception e) {
                            Console.WriteLine(Program.SystemLanguage == Language.PT ? $"Falha ao criar diretório: {e.Message}" : $"Failed to create directory: {e.Message}");
                        }
                    }
                }
            } while (!isDirectoryConfirmed);

            string downloadFilePath = System.IO.Path.Combine(directoryPath, "cleangtasa-small.7z");

            if (File.Exists(downloadFilePath)) {
                Console.WriteLine(Program.SystemLanguage == Language.PT ? "O arquivo já existe, download não é necessário." : "File already exists, no need to download.");
                return null;
            }

            const int totalBlocks = 10;
            const char progressBlock = '█';
            const char emptyBlock = '.';

            using (HttpClient client = new()) {
                using (HttpResponseMessage response = await client.GetAsync("http://www.scavengenostalgia.fun/baixar/cleangtasa-small.7z", HttpCompletionOption.ResponseHeadersRead)) {
                    response.EnsureSuccessStatusCode();

                    long contentLength = response.Content.Headers.ContentLength.GetValueOrDefault(0);
                    if (contentLength == 0) {
                        throw new Exception("The content length could not be determined.");
                    }

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(downloadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)) {
                        var totalRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;

                        do {
                            int read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0) {
                                isMoreToRead = false;
                            } else {
                                await fileStream.WriteAsync(buffer, 0, read);

                                totalRead += read;
                                double progress = (double)totalRead / contentLength;
                                var blocksCount = (int)Math.Round(progress * totalBlocks);
                                string progressBar = new string(progressBlock, blocksCount) + new string(emptyBlock, totalBlocks - blocksCount);

                                Console.CursorLeft = 0;
                                Console.Write($"[{progressBar}] {progress:P1}");
                            }
                        } while (isMoreToRead);
                    }
                }
            }

            return directoryPath;
        }

        public void Verify() {

        }

        public bool Launch() {
            if (IsRunning) {
                Focus();

                return false;
            }
            ProcessStartInfo startInfo = new() {
                FileName = Path + "\\samp.exe",
                Arguments = "sv.scavengenostalgia.fun:7777"
            };

            Process = Process.Start(startInfo);

            return true;
        }

        public int ComparePath(string path) {
            if (string.IsNullOrEmpty(path)) return -1;
            if (string.IsNullOrEmpty(Path)) return -2;

            if (Path.Contains(System.IO.Path.GetDirectoryName(path))) return 1;

            return 0;
        }

        private static string GetHash(HashAlgorithm hashAlgorithm, string input) {
            using (FileStream stream = File.OpenRead(input)) {
                // Compute the hash of the input file
                byte[] hash = hashAlgorithm.ComputeHash(stream);

                // Convert the byte array to hexadecimal string
                StringBuilder sb = new();
                for (var i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("X2"));

                return sb.ToString();
            }
        }

        public int GetFileCount() => Files.Length;

        public IEnumerable<(string FilePath, long Length)> GetFileList() {
            if (Files.Length == 0) return Enumerable.Empty<(string FilePath, long Length)>();

            return Files.Select(file => (System.IO.Path.GetRelativePath(Path, file), new FileInfo(file).Length));
        }

        // System functions for handling Window Focus
        [DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("User32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("User32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /*[DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);*/

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public bool SendKeys(string text) {
            if (Process == null) return false;

            IntPtr gameWindow = GetWindowHandle();

            if (gameWindow != IntPtr.Zero) {
                if (!IsFocused) Focus();

                Keyboard.PressF6();
                Thread.Sleep(100);
                foreach (char ch in text) SendMessage(gameWindow, 0x0102, (IntPtr)ch, IntPtr.Zero);
                Keyboard.PressEnter();

                return true;
            }

            return false;
        }

        public async Task<bool> Close() {
            if (!IsRunning) return false;

            Process.Kill();
            await Process.WaitForExitAsync();
            Process = null;

            return true;
        }

        public bool Focus() {
            if (!IsRunning || IsFocused) return false;

            IntPtr windowHandle = GetWindowHandle();

            if (windowHandle != IntPtr.Zero) {
                ShowWindow(windowHandle, 1);
                SetForegroundWindow(windowHandle);

                return true;
            }

            return false;
        }

        public IntPtr GetWindowHandle() {
            if (Process.MainWindowHandle != IntPtr.Zero) return Process.MainWindowHandle;

            Process newProcess = FindGTAProcesses().FirstOrDefault();

            if (newProcess != null) Process = newProcess;

            return Process.MainWindowHandle;
        }

        public static IEnumerable<Process> FindGTAProcesses() {
            return Process.GetProcesses()
                .Where(p => !p.ProcessName.StartsWith("System") && !p.ProcessName.StartsWith("Idle"))
                .Where(p => {
                    try {
                        string windowTitle = p.MainWindowTitle.ToLower();

                        return windowTitle != string.Empty &&
                               (windowTitle.Contains("gta san andreas") || windowTitle.Contains("gta:sa:mp")) &&
                               p.MainModule.ModuleName.ToLower() == "gta_sa.exe";
                    } catch { }

                    return false;
                });
        }
    }
}
