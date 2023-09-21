using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NostalgiaAnticheat.Game {
    public static class GTASA {
        public const string EXECUTABLE_NAME = "gta_sa.exe";
        public const int MIN_GAME_FILES = 100;

        public static Process Process;
        public static bool Monitoring;
        public static event Action GameStarted;
        public static event Action GameExited;

        private static JsonDocument _gameManifest;

        public static string CurrentInstallationPath { get; private set; }
        public static bool IsInstalled => InstallPath != null;
        public static string ExecutablePath => IsInstallationValid ? Path.Combine(CurrentInstallationPath, EXECUTABLE_NAME) : null;
        public static bool IsInstallationValid => !string.IsNullOrEmpty(CurrentInstallationPath) && IsInstallationPathValid(CurrentInstallationPath);

        public static async Task FetchManifest() {
            try {
                string json = await new HttpClient().GetStringAsync("https://api.scavengenostalgia.fun/game");
                _gameManifest = JsonDocument.Parse(json);
            } catch { return; }
        }

        public static bool IsInstallationPathValid(string installationPath) {
            string path = !string.IsNullOrEmpty(Path.GetExtension(installationPath)) ? Path.GetDirectoryName(installationPath) : installationPath;

            if (!Directory.Exists(path)) return false;

            if (!IsExecutableValid(Path.Combine(path, EXECUTABLE_NAME))) return false;

            string[] installationFiles = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

            if(installationFiles.Length < CountFilesInManifest()) return false;

            if (!ValidateInstallationAgainstManifest(installationPath, out List<(string FilePath, string Issue)> badFiles)) {
                Debug.WriteLine(string.Join("\n", badFiles.Select(bf => $"{bf.FilePath}: {bf.Issue}")));
                return false;
            }

            return true;
        }

        public static bool SetInstallationPath(string installationPath) {
            string path = installationPath;

            if (Path.HasExtension(installationPath)) path = Path.GetDirectoryName(installationPath);

            if (!IsInstallationPathValid(path)) return false;

            CurrentInstallationPath = path;

            return true;
        }

        // Get all the files in the installation directory
        public static string[] GetFiles => IsInstallationValid ? Directory.GetFiles(CurrentInstallationPath, "*", SearchOption.AllDirectories) : null;

        // Used to store if the game is connected to the gameserver
        public static bool Playing { get; internal set; }

        public static bool IsRunning => Process != null && Process.MainWindowHandle != IntPtr.Zero;

        public static bool IsResponding => IsRunning && Process?.Responding == true;

        public static List<string> Modules => IsRunning ? Process.Modules.Cast<ProcessModule>().Select(m => m.ModuleName).ToList() : null;

        public static bool IsFocused {
            get {
                if (Process == null || Process.HasExited) return false;

                IntPtr windowHandle = Process.MainWindowHandle;
                return windowHandle != IntPtr.Zero && windowHandle == GetForegroundWindow();
            }
        }

        public static string InstallPath => (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\GTA San Andreas\Installation", "ExePath", null) as string)?.Replace("\"", "");
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

            using HttpResponseMessage response = await new HttpClient().GetAsync("http://www.scavengenostalgia.fun/baixar/cleangtasa-small.7z", HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            long contentLength = response.Content.Headers.ContentLength.GetValueOrDefault(0);
            if (contentLength == 0) throw new Exception("The content length could not be determined.");

            using Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(downloadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

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
                    double progress = (double)totalRead / contentLength;
                    var blocksCount = (int)Math.Round(progress * totalBlocks);
                    string progressBar = new string(progressBlock, blocksCount) + new string(emptyBlock, totalBlocks - blocksCount);

                    Console.CursorLeft = 0;
                    Console.Write($"[{progressBar}] {progress:P1}");
                }
            } while (isMoreToRead);

            return directoryPath;
        }

        public static bool Launch() {
            if (IsRunning) {
                Focus();

                return false;
            }

            Process = Process.Start(new ProcessStartInfo() {
                FileName  = CurrentInstallationPath + "\\samp.exe",
                Arguments = "sv.scavengenostalgia.fun:7777"
            });

            return true;
        }

        public static async Task<bool> Close() {
            if (!IsRunning) return false;

            Process.Kill();
            await Process.WaitForExitAsync();
            Process = null;

            return true;
        }

        public static bool Focus() {
            if (!IsRunning || IsFocused) return false;

            IntPtr windowHandle = Process.MainWindowHandle;

            if (windowHandle == IntPtr.Zero) return false;

            ShowWindow(windowHandle, 1);
            SetForegroundWindow(windowHandle);

            return true;
        }

        private static bool IsExecutableValid(string executablePath) {
            if(!File.Exists(executablePath)) return false;

            string[] validHashes = {
                "8C609F108AD737DEFFBD0D17C702F5974D290C4379DE742277B809F80350DA1C",
                "A559AA772FD136379155EFA71F00C47AAD34BBFEAE6196B0FE1047D0645CBD26",
                "403EB9EC0BE348615697363033C1166BBA8220A720D71A87576A6B2737A9B765",
                "f01a00ce950fa40ca1ed59df0e789848c6edcf6405456274965885d0929343ac" // Mosby
            };

            // Convert the byte array to hexadecimal string
            StringBuilder sb = new();
            foreach (var byteValue in SHA256.Create().ComputeHash(File.OpenRead(executablePath))) sb.Append(byteValue.ToString("X2"));

            return validHashes.Contains(sb.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<Process> GetAllGTAProcesses() {
            return Process.GetProcesses()
                .Where(p => !p.ProcessName.StartsWith("System") && !p.ProcessName.StartsWith("Idle"))
                .Where(p => {
                    try {
                        string windowTitle = p.MainWindowTitle.ToLower();
                        return windowTitle != string.Empty &&
                               (windowTitle.Contains("gta san andreas") || windowTitle.Contains("gta:sa:mp")) &&
                               p.MainModule.ModuleName.ToLower() == EXECUTABLE_NAME;
                    } catch {
                        return false;
                    }
                });
        }

        /// <summary>
        /// Starts monitoring for the GTA SA processes. Kills any non-legitimate instances of the game and raises events when the game starts or exits.
        /// </summary>
        public static async void StartMonitoring() {
            if(Monitoring) return;

            Monitoring = true;

            await Task.Run(() => {
                while (true) {
                    if (ExecutablePath == null) continue;

                    // Get a list of all currently running GTA processes
                    // Loop through each GTA process found
                    foreach (var process in GetAllGTAProcesses()) {
                        // If the process path doesn't match our installation path, it's not a legitimate process and we should kill it
                        if (process.MainModule.FileName != ExecutablePath) { 
                            process.Kill();
                            continue;
                        }

                        // If our currently tracked process is null, has exited, or is different from the current process in loop
                        if (Process == null || Process.HasExited || Process.Id != process.Id) {
                            // If we currently don't have a process tracked or it has exited, raise the GameStarted event
                            if (Process == null || Process.HasExited) GameStarted?.Invoke();

                            // Assign the current process in loop as our main GTA process to track
                            Process = process;
                        }
                    }

                    // If we're tracking a process but it has exited, raise the GameExited event and reset the tracked process
                    if (IsRunning && Process.HasExited) {
                        Process = null;
                        GameExited?.Invoke();
                    }

                    // Pause the loop for 1 second before checking again
                    Thread.Sleep(1000);
                }
            });
        }


        public static int CountFilesInManifest() {
            static int CountFilesInNode(JsonElement node) {
                int fileCount = 0;

                foreach (var entry in node.EnumerateObject()) {
                    if (entry.Name == "files" && entry.Value.ValueKind == JsonValueKind.Object) {
                        fileCount += entry.Value.EnumerateObject().Count();
                    } else if (entry.Value.ValueKind == JsonValueKind.Object) {
                        fileCount += CountFilesInNode(entry.Value);
                    }
                }

                return fileCount;
            }

            return CountFilesInNode(_gameManifest.RootElement);
        }

        public static bool ValidateInstallationAgainstManifest(string installationPath, out List<(string FilePath, string Issue)> badFiles) {
            if (_gameManifest == null) throw new InvalidOperationException("Manifest has not been fetched.");

            badFiles = new List<(string FilePath, string Issue)>();

            /*static DateTime UnixTimeStampToDateTime(long unixTimeStamp) {
                DateTime dateTimeValue = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                dateTimeValue = dateTimeValue.AddSeconds(unixTimeStamp).ToLocalTime();
                return dateTimeValue;
            }*/

            static bool ValidateNode(JsonElement node, string currentPath, List<(string FilePath, string Issue)> badFilesList) {
                foreach (var property in node.EnumerateObject()) {
                    string newPath = property.Name.Equals("files", StringComparison.OrdinalIgnoreCase)
                            ? currentPath
                            : Path.Combine(currentPath, property.Name);

                    if (property.Name.Equals("files", StringComparison.OrdinalIgnoreCase)) {
                        foreach (var fileNode in property.Value.EnumerateObject()) {
                            string filePath = Path.Combine(newPath, fileNode.Name);

                            if (File.Exists(filePath)) {
                                FileInfo fileInfo = new(filePath);
                                long expectedSize = fileNode.Value.GetProperty("size").GetInt64();
                                long expectedLastModified = fileNode.Value.GetProperty("last_modified").GetInt64();
                                long fileLastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();

                                if (fileInfo.Length != expectedSize) badFilesList.Add((filePath, "File size mismatch"));

                                if (fileLastModified != expectedLastModified) badFilesList.Add((filePath, $"Last modified time mismatch: {fileLastModified} Expected: {expectedLastModified}"));
                            } else
                                badFilesList.Add((filePath, "File does not exist"));
                        }
                    } else if (property.Value.ValueKind == JsonValueKind.Object)
                        ValidateNode(property.Value, newPath, badFilesList);
                }

                return true;
            }

            try {
                ValidateNode(_gameManifest.RootElement, installationPath, badFiles);
                return badFiles.Count == 0;
            } catch (Exception ex) {
                Debug.WriteLine(ex.Message);
                return false;
            }
        }

        public static async Task<bool> DownloadGameArchive() {
            var archivePath = Path.Combine(Directory.GetCurrentDirectory(), "gtasa.7z");

            try {
                using HttpClient client = new();

                // Get the total size of the file to be downloaded
                var responseHead = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "http://scavengenostalgia.fun/baixar/gtasa.7z"));
                var totalBytes = responseHead.Content.Headers.ContentLength.GetValueOrDefault();

                var fileInfo = new FileInfo(archivePath);
                long startRange = 0;
                if (fileInfo.Exists) {
                    startRange = fileInfo.Length;

                    if (startRange >= totalBytes) {
                        Console.WriteLine("Game archive already downloaded");
                        return true;
                    }
                }

                CancellationTokenSource cts = new();

                Task keyListenerTask = Task.Run(() => {
                    while (true) {
                        if (Console.KeyAvailable) {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Escape) {
                                cts.Cancel();
                                break;
                            }
                        }
                    }
                });

                // Specify the starting range in the header
                client.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(startRange, null);

                using var response = await client.GetAsync("http://scavengenostalgia.fun/baixar/gtasa.7z", HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (!response.IsSuccessStatusCode) {
                    Console.WriteLine("Error: Unable to download the Game archive");
                    return false;
                }

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(archivePath, FileMode.Append, FileAccess.Write, FileShare.None, 8192, true);
                var totalReadBytes = startRange;
                var buffer = new byte[8192];
                var isMoreToRead = true;

                do {
                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (read == 0) {
                        isMoreToRead = false;
                    } else {
                        await fileStream.WriteAsync(buffer, 0, read, cts.Token);

                        totalReadBytes += read;
                        var percentage = totalReadBytes * 100 / totalBytes;

                        Console.SetCursorPosition(0, Console.CursorTop);
                        Console.Write($"{percentage}% downloaded");
                    }
                }
                while (isMoreToRead && !cts.Token.IsCancellationRequested);

                if (cts.Token.IsCancellationRequested) {
                    Console.WriteLine("Download cancelled.");
                    return false;
                }

                return true;
            } catch (OperationCanceledException) {
                Console.WriteLine("Download cancelled by user.");
                return false;
            } catch (Exception) {
                Console.WriteLine("An error occurred while downloading the game archive.");
                return false;
            }
        }

        public static bool DecompressGameArchive(string installPath) {
            var archivePath = Path.Combine(Directory.GetCurrentDirectory(), "gtasa.7z");

            if (!File.Exists(archivePath)) {
                Console.WriteLine("The archive file does not exist.");
                return false;
            }

            try {
                if (!Directory.Exists(installPath)) Directory.CreateDirectory(installPath);

                Console.WriteLine("Decompressing GTASA:");

                if (OS.Is7ZipInstalled()) {
                    var processStartInfo = new ProcessStartInfo {
                        FileName = "7z",
                        Arguments = $"x {archivePath} -o{installPath} -y",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = false,
                    };

                    var process = Process.Start(processStartInfo);

                    process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);

                    process.BeginOutputReadLine(); // Start reading from the standard output
                    process.WaitForExit();

                    Console.WriteLine(process.ExitCode == 0 ? "\nDecompression completed." : "\nDecompression failed.");

                    return process.ExitCode == 0;
                } else {
                    using var archive = SharpCompress.Archives.SevenZip.SevenZipArchive.Open(archivePath);
                    int totalEntries = archive.Entries.Count;
                    int entryIndex = 0;
                    int dotCount = 0;
                    string currentFileName = string.Empty;

                    Timer dotTimer = new(_ => {
                        dotCount = (dotCount + 1) % 4;
                        Console.SetCursorPosition(0, Console.CursorTop);
                        Console.Write(new string(' ', Console.WindowWidth - 1));
                        Console.SetCursorPosition(0, Console.CursorTop);

                        double progress = ((double)entryIndex / totalEntries) * 100;
                        Console.Write($"Decompressing: {currentFileName} ({progress:0.00}%) {new string('.', dotCount)}");
                    }, null, 0, 500);

                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory)) {
                        currentFileName = entry.Key;
                        entry.WriteToDirectory(installPath, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });

                        entryIndex++;
                    }

                    dotTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    dotTimer.Dispose();

                    Console.WriteLine("\nDecompression completed.");
                    return true;
                }
            } catch (OperationCanceledException) {
                Console.WriteLine("\nDecompression was cancelled.");
                return false;
            } catch (Exception ex) {
                Console.WriteLine($"\nDecompression failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> Install() {
            Console.Write("Enter the path where you want to install the game: ");
            string installPath = Console.ReadLine();

            if (await DownloadGameArchive()) {
                if (DecompressGameArchive(installPath)) {
                    Console.WriteLine("Game downloaded and installed successfully.");
                } else {
                    Console.WriteLine("Failed to decompress the game archive.");
                    return false;
                }
            } else {
                Console.WriteLine("Failed to download the game archive.");
                return false;
            }

            return true;
        }

        [DllImport("User32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("User32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
