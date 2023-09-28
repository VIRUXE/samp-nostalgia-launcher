using NostalgiaAnticheat.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static NostalgiaAnticheat.Game.GameValidation;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;

namespace NostalgiaAnticheat {
    internal enum Language { EN, PT }

    internal class Program {
        public const string API_ADDR = "https://api.scavengenostalgia.fun";
        public static Language SystemLanguage = Language.EN;

        public static string DisplayMessage(string ptMessage, string enMessage = null, ConsoleColor color = ConsoleColor.Gray, bool newLine = true, bool isAction = false) {
            string message = enMessage != null ? SystemLanguage == Language.PT ? ptMessage : enMessage : ptMessage;

            if (isAction) {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("-> ");
            }

            Console.ForegroundColor = color;

            if (newLine) {
                Console.WriteLine(message);
            } else {
                Console.Write(message);
            }

            Console.ResetColor();

            return message;
        }

        private static async void LogWatcher_OnConnected(object sender, EventArgs e) {
            do await Task.Delay(1000); while (!GTASA.IsResponding);

            GTASA.Playing = true;

            DisplayMessage("Conexão ao servidor de jogo estabelecida.", "Connected to the game server.", ConsoleColor.Green, true, true);

            Menu.DisplayOptions();
        }

        private static void LogWatcher_OnDisconnected(object sender, EventArgs e) {
            GTASA.Playing = false;

            DisplayMessage("Conexão com o servidor foi terminada.", "Connection to the game server was terminated.", ConsoleColor.Green, true, true);

            Menu.DisplayOptions();
        }

        public static async Task Main() {

            Console.Title = "Scavenge Nostalgia";

            if (!new Mutex(false, "NostalgiaLauncherMutex").WaitOne(0, false)) return;

            if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToUpper() == Language.PT.ToString()) SystemLanguage = Language.PT;

            DisplayMessage("Launcher do Scavenge Nostalgia", "Scavenge Nostalgia Launcher", ConsoleColor.White, false);
            Console.WriteLine($" - {Assembly.GetExecutingAssembly().GetName().Version}\n");

            /*if (!Updater.ShowReleaseNotes()) {
                if (!await Updater.CheckAndUpdate()) {
                    Console.WriteLine("Unable to continue. Press any key to exit.");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
            }*/

           TaskCompletionSource<object> tcs = new();

            Gameserver.ServerOnline += () => {
                if (Console.CursorLeft > 0) Console.WriteLine();
                DisplayMessage("Servidor ficou Online.", "Server is now Online.", ConsoleColor.Green, true, true);
                tcs.TrySetResult(null);  // Set the task as complete when the server comes back online
                Menu.DisplayOptions();
            };

            Gameserver.ServerOffline += () => {
                if (Console.CursorLeft > 0) Console.WriteLine();
                DisplayMessage("Servidor está Offline. Aguardando que volte...", "Server is Offline. Waiting for it to come back...", ConsoleColor.Red, true, true);
            };

            _ = Gameserver.Monitor(); // Start monitoring our gameserver

            while(Gameserver.LastState == Network.State.None) { // This is the first state check
                if(Gameserver.State == Network.State.Offline) {
                    DisplayMessage("O Servidor de Jogo está Offline. Vamos aguardar antes de continuar...", "The Game Server is Offline. Let's wait for it to come back...", ConsoleColor.Red, true, true);

                    await tcs.Task;
                }
            }

            // By now the API should be operational right?
            /*bool? isBanned = await Player.IsHwBanned();

            if (isBanned == true) {
                DisplayMessage("Banido.", "Banned.", ConsoleColor.Red, true, true);
                Console.ReadKey();
                Environment.Exit(0);
            } else if (isBanned == null) {
                DisplayMessage("Não foi possível verificar contactar o servidor.", "Could not establish a connection with the server.", ConsoleColor.Yellow, false, true);
                Console.ReadKey();
                Environment.Exit(0);
            }*/

