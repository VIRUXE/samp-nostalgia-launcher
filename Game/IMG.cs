using System.Collections.Generic;
using System.Linq;
using IMGSharp;

namespace NostalgiaAnticheat.Game {
    internal class IMG {
        public static Dictionary<string, int> GetMetadata(string imgPath) {
            return IMGFile.OpenRead(imgPath).Entries.ToDictionary(entry => entry.Name, entry => entry.Length);
        }
    }
}
