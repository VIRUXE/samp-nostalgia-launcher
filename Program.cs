using NostalgiaAnticheat.Game;
using System;
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

            DisplayMessage("Conexão ao servidor de jogo estabelecida.", "Connected to the game server.", ConsoleColor.Green, true, false);
        }

        public static async Task Main() {
            Console.Title = "Scavenge Nostalgia";

            if (!new Mutex(false, "NostalgiaLauncherMutex").WaitOne(0, false)) return;

            if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToUpper() == Language.PT.ToString()) SystemLanguage = Language.PT;

            DisplayMessage("Launcher do Scavenge Nostalgia", "Scavenge Nostalgia Launcher", ConsoleColor.White, false);
            Console.WriteLine($" - {Assembly.GetExecutingAssembly().GetName().Version}\n");

            if (await Player.IsHwBanned()) {
                DisplayMessage("Banido.", "Banned.", ConsoleColor.Red, true, true);
                Console.ReadKey();
                Environment.Exit(0);
            }

            // Logout Player on app exit
            AppDomain.CurrentDomain.ProcessExit += async (s, e) => await Player.Logout();

            LogWatcher.OnConnected += LogWatcher_OnConnected;

            _ = Gameserver.Monitor(); // Start monitoring our gameserver

            // but why
            while (Gameserver.State == Network.State.Offline) {
                DisplayMessage("Servidor Offline. Aguardando...", "Server Offline. Waiting...", ConsoleColor.Red, true, true);

                Gameserver.StateUpdated.Wait();
            }

            Gameserver.ServerOnline += () => {
                DisplayMessage("Servidor voltou.", "Server is now back Online.", ConsoleColor.Green, true, true);
            };

            Gameserver.ServerOffline += () => {
                DisplayMessage("Servidor caiu. Aguardando que volte...", "Server went Offline. Waiting to come back...", ConsoleColor.Green, true, true);
            };

            LogWatcher.Start();

            Console.WriteLine($"Nickname: {SAMP.GetPlayerNickname()}");

            await Player.Login("VIRUXE", "conacona");

            GTASA.SetInstallationPath(SAMP.GetGamePath());

            _ = Menu.Show();
        }
    }
}
