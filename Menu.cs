using NostalgiaAnticheat.Game;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NostalgiaAnticheat {
    public static class Menu {
        private record MenuOption((string PT, string EN) Name, Func<bool> Condition, Func<Task> Function);

        private static readonly List<MenuOption> menuOptions = new() {
            new MenuOption(("Logar", "Login"),
                () => !Player.LoggedIn && Gameserver.IsOnline,
                async () => {
                    Program.DisplayMessage(
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

                    Program.DisplayMessage(
                        "Agora, digite sua senha (as entradas serão ocultas por motivos de segurança):",
                        "Now, enter your password (entries will be hidden for security reasons):",
                        ConsoleColor.Cyan);

                    var accountData = await Player.Login(nickname, ReadPassword());

                    if (accountData.Count > 0) { // Means we actually got something back. Even if it is an error
                        if (!accountData.ContainsKey("error")) {
                            foreach (var item in accountData) Console.WriteLine(item);
                        } else {
                            Program.DisplayMessage("Erro", "Error", ConsoleColor.Red, false);
                            Console.WriteLine($": {accountData["error"]}");
                        }
                    } else
                        Program.DisplayMessage(
                            "Agora, digite sua senha (as entradas serão ocultas por motivos de segurança):",
                            "Now, enter your password (entries will be hidden for security reasons):",
                            ConsoleColor.Cyan);
                }
            ),
            new MenuOption(("Deslogar", "Logout"),
                () => Player.LoggedIn,
                async () => {
                    try {
                    if (await Player.Logout()) {
                        Program.DisplayMessage("Deslogado com Sucesso", "Logged out.", ConsoleColor.Green, true, true);
                    } else
                        Program.DisplayMessage("Ocorreu um erro ao deslogar.", "An error ocurred while logging out.", ConsoleColor.Red, true, true);
                    } catch (Exception e) { Console.WriteLine(e); }
                }   
            ),
            new MenuOption(("Jogar", "Play"),
                () => Player.LoggedIn && !GTASA.IsRunning && Gameserver.IsOnline && GTASA.IsInstallationValid,
                () => {
                    if (GTASA.Launch())
                        Program.DisplayMessage("Jogo Iniciado. Esperando conexão... ", "GTASA Started. Waiting for connection... ", ConsoleColor.White, false, true);
                    else
                        Program.DisplayMessage("O jogo já se encontra iniciado. Focando na tela.", "The GTASA is already running. Showing window.", ConsoleColor.Yellow, true, true);

                    return Task.CompletedTask;
                }
            ),
            new MenuOption(("Verificar Arquivos do Jogo", "Verify Game Files"),
                () => Player.LoggedIn && !GTASA.IsRunning,
                () => {
                    //GTASA.Verify();
                    Console.WriteLine("Verifying files...");

                    return Task.CompletedTask;
                }
            ),
            new MenuOption(("Focar no Jogo", "Focus on GTASA"),
                () => GTASA.IsRunning && !GTASA.IsFocused,
                () => {
                    if (GTASA.Focus())
                        Program.DisplayMessage("Jogo focado na tela.", "Focused on GTASA", ConsoleColor.Green, true, true);
                    else
                        Program.DisplayMessage("O jogo já se encontra na tela.", "You're already focused on the GTASA.", ConsoleColor.Yellow, true, true);

                    return Task.CompletedTask;
                }
            ),
            new MenuOption(("Fechar Jogo", "Close GTASA"),
                () => GTASA.IsRunning,
                async () => {
                    Program.DisplayMessage("Fechando", "Closing", ConsoleColor.Yellow, false, true);

                    Console.Write("... ");

                    if (await GTASA.Close())
                        Program.DisplayMessage("Fechado.", "Closed.", ConsoleColor.Green);
                    else
                        Program.DisplayMessage("Impossivel fechar.", "Unable to Close.", ConsoleColor.Red);
                }
            )
        };

        private static string ReadPassword() {
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

        public static async Task Show() {
            Dictionary<char, Func<Task>> availableOptions = new();

            void DisplayMenu() {
                availableOptions.Clear();

                Console.WriteLine(Program.SystemLanguage == Language.PT ? "\nPressione a tecla correspondente a opção desejada..." : "\nPress the key corresponding to your option...");

                var optionIndex = 1;
                foreach (MenuOption option in menuOptions) {
                    if (!option.Condition()) continue;

                    availableOptions.Add(optionIndex.ToString()[0], option.Function);

                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write($"{optionIndex}. ");
                    Console.ResetColor();
                    Console.WriteLine(Program.SystemLanguage == Language.PT ? option.Name.PT : option.Name.EN);

                    optionIndex++;
                }

                Console.WriteLine("Q. Quit\n");
            }

            DisplayMenu();

            while (true) {
                ConsoleKeyInfo key = Console.ReadKey(true);

                if (key.KeyChar == 'q' || key.KeyChar == 'Q') {
                    _ = Player.Logout(); // No need to wait since we are exiting
                    Environment.Exit(0);
                } else if (availableOptions.ContainsKey(key.KeyChar)) {
                    try {
                        var task = availableOptions[key.KeyChar]();
                        await task;
                        Console.WriteLine("Task Status: " + task.Status);

                        DisplayMenu();
                    } catch (Exception ex) {
                        Console.WriteLine("An error occurred: " + ex.Message);
                    }
                } else {
                    Program.DisplayMessage("Opção inválida. Tente novamente.", "Invalid option. Try again.", ConsoleColor.Red);
                }
            }
        }
    }
}
