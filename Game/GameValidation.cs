using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NostalgiaAnticheat.Game {
    public class GameValidation {
        public static JsonDocument Manifest;

        public enum ValidationResult {
            Valid,
            MissingExecutable,
            MissingRequiredFiles,
            ManifestNotFetched,
            InvalidFiles,
            InvalidExecutable
        }

        public enum InvalidFileReason { 
            DoesNotExist,
            FileSizeMismatch,
            LastModifiedTimeMismatch,
            NoAccess,
            UnknownError
        }

        public static string GetReadableValidationResult(ValidationResult validationResult) => validationResult switch {
            ValidationResult.Valid => "The installation is valid.",
            ValidationResult.MissingExecutable => "The executable file is missing.",
            ValidationResult.InvalidExecutable => "The executable file is invalid.",
            ValidationResult.ManifestNotFetched => "The manifest could not be fetched.",
            ValidationResult.MissingRequiredFiles => "Required files are missing.",
            ValidationResult.InvalidFiles => "Some files are invalid.",
            _ => "Unknown reason."
        };

        public static (bool Valid, string Hash) IsExecutableValid(string executablePath) {
            if (!File.Exists(executablePath)) return (false, null);

            StringBuilder sb = new();
            foreach (var byteValue in SHA256.Create().ComputeHash(File.OpenRead(executablePath))) sb.Append(byteValue.ToString("X2"));

            string computedHash = sb.ToString();

            return (new string[] {
                "8C609F108AD737DEFFBD0D17C702F5974D290C4379DE742277B809F80350DA1C",
                "A559AA772FD136379155EFA71F00C47AAD34BBFEAE6196B0FE1047D0645CBD26", // HOODLUM
                "403EB9EC0BE348615697363033C1166BBA8220A720D71A87576A6B2737A9B765",
                "f01a00ce950fa40ca1ed59df0e789848c6edcf6405456274965885d0929343ac" // Mosby
            }.Contains(computedHash, StringComparer.OrdinalIgnoreCase), computedHash);
        }

        public static async Task FetchManifest() {
            try {
                string json = await new HttpClient().GetStringAsync("https://api.scavengenostalgia.fun/game");
                Manifest = JsonDocument.Parse(json);
            } catch { return; }
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

        public static (long Size, long LastModified) GetFileInfoFromManifest(string relativePath) {
            try {
                string[] pathParts = relativePath.Split('\\');
                JsonElement currentNode = Manifest.RootElement;

                foreach (var part in pathParts) {
                    if (currentNode.TryGetProperty("files", out JsonElement filesNode)) {
                        if (filesNode.TryGetProperty(part, out JsonElement fileNode)) {
                            currentNode = fileNode;
                            break; // File found, exit loop
                        }
                    }

                    if (!currentNode.TryGetProperty(part, out currentNode)) {
                        throw new Exception($"Invalid path in manifest: {part}");
                    }
                }

                if (!currentNode.TryGetProperty("size", out JsonElement sizeElement) ||
                    !currentNode.TryGetProperty("last_modified", out JsonElement lastModifiedElement)) {
                    throw new Exception("Size or Last Modified not found in manifest.");
                }

                return (sizeElement.GetInt64(), lastModifiedElement.GetInt64());
            } catch (Exception e) {
                // Handle exception or re-throw, depending on your needs
                throw new Exception($"Failed to get file info from manifest: {e.Message}");
            }
        }


    }
}
