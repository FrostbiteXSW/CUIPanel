using System;
using System.Threading;
using CUIPanel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestSimpleMoving {
    [TestClass]
    internal static class TestSimpleMoving {
        private const int BWidth = 6, BHeight = 3;
        private static ConsoleManager _cManager;
        private static int _curRow, _curCol, _curRowBefore, _curColBefore;

        private static void Init() {
            _cManager = new ConsoleManager {
                UpdateRate = 10,
                UsePassiveUpdate = true,
                CursorVisible = false,
                IsPaused = true
            };
            _cManager.SetWindowSize((BWidth + 1) * 8 + 2, (BHeight + 1) * 8 + 2);
            var a = new char[(BHeight + 1) * 8 + 1, (BWidth + 1) * 8 + 1];
            for (var i = 0; i < a.GetLength(0); i++) {
                for (var j = 0; j < a.GetLength(1); j += BWidth + 1)
                    if (i == 0) {
                        if (j == 0) {
                            a[i, j] = '┌';
                            for (var k = 0; k < BWidth; k++)
                                a[i, j + k + 1] = '─';
                        }
                        else if (j == a.GetLength(1) - 1) {
                            a[i, j] = '┐';
                        }
                        else {
                            a[i, j] = '┬';
                            for (var k = 0; k < BWidth; k++)
                                a[i, j + k + 1] = '─';
                        }
                    }
                    else if (i == a.GetLength(0) - 1) {
                        if (j == 0) {
                            a[i, j] = '└';
                            for (var k = 0; k < BWidth; k++)
                                a[i, j + k + 1] = '─';
                        }
                        else if (j == a.GetLength(1) - 1) {
                            a[i, j] = '┘';
                        }
                        else {
                            a[i, j] = '┴';
                            for (var k = 0; k < BWidth; k++)
                                a[i, j + k + 1] = '─';
                        }
                    }
                    else if (i % 4 == 0) {
                        if (j == 0) {
                            a[i, j] = '├';
                            for (var k = 0; k < BWidth; k++)
                                a[i, j + k + 1] = '─';
                        }
                        else if (j == a.GetLength(1) - 1) {
                            a[i, j] = '┤';
                        }
                        else {
                            a[i, j] = '┼';
                            for (var k = 0; k < BWidth; k++)
                                a[i, j + k + 1] = '─';
                        }
                    }
                    else {
                        a[i, j] = '│';
                    }
            }
            _cManager.DrawPanel(0, 0, a);
            for (var i = 0; i < a.GetLength(0); i += BHeight + 1)
                _cManager.DrawPanel(i, 0, i, a.GetLength(1) - 1, ConsoleColor.Yellow, ConsoleColor.DarkGray);
            for (var i = 0; i < a.GetLength(1); i += BWidth + 1)
                _cManager.DrawPanel(0, i, a.GetLength(0) - 1, i, ConsoleColor.Yellow, ConsoleColor.DarkGray);
            for (var i = 1; i < (BHeight + 1) * 8 + 1; i += BHeight + 1)
                for (var j = 1; j < (BWidth + 1) * 8 + 1; j += BWidth + 1) {
                    _cManager.DrawPanel(i, j, i + BHeight - 1, j + BWidth - 1, ConsoleColor.Green, ConsoleColor.Black);
                    _cManager.DrawPanel(i + 1, j + 2, new[,] {{'-', '-'}});
                }
            _curRow = _curCol = _curRowBefore = _curColBefore = 0;
            _cManager.Title = Console.WindowWidth.ToString() + ',' + Console.WindowHeight;
            Console.CancelKeyPress += Console_CancelKeyPress;
            _cManager.DrawPanel(2 + _curRow * (BHeight + 1), 3 + _curCol * (BWidth + 1), new[,] {{'+', '+'}}, ConsoleColor.Red, ConsoleColor.Black);
            _cManager.BeforeUpdate += _cManager_BeforeUpdate;
            _cManager.AfterResize += _cManager_AfterResize;
            _cManager.KeyPressed += _cManager_KeyPressed;
            _cManager.IsPaused = false;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e) {
            _cManager.Clear();
            _cManager.IsPaused = true;
            Console.Clear();
            Console.WriteLine("测试结束，程序将在3秒后退出...");
            Thread.Sleep(3 * 1000);
            _cManager.Exit();
            Console.Clear();
            Console.WriteLine("程序已退出。");
            Environment.Exit(0);
        }

        private static void _cManager_BeforeUpdate(ConsoleManager cManager) {
            if (_curRow == _curRowBefore && _curCol == _curColBefore) return;
            _cManager.DrawPanel(2 + _curRowBefore * (BHeight + 1), 3 + _curColBefore * (BWidth + 1), new[,] {{'-', '-'}}, ConsoleColor.Green, ConsoleColor.Black);
            _curRowBefore = _curRow;
            _curColBefore = _curCol;
            _cManager.DrawPanel(2 + _curRowBefore * (BHeight + 1), 3 + _curColBefore * (BWidth + 1), new[,] {{'+', '+'}}, ConsoleColor.Red, ConsoleColor.Black);
        }

        private static void _cManager_AfterResize(ConsoleManager cManager) {
            _cManager.Title = Console.WindowWidth.ToString() + ',' + Console.WindowHeight;
            if (cManager.PanelHeight < (BHeight + 1) * 8 + 1 || cManager.PanelWidth < (BWidth + 1) * 8 + 1) {
                cManager.Clear();
                Console.WriteLine("窗口大小过小，无法继续显示。");
                Console.WriteLine("按任意键继续...");
                Console.ReadKey(true);
                Environment.Exit(-1);
            }
        }

        private static void _cManager_KeyPressed(ConsoleManager cManager, ConsoleKeyInfo keyInfo) {
            switch (keyInfo.Key) {
                case ConsoleKey.LeftArrow:
                    _curCol = _curCol - 1 < 0 ? 0 : _curCol - 1;
                    break;
                case ConsoleKey.RightArrow:
                    _curCol = _curCol + 1 > 7 ? 7 : _curCol + 1;
                    break;
                case ConsoleKey.UpArrow:
                    _curRow = _curRow - 1 < 0 ? 0 : _curRow - 1;
                    break;
                case ConsoleKey.DownArrow:
                    _curRow = _curRow + 1 > 7 ? 7 : _curRow + 1;
                    break;
            }
        }

        [TestMethod]
        public static void Main(string[] args) {
            try {
                Init();
            }
            catch (Exception e) {
                Console.WriteLine(e.Message);
                Console.WriteLine("按下回车键继续...");
                Console.ReadLine();
            }
        }
    }
}