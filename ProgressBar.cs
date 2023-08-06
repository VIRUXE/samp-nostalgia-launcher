using System;

namespace NostalgiaAnticheat {
    public static class ProgressBar {
        private const int totalBlocks = 10;
        private const char progressBlock = '█';
        private const char emptyBlock = '.';

        public static void Display(long totalRead, long contentLength) {
            double progress = (double)totalRead / contentLength;
            var blocksCount = (int)Math.Round(progress * totalBlocks);
            string progressBar = new string(progressBlock, blocksCount) + new string(emptyBlock, totalBlocks - blocksCount);

            // Move the cursor back to the start of the line
            Console.CursorLeft = 0;

            // Write the progress bar
            Console.Write($"[{progressBar}] {progress:P1}");
        }
    }

}
