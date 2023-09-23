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

            if (!Updater.ShowReleaseNotes()) {
                if (!await Updater.CheckAndUpdate()) {
                    Console.WriteLine("Unable to continue. Press any key to exit.");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
            }

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
            AppDomain.CurrentDomain.ProcessExit += async (s, e) => await Player.Logout();

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
            }

            Console.WriteLine($"Caminho do Jogo: {SAMP.GamePath}");

            // Check for SA-MP
            if (SAMP.GamePath == null) {
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

            string sampVersion = SAMP.Version ?? "Unknown";

            Console.WriteLine($"Nickname: {SAMP.PlayerName}");
            Console.WriteLine($"SA-MP: {sampVersion}\n");

            //await Player.Login("VIRUXE", "conacona");

            await GTASA.FetchManifest();

            Debug.WriteLine(GTASA.CountFilesInManifest());

            var (isSuccessful, reasons) = GTASA.SetInstallationPath(SAMP.GamePath);

            if (!isSuccessful) {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine("The installation path in SA-MP is not valid. Reason(s):");
                foreach (var reason in reasons) Console.WriteLine($"- {reason}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\nEither repair this Installation or create a clean Installation!\n");
                Console.ResetColor();
            }

            GTASA.MonitorProcesses();

            await Menu.Init();
        }
    }
}
