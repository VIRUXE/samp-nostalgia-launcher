using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NostalgiaAnticheat.Game;

namespace NostalgiaAnticheat {
    public record MenuOption((string PT, string EN) Name, Func<Task> Action, Func<bool> Condition);

    internal enum Language { EN, PT }

    internal class Program {
        public static Language SystemLanguage = Language.EN;
        private static GTASA Game;
        private static readonly Player Player = new();
        private static readonly LogWatcher LogWatcher = new();

        public static string SendMessage(string ptMessage, string enMessage = null, ConsoleColor color = ConsoleColor.Gray, bool newLine = true, bool isAction = false) {
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

        public static string ReadPassword() {
            var password = "";
            ConsoleKeyInfo info = Console.ReadKey(true);

            while (info.Key != ConsoleKey.Enter) {
                if (info.Key != ConsoleKey.Backspace) {
                    Console.Write("*");
                    password += info.KeyChar;
                } else if (info.Key == ConsoleKey.Backspace) {
                    if (!string.IsNullOrEmpty(password)) {
                        password = password.Substring(0, password.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                info = Console.ReadKey(true);
            }

            Console.WriteLine();

            return password;
        }

        public static async Task ShowMenu() {
            Dictionary<char, Func<Task>> options = new();

            List<MenuOption> menuOptions = new() {
                new MenuOption(
                    ("Logar", "Login"),
                    async () => {
                        SendMessage(
                            "Para entrar, você precisará fornecer seu Apelido e Senha do Servidor.\nDigite seu apelido (ou deixe em branco para usar o que tem no SA-MP):",
                            "To log in, you will need to provide your server nickname and password.\nEnter your nickname (or leave blank to use the one you have in SA-MP):"
                            , ConsoleColor.Cyan);

                        string sampNickname = SAMP.GetPlayerNickname();
                        string inputNickname = Console.ReadLine()?.Trim();
                        string nickname;

                        if (!string.IsNullOrEmpty(inputNickname) && inputNickname != sampNickname) {
                            SAMP.SetPlayerNickname(inputNickname);
                            nickname = inputNickname;
                        } else
                            nickname = sampNickname;

                        SendMessage(
                            "Agora, digite sua senha (as entradas serão ocultas por motivos de segurança):",
                            "Now, enter your password (entries will be hidden for security reasons):",
                            ConsoleColor.Cyan);

                        var accountData = await Player.Login(nickname, ReadPassword());

                        if (accountData.Count > 0) { // Means we actually got something back. Even if it is an error
                            if (!accountData.ContainsKey("error")) {
                                foreach (var item in accountData) Console.WriteLine(item);
                            } else {
                                SendMessage("Erro", "Error", ConsoleColor.Red, false);
                                Console.WriteLine($": {accountData["error"]}");
                            }
                        } else
                            SendMessage(
                                "Agora, digite sua senha (as entradas serão ocultas por motivos de segurança):",
                                "Now, enter your password (entries will be hidden for security reasons):",
                                ConsoleColor.Cyan);
                    },
                    () => !Player.LoggedIn && Game.ComparePath(SAMP.GetGamePath()) == 1 && Gameserver.IsOnline && Game.Valid
                ),
                new MenuOption(
                    ("Deslogar", "Logout"),
                    async () => {
                        if (await Player.Logout())
                            SendMessage("Deslogado com Sucesso", "Logged out.", ConsoleColor.Green, true, true);
                        else
                            SendMessage("Ocorreu um erro ao deslogar.", "An error ocurred while logging out.", ConsoleColor.Red, true, true);
                    },
                    () => Player.LoggedIn
                ),
                new MenuOption(
                    ("Jogar", "Play"),
                    async () => {
                        if (Game.Launch())
                            SendMessage("Jogo Iniciado. Esperando conexão... ", "Game Started. Waiting for connection... ", ConsoleColor.White, false, true);
                        else
                            SendMessage("O jogo já se encontra iniciado. Focando na tela.", "The game is already running. Showing window.", ConsoleColor.Yellow, true, true);

                        do await Task.Delay(1000); while (!Game.Connected);
                    },
                    () => Player.LoggedIn && !Game.IsRunning && Game.ComparePath(SAMP.GetGamePath()) == 1 && Gameserver.IsOnline && Game.Valid
                ),
                new MenuOption(
                    ("Verificar Arquivos do Jogo", "Verify Game Files"),
                    () => {
                        Game.Verify();
                        return Task.CompletedTask;
                    },
                    () => Player.LoggedIn && !Game.IsRunning
                ),
                new MenuOption(
                    ("Focar no Jogo", "Focus on Game"),
                    () => {
                        if (Game.Focus()) 
                            SendMessage("Jogo focado na tela.", "Focused on Game", ConsoleColor.Green, true, true);
                        else 
                            SendMessage("O jogo já se encontra na tela.", "You're already focused on the game.", ConsoleColor.Yellow, true, true);

                        return Task.CompletedTask;
                    },
                    () => Game.IsRunning
                ),
                new MenuOption(
                    ("Fechar Jogo", "Close Game"),
                    async () => {
                        SendMessage("Fechando", "Closing", ConsoleColor.Yellow, false, true);

                        Console.Write("... ");

                        if (await Game.Close()) 
                            SendMessage("Fechado.", "Closed.", ConsoleColor.Green);
                        else 
                            SendMessage("Impossivel fechar.", "Unable to Close.", ConsoleColor.Red);
                    },
                    () => Game.IsRunning
                )
            };

            SendMessage("\nPressione a tecla correspondente a opção desejada...", "\nPress the key corresponding to your option...", ConsoleColor.White);

            var optionIndex = 1;
            foreach (MenuOption option in menuOptions) {
                if (option.Condition()) {
                    options.Add(optionIndex.ToString()[0], option.Action);

                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write($"{optionIndex}. ");
                    Console.ResetColor();
                    Console.WriteLine(SystemLanguage == Language.PT ? option.Name.PT : option.Name.EN);

                    optionIndex++;
                }
            }

            Console.WriteLine("Q. Quit\n");

            while (true) {
                ConsoleKeyInfo key = Console.ReadKey(true);

                //Debug.WriteLineIf(!string.IsNullOrEmpty(key.ToString()), $"Key Pressed: {key.KeyChar}");

                if (key.KeyChar == 'q' || key.KeyChar == 'Q') {
                    _ = Player.Logout(); // No need to wait since we are exiting

                    Environment.Exit(0);
                } else if (options.ContainsKey(key.KeyChar)) {
                    await options[key.KeyChar]();

                    await ShowMenu();
                } else {
                    SendMessage("Opção inválida. Tente novamente.", "Invalid option. Try again.", ConsoleColor.Red);
                    await ShowMenu();
                }
            }
        }

        private static async void LogMonitor_OnConnected(object sender, EventArgs e) {
            do await Task.Delay(1000); while (!Game.IsResponding);

            Game.Connected = true;

            SendMessage("Conexão ao servidor de jogo estabelecida.", "Connected to the gameserver.", ConsoleColor.Green, true, false);

            //await Task.Delay(10000);

            _ = SAMP_API.API.SendChat("/2fa 1a2b3c");

            _ = SAMP_API.API.AddChatMessage("Anti-Cheat ativo.");

            /*if (Game.SendKeys("/2fa 1a2b3c"))
                SendMessage("Autenticação enviada.", "Authentication sent.", ConsoleColor.Green, true, true);
            else {
                SendMessage("Autenticação falhou. Fechando o Jogo.", "Authentication failed. Closing game.", ConsoleColor.Red, true, true);
                _ = Game.Close();
            }*/

            await ShowMenu();
        }

        public static async Task Main() {
            if (!new Mutex(false, "NostalgiaLauncherMutex").WaitOne(0, false)) return;

            if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToUpper() == Language.PT.ToString()) SystemLanguage = Language.PT;

            Console.Title = "Scavenge Nostalgia";

            SendMessage("Launcher do Scavenge Nostalgia", "Scavenge Nostalgia Launcher", ConsoleColor.White, false);
            Console.WriteLine($" - {Assembly.GetExecutingAssembly().GetName().Version}\n");

            if (await Player.IsHwBanned()) {
                SendMessage("Banido.", "Banned.", ConsoleColor.Red, true, true);
                Console.ReadKey();
                Environment.Exit(0);
            }

            // Logout Player on app exit
            AppDomain.CurrentDomain.ProcessExit += async (s, e) => await Player.Logout();

            LogWatcher.OnConnected += LogMonitor_OnConnected;

            _ = Gameserver.Monitor();

            while (Gameserver.State == Network.State.Offline) {
                SendMessage("Servidor Offline. Aguardando...", "Server Offline. Waiting...", ConsoleColor.Red, true, true);

                Gameserver.StateUpdated.Wait();
            }

            Gameserver.ServerOnline += async () => {
                SendMessage("Servidor voltou.", "Server is now back Online.", ConsoleColor.Green, true, true);

                await ShowMenu();
            };

            Gameserver.ServerOffline += async () => {
                SendMessage("Servidor caiu. Aguardando que volte...", "Server went Offline. Waiting to come back...", ConsoleColor.Green, true, true);

                await ShowMenu();
            };

            var gameInstallPath = GTASA.GetInstallPath();
            var sampGamePath = SAMP.GetGamePath();

            if(sampGamePath == null) {
                SendMessage("Parece que não tem o SA-MP instalado. Deseja instalar?", "You don't have SA-MP installed. Would you like to install it?", ConsoleColor.Yellow, true, true);


            }

            var sampVersion = SAMP.GetVersion();
            var playerName = SAMP.GetPlayerNickname();

            /*SendMessage("Caminho de Instalação: ", "Installation Path: ", ConsoleColor.Gray, false);
            Console.ForegroundColor = ConsoleColor.Magenta;
            SendMessage(gameInstallPath, null, ConsoleColor.Magenta);

            if (!string.IsNullOrEmpty(sampGamePath) && !string.IsNullOrEmpty(playerName)) {
                SendMessage("Caminho do Jogo no SA-MP: ", "SA-MP's Game Path: ", ConsoleColor.Gray, false);
                SendMessage(sampGamePath, null, ConsoleColor.Magenta);

                Console.Write("Nickname: ");
                SendMessage(playerName, null, ConsoleColor.Magenta);

                SendMessage("Versão do SA-MP: ", "SA-MP's Version: ", ConsoleColor.Gray, false);

                                

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine(!string.IsNullOrEmpty(sampVersion) ? $"R{sampVersion}" : SendMessage("Desconhecida", "Unknown"));
                Console.ResetColor();*/

            Game = new GTASA(sampGamePath);

            LogWatcher.Start();

            await Player.Login("VIRUXE", "conacona");

            await ShowMenu();
        }
    }
}