            // Logout Player on app exit
            AppDomain.CurrentDomain.ProcessExit += async (s, e) => {
                if(Settings.InitialSAMPGamePath != null) SAMP.GamePath = Settings.InitialSAMPGamePath;
                await Player.Logout();
            };

            if (!GTASA.IsInstalled) {
                Console.WriteLine("GTA San Andreas is not installed. Press space to install or Esc to exit.");

                var keyInfo = Console.ReadKey();
                if (keyInfo.Key == ConsoleKey.Spacebar) {
                    if (!await GTASA.Install()) {
                        Console.WriteLine("Installation failed. Press any key to exit.");
                        Console.ReadKey();
                        Environment.Exit(0);
                    }
                } else if (keyInfo.Key == ConsoleKey.Escape) {
                    Console.WriteLine("Exiting application.");
                    Environment.Exit(0);
                }
            } else if(Settings.SelectedGameInstallation == null) {
                try {
                    Settings.SelectedGameInstallation = new GameInstallation(GTASA.InstallPath);
                } catch (Exception ex) {
                    Console.WriteLine($"{ex.Message}");
                }
            }

            // Check for SA-MP
            if (!SAMP.IsInstalled) {
                Console.WriteLine("SA-MP is not installed. Press space to install or Esc to exit.");
                var keyInfo = Console.ReadKey();
                if (keyInfo.Key == ConsoleKey.Spacebar) {
                    if (!await SAMP.Install()) {
                        Console.WriteLine("Installation failed. Press any key to exit.");
                        Console.ReadKey();
                        Environment.Exit(0);
                    }
                } else if (keyInfo.Key == ConsoleKey.Escape) {
                    Console.WriteLine("Exiting application.");
                    Environment.Exit(0);
                }
            } else if(Settings.SelectedSAMPInstallation == null) {
                try {
                    Settings.SelectedSAMPInstallation = new GameInstallation(SAMP.GamePath);
                } catch (Exception ex) {
                    Console.WriteLine(ex);
                }
            }

            Settings.InitialSAMPGamePath = SAMP.GamePath;

            Console.WriteLine($"Pasta de Jogo: {Settings.SelectedGameInstallation}");
            if(Settings.SelectedSAMPInstallation != Settings.SelectedGameInstallation) Console.WriteLine($"Pasta de SA-MP: {Settings.SelectedSAMPInstallation}");

            LogWatcher.OnConnected += LogWatcher_OnConnected;

            GTASA.GameStarted += () => {
                DisplayMessage("Jogo Iniciado.", "Game Started.", ConsoleColor.Green, true, true);

                Menu.DisplayOptions();
            };

            GTASA.GameExited += () => {
                DisplayMessage("Jogo Terminado.", "Game Terminated.", ConsoleColor.Yellow, true, true);

                Menu.DisplayOptions();
            };

            LogWatcher.Start();

            string sampVersion = SAMP.Version ?? "Selected Location for SA-MP doesn't have it installed.";

            Console.WriteLine($"Nickname: {SAMP.PlayerName}");
            Console.WriteLine($"SA-MP: {sampVersion}\n");

            //await Player.Login("VIRUXE", "conacona");

            await GameValidation.FetchManifest();

            var validationResult = Settings.SelectedGameInstallation.Validate(out List<(string FilePath, List<InvalidFileReason> Reasons)> invalidFiles);

