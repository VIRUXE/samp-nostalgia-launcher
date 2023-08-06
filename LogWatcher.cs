using System;
using System.IO;
using System.Threading;

namespace NostalgiaAnticheat {

    public class LogWatcher {
        private readonly FileSystemWatcher fileSystemWatcher;
        private long lastReadLength;

        public LogWatcher() {
            fileSystemWatcher = new FileSystemWatcher {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"GTA San Andreas User Files\SAMP"),
                Filter = "chatlog.txt",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            fileSystemWatcher.Changed += OnChanged;
        }

        public event EventHandler OnConnecting;
        public event EventHandler OnConnected;

        public void Start() {
            fileSystemWatcher.EnableRaisingEvents = true;
        }

        public void Stop() {
            fileSystemWatcher.EnableRaisingEvents = false;
        }

        private void OnChanged(object sender, FileSystemEventArgs e) {
            // If the file is being written to, we wait a moment to let the write finish
            while (IsFileLocked(new FileInfo(e.FullPath))) Thread.Sleep(1000);

            using (FileStream fileStream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                // If the file length is less than the last read length, the file was recreated, reset the last read length
                if (fileStream.Length < lastReadLength) lastReadLength = 0;

                // Seek to the last read position
                fileStream.Seek(lastReadLength, SeekOrigin.Begin);

                using (StreamReader reader = new StreamReader(fileStream)) {
                    string line;

                    while ((line = reader.ReadLine()) != null) {
                        if (string.IsNullOrEmpty(line)) continue;

                        /*if (line.Contains("Connecting to 216.238.113.189:7777")) {
                            OnLineMatched?.Invoke(this, new LineMatchedEventArgs { Line = line });
                            break;
                        }*/

                        // Trigger the Connecting event
                        if (line.Contains("Connecting to 216.238.113.189:7777")) {
                            OnConnecting?.Invoke(this, EventArgs.Empty);
                        }

                        // Trigger the Connected event
                        if (line.Contains("Connected to")) {
                            OnConnected?.Invoke(this, EventArgs.Empty);
                        }
                    }

                    lastReadLength = fileStream.Position;
                }
            }
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
