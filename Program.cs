using NostalgiaAnticheat.Game;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NostalgiaAnticheat {
    internal enum Language { EN, PT }

    internal class Program {
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

            TaskCompletionSource<object> tcs = new();

            Gameserver.ServerOnline += () => {
                DisplayMessage("Servidor ficou Online.", "Server is now Online.", ConsoleColor.Green, true, true);
                tcs.TrySetResult(null);  // Set the task as complete when the server comes back online
            };

            Gameserver.ServerOffline += () => {
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
            AppDomain.CurrentDomain.ProcessExit += async (s, e) => await Player.Logout();

            // Now before any actual good stuff we make sure we have GTASA and SAMP installed
            if (!GTASA.IsInstalled) {
                Console.WriteLine("GTA San Andreas is not installed. Would you like to install it? (y/n)");
                char choice = Console.ReadKey().KeyChar;
                if (choice == 'y' || choice == 'Y') {
                    bool success = await GTASA.Install();
                    if (!success) {
                        Console.WriteLine("Installation failed. Unable to continue.");
                        Console.ReadKey();
                        Environment.Exit(0);
                    }
                } else {
                    Console.WriteLine("GTA San Andreas is required. Unable to continue.");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
            }

            // Check if SA-MP is installed
            if (!SAMP.IsInstalled) {
                Console.WriteLine("SA-MP is not installed. Would you like to install it? (y/n)");
                char choice = Console.ReadKey().KeyChar;
                if (choice == 'y' || choice == 'Y') {
                    bool success = await SAMP.Install();
                    if (!success) {
                        Console.WriteLine("Installation failed. Unable to continue.");
                        Console.ReadKey();
                        Environment.Exit(0);
                    }
                } else {
                    Console.WriteLine("SA-MP is required. Unable to continue.");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
            }

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

            Console.WriteLine($"Nickname: {SAMP.PlayerName}");
            Console.WriteLine($"Caminho do Jogo: {SAMP.GamePath}");
            Console.WriteLine($"SA-MP: {SAMP.Version}\n");

            //await Player.Login("VIRUXE", "conacona");

            await GTASA.FetchManifest();

            Debug.WriteLine(GTASA.CountFilesInManifest());

            if (!GTASA.SetInstallationPath(SAMP.GamePath)) Console.WriteLine($"Your current Game Directory (\"{SAMP.GamePath}\") is not valid.");

            GTASA.StartMonitoring();

            await Menu.Init();
        }
    }
}
