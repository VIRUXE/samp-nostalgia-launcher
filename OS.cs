using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using HtmlAgilityPack;

namespace NostalgiaAnticheat {
    internal class OS {
        public static IEnumerable<(string FileName, string WindowTitle)> GetOpenWindows() {
            return Process.GetProcesses()
                .Where(p => !p.ProcessName.StartsWith("System") && !p.ProcessName.StartsWith("Idle") && p.MainWindowTitle != string.Empty)
                .Select(p => (p.ProcessName, p.MainWindowTitle));
        }

        public static string GenerateHWID() {
            var processorId = "";

            // Get processor ID
            foreach (ManagementObject mo in new ManagementObjectSearcher("Select ProcessorId From Win32_processor").Get()) {
                processorId = mo["ProcessorId"].ToString();
                break;
            }

            // Get volume _hwid number
            ManagementObject dsk = new(@"win32_logicaldisk.deviceid=""c:""");
            dsk.Get();

            byte[] bytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(processorId + dsk["VolumeSerialNumber"]));
            StringBuilder builder = new();

            for (var i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));

            return builder.ToString();
        }

        public static bool Is7ZipInstalled() {
            try {
                Process process = new();
                process.StartInfo.FileName = "7z";
                process.StartInfo.Arguments = "";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                return true;
            } catch {
                return false;
            }
        }

        public static async Task<string> Get7ZipInstallerUrl() {
            var response = await new HttpClient().GetStringAsync("https://www.7-zip.org/download.html");

            var document = new HtmlDocument();
            document.LoadHtml(response);

            int rowIndex = Environment.Is64BitOperatingSystem ? 5 : 6;

            var msiUrlNode = document.DocumentNode.SelectSingleNode($"//table[@cellspacing='1']//tr[{rowIndex}]//td[@class='Item'][1]/a");

            if (msiUrlNode != null)
                return "https://www.7-zip.org/" + msiUrlNode.Attributes["href"].Value;
            else
                throw new Exception("Unable to find the MSI installer URL.");
        }

        public static async Task<bool> Install7Zip() {
            try {
                string installerUrl = await Get7ZipInstallerUrl();
                string installerName = Path.GetFileName(new Uri(installerUrl).AbsolutePath);
                string installerPath = Path.Combine(Directory.GetCurrentDirectory(), installerName);

                if (!File.Exists(installerPath)) {
                    Console.Write("Downloading 7Zip installer... ");
                    try {
                        byte[] installerBytes = await new HttpClient().GetByteArrayAsync(installerUrl);
                        await File.WriteAllBytesAsync(installerPath, installerBytes);
                        Console.WriteLine("Done.");
                    } catch (Exception downloadEx) {
                        Console.WriteLine("Failed.");
                        Console.WriteLine("Could not download the installer: " + downloadEx.Message);
                        return false;
                    }
                }

                using Process installProcess = new() {
                    StartInfo = new ProcessStartInfo {
                        FileName = "msiexec",
                        Arguments = $"/i \"{installerPath}\" /qn",
                        Verb = "runas"
                    }
                };

                Console.Write("Installing 7Zip... ");
                installProcess.Start();
                installProcess.WaitForExit();

                if (installProcess.ExitCode != 0) {
                    Console.WriteLine("Failed.");
                    Console.WriteLine($"Installation failed with exit code {installProcess.ExitCode}");
                    return false;
                }

                Console.WriteLine("Successful.");
                return true;
            } catch (Exception ex) {
                Console.WriteLine("Failed.");
                Console.WriteLine($"An error occurred: {ex.Message}");
                return false;
            }
        }
    }
}