            if (validationResult != GameValidation.ValidationResult.Valid) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"This Installation is Invalid. Reason: {GetReadableValidationResult(validationResult)}");
                Console.ResetColor();
                Console.WriteLine("\nEither repair this Installation or create a clean one!\n");
            }

            GTASA.MonitorGameProcesses();

            await Menu.Init();
        }

        public static async Task ChangeInstallationPaths() {
            while (true) {
                List<GameInstallation> availableInstallations = new(Settings.GameInstallations);

                /*if (!availableInstallations.Any(inst => inst.Location == GamePath)) {
                    try {
                        availableInstallations.Insert(0, new GameInstallation(GamePath));
                    } catch (Exception ex) { Console.WriteLine(ex); }
                }*/

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Select or Add another GTA San Andreas Installation:");
                Console.ResetColor();
                for (int i = 0; i < availableInstallations.Count; i++) {
                    var installation = availableInstallations[i];
                    string displayText = $"{i + 1}. {installation.Location}";

                    if (installation.Location == Settings.SelectedGameInstallation.Location && installation.Location == Settings.SelectedSAMPInstallation.Location) 
                        displayText += " (Selected for Game/SA-MP)";
                    else if (installation.Location == Settings.SelectedGameInstallation.Location) 
                        displayText += " (Selected for Game)";
                    else if (installation.Location == Settings.SelectedSAMPInstallation.Location) 
                        displayText += " (Selected for SA-MP)";

                    if(!installation.HasExecutable) displayText += " (Doesn't have GTASA)";

                    if(!installation.HasSAMP) displayText += " (Doesn't have SA-MP)";

                    if(installation.Validate() != GameValidation.ValidationResult.Valid) {
                        Console.ForegroundColor = ConsoleColor.Red;
                        displayText += " (Invalid)";
                    }

                    Console.WriteLine(displayText);
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{availableInstallations.Count + 1}. Add an Existing GTA San Andreas Installation");
                Console.WriteLine($"{availableInstallations.Count + 2}. Download and Install a Clean GTA San Andreas");
                Console.ResetColor();

                int choice;
                while (true) {
                    Console.Write("Choose an option: ");

                    var keyInfo = Console.ReadKey();
                    Console.WriteLine();  // Move to next line as ReadKey doesn't do this automatically
                    if (int.TryParse(keyInfo.KeyChar.ToString(), out choice) && choice >= 1 && choice <= availableInstallations.Count + 2)
                        break;
                    else
                        Console.WriteLine("Invalid choice, please try again.");
                }
                Console.WriteLine();

                if (choice == availableInstallations.Count + 2) {
                    await GTASA.Install();
                } else if (choice == availableInstallations.Count + 1) {
                    AddNewInstallationPath();
                    break;
                } else {
                    var chosenInstallation = availableInstallations[choice - 1];

                    // Check if directory exists
                    if (!chosenInstallation.HasExecutable) {
                        Console.WriteLine($"The directory '{chosenInstallation}' no longer exists. Removing it from the list...");
                        Settings.GameInstallations.Remove(chosenInstallation);
                        Settings.Save();
                        Console.WriteLine("Installation has been removed from the list. Relisting available paths...");
                        continue;  // Continue the while loop to relist paths
                    }

                    var validationResult = chosenInstallation.Validate(out List<(string FilePath, List<InvalidFileReason> Reasons)> invalidFiles);

                    if (chosenInstallation == Settings.SelectedGameInstallation) {
                        Console.WriteLine("You have selected the current installation path.\n");
                    } else { // User selected a different Installation from what is the current one
                        if (validationResult != GameValidation.ValidationResult.Valid) {
                            Console.WriteLine($"This Installation '{chosenInstallation}' is Invalid. You can repair it if you like.\nWould you like to remove it from the list anyway? (y/n)");

                            char removeChoice = Console.ReadKey().KeyChar;
                            Console.WriteLine();
                            if (removeChoice == 'y' || removeChoice == 'Y') {
                                Settings.GameInstallations.Remove(chosenInstallation);
                                Settings.Save();
                                Console.WriteLine("Installation has been removed from the list. Relisting available paths...");
                                continue;
                            }
                        }

                        Settings.SelectedGameInstallation = chosenInstallation;

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Game path changed to: {chosenInstallation}");
                        Console.ResetColor();
                    }

                    if (validationResult != GameValidation.ValidationResult.Valid) {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write("This Installation is Invalid. Reason: ");
                        switch (validationResult) {
                            case GameValidation.ValidationResult.MissingExecutable:
                                Console.WriteLine("Missing executable.");
                                break;
                            case GameValidation.ValidationResult.InvalidExecutable:
                                Console.WriteLine("Invalid executable.");
                                break;
                            case GameValidation.ValidationResult.ManifestNotFetched:
                                Console.WriteLine("Manifest not fetched.");
                                break;
                            case GameValidation.ValidationResult.MissingRequiredFiles:
                                Console.WriteLine("Missing required files.");
                                break;
                            case GameValidation.ValidationResult.InvalidFiles:
                                Console.WriteLine("Contains Invalid files.");
                                Console.ForegroundColor = ConsoleColor.White;
                                foreach (var (FilePath, Reasons) in invalidFiles) Console.WriteLine($"- {FilePath}: {string.Join(", ", Reasons.Select(r => Regex.Replace(r.ToString(), "([A-Z])", " $1").Trim()))}");
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine(invalidFiles.Count + " in total.");
                                break;
                        }

                        Console.ResetColor();
                    } else { // Installation is actually valid so we now check for SA-MP
                             // We first check if the currently selected installation has it or not
                        if (!Settings.SelectedSAMPInstallation.HasSAMP) {
                            Console.WriteLine("The selected installation doesn't contain SA-MP. Searching for an alternative...");
                            // It doesn't actually have so we need to find one or install SA-MP
                            if (Settings.GameInstallations.Any(i => i.HasSAMP)) {
                                Console.WriteLine("Found other installations with SA-MP. Automatically selecting one...");
                                // We actually have one or more installations with SA-MP files
                                // Automatically select the first one that has SA-MP
                                Settings.SelectedSAMPInstallation = Settings.GameInstallations.FirstOrDefault(i => i.HasSAMP);
                            } else {
                                Console.WriteLine("No installations with SA-MP found. Initiating SA-MP installation...");
                                // No installations that contain SA-MP on record so we need to either install or add an installation that has one
                                if (await SAMP.Install()) {
                                    try {
                                        Console.WriteLine("SA-MP installation successful. Adding it to the list of game installations...");
                                        var sampInstallation = new GameInstallation(SAMP.GamePath);
                                        Settings.GameInstallations.Add(sampInstallation);
                                        Settings.SelectedSAMPInstallation = sampInstallation;
                                    } catch (Exception ex) {
                                        Console.WriteLine($"An error occurred: {ex}");
                                    }
                                }
                            }
                        }
                    }

                    break;
                }
            }
        }

        public static void AddNewInstallationPath() {
            while (true) {
                StringBuilder inputBuilder = new();
                int currentPosition = 0;

                Console.WriteLine("Please enter the installation path (Press ESC to cancel): ");

                while (true) {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

                    if (keyInfo.Key == ConsoleKey.Enter) {
                        break;
                    } else if (keyInfo.Key == ConsoleKey.Escape) {
                        return;
                    } else if (keyInfo.Key == ConsoleKey.Backspace) {
                        if (currentPosition > 0) {
                            inputBuilder.Remove(currentPosition - 1, 1);
                            Console.Write("\b \b");  // Handle backspace visually
                            currentPosition--;
                        }
                    } else if (keyInfo.Key == ConsoleKey.Home) {
                        Console.CursorLeft = 0;
                        currentPosition = 0;
                    } else {
                        inputBuilder.Insert(currentPosition, keyInfo.KeyChar);
                        Console.Write(keyInfo.KeyChar);  // Echo character to console
                        currentPosition++;
                    }
                }

                Console.WriteLine("\n");  // Move to the next line after Enter is pressed

                string installationPath = inputBuilder.ToString();

                if (string.IsNullOrEmpty(installationPath)) {
                    Console.WriteLine("Path cannot be empty. Please try again.");
                    continue;
                }

                try {
                    var installation = new GameInstallation(installationPath);

                    Settings.GameInstallations.Add(installation);
                    Settings.SelectedGameInstallation = installation;

                    Console.WriteLine("Installation added.");
                } catch (Exception) {
                    Console.WriteLine("Invalid path. Please try again.");
                }

                Console.WriteLine();
            }
        }
    }
}
