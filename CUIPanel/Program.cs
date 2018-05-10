using System;
using System.Threading;

namespace CUIPanel {
    internal class Program {
        static int c = -1;

        public static void Main(string[] args) {
            try {
                ConsoleManager cManager = new ConsoleManager {
                    UpdateRate = 100,
                    UsePassiveUpdate = true,
                    CursorVisible = false
                };
                cManager.SetWindowSize(120, 40);
                char[,] a = cManager.PanelBuffer;
                for (int i = 0; i < a.GetLength(0); i++)
                    for (int j = 0; j < a.GetLength(1); j++)
                        a[i,j] = ' ';
                cManager.DrawPanel(0, 0, a);
                cManager.BeforeUpdate += Test_ColorWave;

                Console.Title = Console.WindowWidth.ToString() + ',' + Console.WindowHeight.ToString();
                while (true)
                    Thread.Sleep(100000);
            } catch (Exception e) {
                Console.WriteLine(e.Message);
                Console.WriteLine("按下回车键继续...");
                Console.ReadLine();
            }
        }

        private static void Test_ColorWave(ConsoleManager cManager) {
            int color = c++ == -1 ? 0 : ((int)cManager.FGColorSet[0, 0] + 1) % 15, 
                width = 2;
            for (int i = 0; i < cManager.PanelHeight; i += width) {
                cManager.DrawPanel(i, 0, i + width - 1 >= cManager.PanelHeight ? cManager.PanelHeight - 1 : i + width - 1, cManager.PanelWidth - 1, (ConsoleColor)color, (ConsoleColor)color + 1);
                color = (color + 1) % 15;
            }
        }
    }
}