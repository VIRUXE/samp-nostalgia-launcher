using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace NostalgiaAnticheat {
    internal class OS {
        public static IEnumerable<(string FileName, string WindowTitle)> GetOpenWindows() {
            return Process.GetProcesses()
                .Where(p => !p.ProcessName.StartsWith("System") && !p.ProcessName.StartsWith("Idle") && p.MainWindowTitle != string.Empty)
                .Select(p => (p.ProcessName, p.MainWindowTitle));
        }
    }
}
