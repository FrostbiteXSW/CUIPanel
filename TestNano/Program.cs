using CUIPanel;
using System;
using System.Text;

namespace TestNano {
    static class Program {
        private static ConsoleManager _cManager;
        private static readonly StringBuilder StringBuffer = new StringBuilder();

        static void Init() {
            _cManager = new ConsoleManager() {
                IsPaused = true,
                UsePassiveUpdate = true,
                UpdateRate = 10,
                CursorVisible = true
            };

            _cManager.AfterResize += CManager_AfterResize;
            _cManager.KeyPressed += CManager_KeyPressed;
            _cManager.SetWindowSize(_cManager.LargestPanelWidth / 2, _cManager.LargestPanelHeight / 2);
            _cManager.SetCursorPosition(1, 0);
            _cManager.IsPaused = false;
        }

        private static void CManager_KeyPressed(ConsoleManager cManager, ConsoleKeyInfo keyInfo) {
            int row = 1;
            int col = 0;
            switch (keyInfo.Key) {
                case ConsoleKey.Backspace:
                    if (StringBuffer.Length == 0) return;
                    for (int i = 0; i < StringBuffer.Length - 1; i++) {
                        if (++col == cManager.PanelWidth) {
                            row++;
                            col = 0;
                        }
                        if (StringBuffer[i] == '\n') {
                            row++;
                            col = 0;
                        }
                    }
                    cManager.DrawPanel(row, col, " ");
                    cManager.SetCursorPosition(row, col);
                    StringBuffer.Remove(StringBuffer.Length - 1, 1);
                    return;
                case ConsoleKey.Enter:
                    for (int i = 0; i < StringBuffer.Length; i++) {
                        if (++col == cManager.PanelWidth) {
                            row++;
                            col = 0;
                        }
                        if (StringBuffer[i] == '\n') {
                            row++;
                            col = 0;
                        }
                    }
                    if (row < cManager.PanelHeight - 1) {
                        cManager.SetCursorPosition(++row, 0);
                        StringBuffer.Append('\n');
                    }
                    return;
                case ConsoleKey.Tab:
                    for (int i = 0; i < StringBuffer.Length; i++) {
                        if (++col == cManager.PanelWidth) {
                            row++;
                            col = 0;
                        }
                        if (StringBuffer[i] == '\n') {
                            row++;
                            col = 0;
                        }
                    }
                    if (row > cManager.PanelHeight - 1 || col > cManager.PanelWidth - 1) return;
                    cManager.DrawPanel(row, col, " ");
                    if (++col == cManager.PanelWidth) {
                        row++;
                        col = 0;
                    }
                    if (row <= cManager.PanelHeight - 1)
                        cManager.SetCursorPosition(row, col);
                    else
                        cManager.SetCursorPosition(row - 1, cManager.PanelWidth - 1);
                    StringBuffer.Append(" ");
                    return;
                default:
                    for (int i = 0; i < StringBuffer.Length; i++) {
                        if (++col == cManager.PanelWidth) {
                            row++;
                            col = 0;
                        }
                        if (StringBuffer[i] == '\n') {
                            row++;
                            col = 0;
                        }
                    }
                    if (row > cManager.PanelHeight - 1 || col > cManager.PanelWidth - 1) return;
                    cManager.DrawPanel(row, col, keyInfo.KeyChar.ToString());
                    if (++col == cManager.PanelWidth) {
                        row++;
                        col = 0;
                    }
                    if (row <= cManager.PanelHeight - 1)
                        cManager.SetCursorPosition(row, col);
                    else
                        cManager.SetCursorPosition(row - 1, cManager.PanelWidth - 1);
                    StringBuffer.Append(keyInfo.KeyChar);
                    return;
            }
        }

        private static void CManager_AfterResize(ConsoleManager cManager) {
            if (cManager.PanelWidth <= 6 || cManager.PanelHeight <= 2) {
                cManager.Exit();
                Console.Clear();
                Console.WriteLine("窗口过小，程序中止。");
            }
            cManager.Clear();
            cManager.DrawPanel(0, 0, 0, cManager.PanelWidth - 1, ' ', ConsoleColor.Black, ConsoleColor.Gray);
            cManager.DrawPanel(0, 0, "Test Nano in CSharp");
            cManager.DrawPanel(0, cManager.PanelWidth - "Provided by XIONG".Length, "Provided by XIONG");
            cManager.Title = (cManager.PanelWidth + 1).ToString() + ',' + (cManager.PanelHeight + 1);
            int row = 1, col = -1;
            for (int i = 0; i < StringBuffer.Length; i++) {
                if (++col == cManager.PanelWidth) {
                    row++;
                    col = 0;
                }
                if (StringBuffer[i] == '\n') {
                    row++;
                    if (row > cManager.PanelHeight - 1) {
                        cManager.SetCursorPosition(cManager.PanelHeight - 1, col);
                        StringBuffer.Remove(i, StringBuffer.Length - i);
                        return;
                    }
                    col = -1;
                }
                else if (row > cManager.PanelHeight - 1) {
                    cManager.SetCursorPosition(cManager.PanelHeight - 1, cManager.PanelWidth - 1);
                    StringBuffer.Remove(i, StringBuffer.Length - i);
                    return;
                }
                else {
                    cManager.DrawPanel(row, col, StringBuffer[i].ToString());
                }
            }
            cManager.SetCursorPosition(row, col + 1);
        }
        
        static void Main(string[] args) {
            Init();
        }
    }
}
