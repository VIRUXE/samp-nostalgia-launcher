using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NostalgiaAnticheat.Game {
    internal class GameValidation {
        public enum BadFileReason { 
            DoesNotExist,
            FileSizeMismatch,
            LastModifiedTimeMismatch
        }

        public class InvalidExecutableException : Exception {
            public string ExecutableHash { get; }

            public InvalidExecutableException(string hash)
                : base($"Executable is not valid. Hash: {hash}") {
                ExecutableHash = hash;
            }
        }

        public class MissingRequiredFilesException : Exception {
            public int ActualFileCount { get; }
            public int ManifestFileCount { get; }

            public MissingRequiredFilesException(int actualFileCount, int manifestFileCount)
                : base($"Missing required files. Found {actualFileCount}, expected at least {manifestFileCount}.") {
                ActualFileCount = actualFileCount;
                ManifestFileCount = manifestFileCount;
            }
        }

        public static bool IsExecutableValid(string executablePath) {
            if (!File.Exists(executablePath)) return false;

            string[] validHashes = {
                "8C609F108AD737DEFFBD0D17C702F5974D290C4379DE742277B809F80350DA1C",
                "A559AA772FD136379155EFA71F00C47AAD34BBFEAE6196B0FE1047D0645CBD26",
                "403EB9EC0BE348615697363033C1166BBA8220A720D71A87576A6B2737A9B765",
                "f01a00ce950fa40ca1ed59df0e789848c6edcf6405456274965885d0929343ac" // Mosby
            };

            // Convert the byte array to hexadecimal string
            StringBuilder sb = new();
            foreach (var byteValue in SHA256.Create().ComputeHash(File.OpenRead(executablePath))) sb.Append(byteValue.ToString("X2"));

            string computedHash = sb.ToString();

            if (!validHashes.Contains(computedHash, StringComparer.OrdinalIgnoreCase)) throw new InvalidExecutableException(computedHash);

            return true;
        }
    }
}
