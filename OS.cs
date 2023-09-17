using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;

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
    }
}
