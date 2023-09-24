using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;

namespace NostalgiaAnticheat {
    public static class Updater {
        public record VersionInfo {
            [JsonPropertyName("major")]
            public int Major { get; init; }

            [JsonPropertyName("minor")]
            public int Minor { get; init; }

            [JsonPropertyName("patch")]
            public int Patch { get; init; }

            [JsonPropertyName("additional")]
            public string Additional { get; init; }

            public VersionInfo(int major, int minor, int patch, string additional) => (Major, Minor, Patch, Additional) = (major, minor, patch, additional);

            public override string ToString() {
                return $"{Major}.{Minor}.{Patch} ({Additional})";
            }
        }

        public record UpdateInfo {
            [JsonPropertyName("version")]
            public VersionInfo Version { get; init; }

            [JsonPropertyName("release_notes")]
            public string ReleaseNotes { get; init; }

            [JsonPropertyName("hash")]
            public object Hash { get; init; }

            [JsonPropertyName("url")]
            public string Url { get; init; }
        }

        //public static VersionInfo CurrentVersion { get; } = new(0,1,0, "0");
        public static VersionInfo CurrentVersion {
            get {
                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                return new VersionInfo(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build, assemblyVersion.Revision.ToString());
            }
        }

        public static bool ShowReleaseNotes() {
            if (File.Exists("tempReleaseNotes.txt")) {
                string releaseNotes = File.ReadAllText("tempReleaseNotes.txt");
                Console.WriteLine($"Updated! Release notes: {releaseNotes}\n");
                File.Delete("tempReleaseNotes.txt");
                File.Delete("updater.bat");

                return true;
            }

            return false;
        }

        public static async Task<bool> CheckAndUpdate() {
            Console.Write("Checking for Updates... ");

            try {
                UpdateInfo? updateInfo = await CheckForUpdates();

                if (updateInfo != null) {
                    Console.Write($"Updating to version: {updateInfo.Version}. ");
                    return await SelfUpdate(updateInfo);
                } else {
                    Console.WriteLine("You are using the latest version.\n");
                    return true;
                }
            } catch {
                return false;
            }
        }

        private static async Task<UpdateInfo?> CheckForUpdates() {
            try {
                string json = await new HttpClient().GetStringAsync($"{Program.API_ADDR}/launcher/manifest");
                UpdateInfo updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json) ?? throw new InvalidOperationException("Failed to deserialize.");

                if (IsUpdateAvailable(updateInfo.Version)) return updateInfo;
            } catch (Exception ex) {
                Console.WriteLine($"Failed to check for updates: {ex.Message}");
                throw;  // Re-throw the exception to be caught in the calling method
            }

            return null;
        }

        private static bool IsUpdateAvailable(VersionInfo available) {
            if (available.Major > CurrentVersion.Major) return true;
            if (available.Major == CurrentVersion.Major && available.Minor > CurrentVersion.Minor) return true;
            if (available.Major == CurrentVersion.Major && available.Minor == CurrentVersion.Minor && available.Patch > CurrentVersion.Patch) return true;
            // Additional logic for 'Additional' if needed
            return false;
        }

        private static async Task<bool> SelfUpdate(UpdateInfo updateInfo) {
            try {
                // Step 1: Download the new version
                var request = new HttpRequestMessage(HttpMethod.Get, updateInfo.Url);
                var response = await new HttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var contentLength = response.Content.Headers.ContentLength.GetValueOrDefault(0L);

                Console.WriteLine("Downloading update...");

                using var downloadStream = await response.Content.ReadAsStreamAsync();
                using MemoryStream memoryStream = new();

                var buffer = new byte[8192];
                var totalBytesRead = 0L;
                var lastBytesRead = 0L;
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                while (true) {
                    var bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0) break;

                    await memoryStream.WriteAsync(buffer, 0, bytesRead);

                    totalBytesRead += bytesRead;

                    if (stopwatch.ElapsedMilliseconds >= 1000) {  // Update speed every second
                        var speed = (totalBytesRead - lastBytesRead) / 1024.0 / 1024.0;  // in MB
                        var progress = (double)totalBytesRead / contentLength * 100;
                        var output = $"Progress: {progress:F2}%   Speed: {speed:F2} MB/s";
                        var padding = new string(' ', Console.WindowWidth - output.Length - 1);  // -1 for the carriage return
                        Console.Write($"\r{output}{padding}");

                        lastBytesRead = totalBytesRead;
                        stopwatch.Restart();
                    }
                }
                Console.WriteLine();

                byte[] newVersionBytes = memoryStream.ToArray();

                // Step 2: Verify hash
                var hashBytes = SHA1.Create().ComputeHash(newVersionBytes);
                var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                if (updateInfo.Hash is string hash) {
                    if (hash != hashString) {
                        Console.WriteLine("Hash verification failed.");
                        return false;
                    }
                } else if (updateInfo.Hash is bool && !(bool)updateInfo.Hash) {
                    Console.WriteLine("File does not exist or hash not available.");
                    return false;
                }

                // Step 3: Replace the current executable
                var tempPath = Path.GetTempFileName();
                await File.WriteAllBytesAsync(tempPath, newVersionBytes);

                File.WriteAllText("tempReleaseNotes.txt", updateInfo.ReleaseNotes);

                // Generate updater batch file
                var updaterBatchContent = new StringBuilder()
                   .AppendLine("@echo off")
                   .AppendLine("title Updating Nostalgia Launcher")
                   .AppendLine("echo Starting the update process...")
                   .AppendLine("echo Replacing the old version with the new one...")
                   .AppendLine($"move /y \"{tempPath}\" \"{Environment.ProcessPath}\"")
                   .AppendLine("echo Update complete!")
                   .AppendLine($"start \"\" \"{Environment.ProcessPath}\"")
                   .ToString();

                var updaterBatchPath = Path.Combine(Path.GetTempPath(), "updater.bat");
                await File.WriteAllTextAsync(updaterBatchPath, updaterBatchContent);

                // Launch the updater batch file and close this application
                Process.Start(new ProcessStartInfo {
                    FileName = updaterBatchPath,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                });

                Environment.Exit(0);

                return true;
            } catch (Exception e) {
                Console.WriteLine($"Failed to update: {e.Message}");
                return false;
            }
        }
    }
}