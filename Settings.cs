using NostalgiaAnticheat.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace NostalgiaAnticheat {
    public static class Settings {
        private static string _filePath => Path.Combine(Directory.GetCurrentDirectory(), "nostalgia.json");
        private static Data _data = new();

        public static List<string> InstallationPaths {
            get => _data.InstallationPaths;
        }

        public static string OldNickname {
            get => _data.OldNickname;
            set {
                _data.OldNickname = value;
                Save();
            }
        }

        static Settings() {
            Load();
        }

        public static void AddInstallationPath(string newPath) {
            if (!string.IsNullOrWhiteSpace(newPath) && !InstallationPaths.Contains(newPath) && File.Exists(Path.Combine(newPath, "gta_sa.exe"))) {
                InstallationPaths.Add(newPath);
                Save();
            } else {
                throw new ArgumentException("Invalid or duplicate installation path");
            }
        }

        private static void Load() {
            if (File.Exists(_filePath)) {
                var json = File.ReadAllText(_filePath);
                _data = JsonSerializer.Deserialize<Data>(json) ?? new Data();

                // Only keep the valid paths
                //_data.InstallationPaths = _data.InstallationPaths.Where(path => GTASA.ValidateInstallationPath(path).Valid).ToList();
            } else {
                _data = new Data();
                Save();
            }
        }

        public static void Save() {
            var json = JsonSerializer.Serialize(_data);
            File.WriteAllText(_filePath, json);
        }

        private class Data {
            public List<string> InstallationPaths { get; set; } = new List<string>();
            public string OldNickname { get; set; }
        }
    }
}
