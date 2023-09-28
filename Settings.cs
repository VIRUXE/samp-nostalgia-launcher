using NostalgiaAnticheat.Game;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NostalgiaAnticheat {
    public static class Settings {
        private static string JsonFilePath => Path.Combine(Directory.GetCurrentDirectory(), "nostalgia.json");
        private static readonly SettingsData FileData = new();

        public static List<GameInstallation> GameInstallations { get; private set; } = new List<GameInstallation>();

        public static string PreviousNickname { get; set; }

        public static string? InitialSAMPGamePath { get; set; }

        public static GameInstallation? SelectedGameInstallation {
            get => GameInstallations.FirstOrDefault(gi => gi.Location == FileData.SelectedGameInstallationPath);
            set {
                FileData.SelectedGameInstallationPath = value?.Location;
                Save();
            }
        }

        public static GameInstallation? SelectedSAMPInstallation {
            get => GameInstallations.FirstOrDefault(gi => gi.Location == FileData.SelectedSAMPInstallationPath);
            set {
                FileData.SelectedSAMPInstallationPath = value?.Location;
                Save();
            }
        }

        static Settings() {
            if (File.Exists(JsonFilePath)) {
                try {
                    var json = File.ReadAllText(JsonFilePath);
                    FileData = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();

                    GameInstallations.Clear();

                    foreach (var path in FileData.GameInstallationPaths) {
                        try {
                            GameInstallations.Add(new GameInstallation(path));
                        } catch (Exception ex) {
                            // Log or handle the exception accordingly
                            // For now, just ignoring the invalid path
                        }
                    }
                } catch (Exception ex) {
                    Debug.WriteLine(ex);
                    FileData = new SettingsData();
                    Save();
                }
            } else {
                FileData = new SettingsData();
                Save();
            }
        }

        public static void Save() {
            FileData.GameInstallationPaths = GameInstallations.Select(gi => gi.Location).ToList();
            var json = JsonSerializer.Serialize(FileData);
            File.WriteAllText(JsonFilePath, json);
        }

        private class SettingsData {
            public List<string> GameInstallationPaths { get; set; }
            public string SelectedGameInstallationPath { get; set; } = string.Empty;
            public string SelectedSAMPInstallationPath { get; set; } = string.Empty;

            public SettingsData() {
                GameInstallationPaths = new List<string>();

                if (GTASA.InstallPath != null) {
                    GameInstallationPaths.Add(GTASA.InstallPath);
                    SelectedGameInstallationPath = GTASA.InstallPath;
                }

                if (SAMP.GamePath != null) {
                    if (GTASA.InstallPath != SAMP.GamePath) GameInstallationPaths.Add(SAMP.GamePath);

                    SelectedSAMPInstallationPath = SAMP.GamePath;
                }
            }
        }

    }
}
