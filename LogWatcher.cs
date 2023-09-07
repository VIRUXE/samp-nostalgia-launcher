using System;
using System.IO;
using System.Threading;

namespace NostalgiaAnticheat {
    public static class LogWatcher {
        private static readonly FileSystemWatcher fileSystemWatcher;
        private static long lastReadLength;

        public static event EventHandler OnConnecting;
        public static event EventHandler OnConnected;

        static LogWatcher() {
            fileSystemWatcher = new FileSystemWatcher {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"GTA San Andreas User Files\SAMP"),
                Filter = "chatlog.txt",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            fileSystemWatcher.Changed += OnChanged;
        }

        public static void Start() {
            fileSystemWatcher.EnableRaisingEvents = true;
        }

        public static void Stop() {
            fileSystemWatcher.EnableRaisingEvents = false;
        }

        private static void OnChanged(object sender, FileSystemEventArgs e) {
            // If the file is being written to, we wait a moment to let the write finish
            while(IsFileLocked(new FileInfo(e.FullPath))) Thread.Sleep(1000);

            // Open a filestream on the chatlog file to read it
            using FileStream fileStream = new(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // If the file length is less than the last read length, the file was recreated, reset the last read length
            if(fileStream.Length < lastReadLength) lastReadLength = 0;

            // Seek to the last read position
            fileStream.Seek(lastReadLength, SeekOrigin.Begin);

            using StreamReader reader = new(fileStream);

            string line;

            while((line = reader.ReadLine()) != null) {
                if (string.IsNullOrEmpty(line)) continue; // Ignore empty lines

                if (line.Contains("Connecting to 216.238.113.189:7777")) {
                    OnConnecting?.Invoke(null, EventArgs.Empty);
                } else if (line.Contains("Connected to")) {
                    OnConnected?.Invoke(null, EventArgs.Empty);
                }
            }

            lastReadLength = fileStream.Position;
        }

        private static bool IsFileLocked(FileInfo file) {
            FileStream stream = null;

            try {
                stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            } catch (IOException) {
                return true;
            } finally {
                stream?.Close();
            }

            return false;
        }
    }
}
