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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static NostalgiaAnticheat.Game.GameValidation;

namespace NostalgiaAnticheat.Game {
    public class InstallationPath {
        public readonly string SystemPath;

        public bool HasSAMP => (new[] { "samp.exe", "samp.dll" }).All(file => File.Exists(Path.Combine(SystemPath, file)));

        public bool HasExecutable => File.Exists(Path.Combine(SystemPath, GTASA.EXECUTABLE_FILENAME));

        public string[] Files => Directory.GetFiles(SystemPath, "*", SearchOption.AllDirectories);

        public bool Validate() {
            if(!HasExecutable) return false;

            var manifestFileCount = GTASA.CountFilesInManifest();

            if (Files.Length < manifestFileCount) throw new MissingRequiredFilesException(Files.Length, manifestFileCount);

            if (GTASA.Manifest == null) throw new InvalidOperationException("Manifest has not been fetched.");

            var badFiles = new List<(string FilePath, BadFileReason Reason)>();

            static bool ValidateNode(JsonElement node, string currentPath, List<(string FilePath, BadFileReason Reason)> badFilesList) {
                foreach (var property in node.EnumerateObject()) {
                    string newPath = property.Name.Equals("files", StringComparison.OrdinalIgnoreCase) ? currentPath : Path.Combine(currentPath, property.Name);

                    if (property.Name.Equals("files", StringComparison.OrdinalIgnoreCase)) {
                        foreach (var fileNode in property.Value.EnumerateObject()) {
                            string filePath = Path.Combine(newPath, fileNode.Name);

                            if (File.Exists(filePath)) {
                                FileInfo fileInfo = new(filePath);
                                long expectedSize = fileNode.Value.GetProperty("size").GetInt64();
                                long expectedLastModified = fileNode.Value.GetProperty("last_modified").GetInt64();
                                long fileLastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();

                                if (fileInfo.Length != expectedSize) badFilesList.Add((filePath, BadFileReason.FileSizeMismatch));

                                if (fileLastModified != expectedLastModified) badFilesList.Add((filePath, BadFileReason.LastModifiedTimeMismatch));
                            } else
                                badFilesList.Add((filePath, BadFileReason.DoesNotExist));
                        }
                    } else if (property.Value.ValueKind == JsonValueKind.Object)
                        ValidateNode(property.Value, newPath, badFilesList);
                }

                return true;
            }

            try {
                ValidateNode(GTASA.Manifest.RootElement, SystemPath, badFiles);
                return badFiles.Count == 0;
            } catch (Exception ex) {
                Debug.WriteLine(ex.Message);
                return false;
            }

            return true;
        }

        public InstallationPath(string path) {
            path = !string.IsNullOrEmpty(Path.GetExtension(path)) ? Path.GetDirectoryName(path) : path;

            try {
                if (GameValidation.IsExecutableValid(Path.Combine(path, GTASA.EXECUTABLE_FILENAME)))
                    SystemPath = path;
                else
                    throw new Exception("Path doesn't exist.");
            } catch {
                throw;
            }
        }
    }

    public static class GTASA {
        public const string EXECUTABLE_FILENAME = "gta_sa.exe";

        public static Process Process;
        public static bool Monitoring;
        public static event Action GameStarted;
        public static event Action GameExited;

        public static JsonDocument Manifest;

        public static string CurrentInstallationPath { get; private set; }

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

        private static IEnumerable<Process> GetAllGTAProcesses() {
            return Process.GetProcesses()
                .Where(p => !p.ProcessName.StartsWith("System") && !p.ProcessName.StartsWith("Idle"))
                .Where(p => {
                    try {
                        string windowTitle = p.MainWindowTitle.ToLower();
                        return windowTitle != string.Empty &&
                               (windowTitle.Contains("gta san andreas") || windowTitle.Contains("gta:sa:mp")) &&
                               p.MainModule.ModuleName.ToLower() == EXECUTABLE_FILENAME;
                    } catch {
                        return false;
                    }
                });
        }

        /// <summary>
        /// Starts monitoring for the GTA SA processes. Kills any non-legitimate instances of the game and raises events when the game starts or exits.
        /// </summary>
        public static async void MonitorProcesses() {
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
                    if (entry.Name == "files" && entry.Value.ValueKind == JsonValueKind.Object)
                        fileCount += entry.Value.EnumerateObject().Count();
                    else if (entry.Value.ValueKind == JsonValueKind.Object)
                        fileCount += CountFilesInNode(entry.Value);
                }

                return fileCount;
            }

            return CountFilesInNode(Manifest.RootElement);
        }

        public static bool ValidateInstallationAgainstManifest(string installationPath, out List<(string FilePath, string Issue)> badFiles) {
            
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
