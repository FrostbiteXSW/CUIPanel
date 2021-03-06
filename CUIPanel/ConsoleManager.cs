﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CUIPanel {
    /// <summary>主面板管理器，管理整个面板及缓冲区，子面板通过向此类递交请求间接控制其所有面板区域</summary>
    public class ConsoleManager {
        /// <summary>事件委托，提供标准事件处理方法规范。</summary>
        /// <param name="cManager">传入事件对应的 <see cref="ConsoleManager" /> 实例</param>
        public delegate void ConsoleManagerEventHandler(ConsoleManager cManager);

        /// <summary>窗口更新器线程句柄内部存储。</summary>
        private readonly Thread _consoleUpdaterHandler;

        /// <summary>按键监视器器线程句柄内部存储。</summary>
        private readonly Thread _keyPressMonitorHandler;

        /// <summary>当前面板默认背景色。</summary>
        public readonly ConsoleColor DefaultbgColor = Console.BackgroundColor;

        /// <summary>当前面板默认前景色。</summary>
        public readonly ConsoleColor DefaultfgColor = Console.ForegroundColor;

        /// <summary>面板背景色缓冲区内部存储。</summary>
        private ConsoleColor[,] _bgColorSet;

        /// <summary>光标可见性内部存储。</summary>
        private bool _cursorVisible;

        /// <summary>面板前景色缓冲区内部存储。</summary>
        private ConsoleColor[,] _fgColorSet;

        /// <summary>全局互斥锁。</summary>
        private SpinLock _globalLock = new SpinLock(true);

        /// <summary>面板缓冲区内部存储，维度存储方式为 [ 行 , 列 ]，起始位置为左上角。</summary>
        private char[,] _panelBuffer;

        /// <summary>指示面板缓冲区是否有过更改（供 <see cref="PassiveUpdate(ConsoleManager)" /> 方法使用）。</summary>
        private bool _panelBufferChanged;

        /// <summary>面板刷新率内部存储。</summary>
        private int _updateRate = 100;

        /// <summary>指示是否使用被动刷新策略（默认不使用）。</summary>
        private bool _usePassiveUpdate;

        /// <summary>
        ///     初始化类 <see cref="ConsoleManager" /> 的实例，此实例将接管 <see cref="Console" /> 的操作。<br />
        ///     初始化此类后请勿直接操作 <see cref="Console" /> 类或者初始化第二个 <see cref="ConsoleManager" /> 类的实例，否则可能导致意料之外的错误。
        /// </summary>
        /// <exception cref="TimeoutException" />
        public ConsoleManager() {
            // 初始化控制台
            Console.Clear();
            Console.ResetColor();
            Console.CursorVisible = _cursorVisible;

            // 初始化面板大小
            PanelWidth = Console.WindowWidth - 1;
            PanelHeight = Console.WindowHeight - 1;

            // 初始化内部缓冲区
            _panelBuffer = new char[PanelHeight, PanelWidth];
            _fgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
            _bgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
            for (var i = 0; i < PanelHeight; i++) {
                for (var j = 0; j < PanelWidth; j++) {
                    _fgColorSet[i, j] = DefaultfgColor;
                    _bgColorSet[i, j] = DefaultbgColor;
                }
            }

            // 添加更新策略
            DuringUpdate += ActiveUpdate;

            // 暂停更新事务，开始线程初始化
            IsPaused = true;

            _consoleUpdaterHandler = new Thread(ConsoleUpdater);
            _consoleUpdaterHandler.Start();
            _keyPressMonitorHandler = new Thread(KeyPressMonitor);
            _keyPressMonitorHandler.Start();

            // 所有初始化完成，恢复更新事务
            IsPaused = false;
        }

        /// <summary>获取面板列数（窗口列数减一）。</summary>
        public int PanelWidth { get; private set; }

        /// <summary>获取面板行数（窗口行数减一）。</summary>
        public int PanelHeight { get; private set; }

        /// <summary>根据当前字体和屏幕分辨率获取缓冲区可能具有的最大列数（控制台窗口可能具有的最大列数减一）。</summary>
        public int LargestPanelWidth => Console.LargestWindowWidth - 1;

        /// <summary>根据当前字体和屏幕分辨率获取缓冲区可能具有的最大行数（控制台窗口可能具有的最大行数减一）。</summary>
        public int LargestPanelHeight => Console.LargestWindowHeight - 1;

        /// <summary>获取面板缓冲区的浅表副本。</summary>
        public char[,] PanelBuffer => (char[,]) _panelBuffer.Clone();

        /// <summary>获取面板前景色缓冲区的浅表副本。</summary>
        public ConsoleColor[,] FgColorSet => (ConsoleColor[,]) _fgColorSet.Clone();

        /// <summary>获取面板背景色缓冲区的浅表副本。</summary>
        public ConsoleColor[,] BgColorSet => (ConsoleColor[,]) _bgColorSet.Clone();

        /// <summary>获取或设置面板刷新率（10-10000）。</summary>
        public int UpdateRate {
            get => _updateRate;
            set {
                if (value < 10 || value > 10000)
                    throw new InvalidOperationException("无效的面板刷新率数值。");
                _updateRate = value;
            }
        }

        /// <summary>获取或设置一个值，用以指示光标是否可见（默认不可见）。</summary>
        public bool CursorVisible {
            get => _cursorVisible;
            set => Console.CursorVisible = _cursorVisible = value;
        }

        /// <summary>获取或设置是否使用被动刷新策略（默认不使用）。</summary>
        public bool UsePassiveUpdate {
            get => _usePassiveUpdate;
            set {
                if (value && !_usePassiveUpdate) {
                    DuringUpdate -= ActiveUpdate;
                    DuringUpdate += PassiveUpdate;
                }
                else if (!value && _usePassiveUpdate) {
                    DuringUpdate -= PassiveUpdate;
                    DuringUpdate += ActiveUpdate;
                }
                _usePassiveUpdate = value;
            }
        }

        /// <summary>获取或设置一个值，指示控制台窗口更新器是否需要暂停一切更新操作。</summary>
        public bool IsPaused { get; set; }

        /// <summary>获取或设置要显示在控制台标题栏中的标题。</summary>
        public string Title {
            get => Console.Title;
            set => Console.Title = value;
        }

        /// <summary>控制台窗口更新器，以指定 <see cref="_updateRate" /> 刷新窗口内容，当窗口大小发生改变时对应更新面板大小。</summary>
        /// <exception cref="TimeoutException" />
        /// <exception cref="ThreadAbortException" />
        private void ConsoleUpdater() {
            try {
                while (true) {
                    Thread.Sleep(_updateRate);

                    if (IsPaused)
                        continue;

                    EnterLock(ref _globalLock);
                    // 获得缓冲区锁，临界区开始

                    _beforeUpdate?.Invoke(this);
                    _duringUpdate?.Invoke(this);
                    _afterUpdate?.Invoke(this);

                    ExitLock(ref _globalLock);
                    // 释放缓冲区锁，临界区结束
                }
            }
            catch (ThreadAbortException) {
                ExitLock(ref _globalLock);
            }
            catch (Exception e) {
                Console.Clear();
                Console.WriteLine(e);
                Console.WriteLine("按任意键继续...");
                Console.ReadKey();
                Environment.Exit(-1);
            }
        }

        /// <summary>按键监视器，当捕获按键后触发对应事件。</summary>
        /// <exception cref="TimeoutException" />
        /// <exception cref="ThreadAbortException" />
        private void KeyPressMonitor() {
            try {
                while (true) {
                    Thread.Sleep(_updateRate);
                    var keyInfo = Console.ReadKey(true);
                    if (IsPaused)
                        continue;

                    EnterLock(ref _globalLock);
                    // 获得缓冲区锁，临界区开始

                    _keyPressed?.Invoke(this, keyInfo);

                    ExitLock(ref _globalLock);
                    // 释放缓冲区锁，临界区结束
                }
            }
            catch (ThreadAbortException) {
                ExitLock(ref _globalLock);
            }
            catch (Exception e) {
                Console.Clear();
                Console.WriteLine(e);
                Console.WriteLine("按任意键继续...");
                Console.ReadKey();
                Environment.Exit(-1);
            }
        }

        /// <summary>以当前缓冲区内容及设置绘制整个窗口。</summary>
        private void DrawPanel() {
            Console.CursorVisible = false;
            int left = Console.CursorLeft, top = Console.CursorTop;
            Console.SetCursorPosition(0, 0);
            var sBuilders = new Queue<StringBuilder>();
            var fgcList = new Queue<ConsoleColor>();
            var bgcList = new Queue<ConsoleColor>();
            var sBuilder = new StringBuilder();
            ConsoleColor cFgc = DefaultfgColor, cBgc = DefaultbgColor;
            for (var i = 0;;) {
                for (var j = 0; j < PanelWidth; j++) {
                    if (cFgc != _fgColorSet[i, j] || cBgc != _bgColorSet[i, j]) {
                        if (sBuilder.Length > 0) {
                            sBuilders.Enqueue(sBuilder);
                            fgcList.Enqueue(cFgc);
                            bgcList.Enqueue(cBgc);
                        }
                        sBuilder = new StringBuilder();
                        cFgc = _fgColorSet[i, j];
                        cBgc = _bgColorSet[i, j];
                    }
                    sBuilder.Append(_panelBuffer[i, j]);
                }
                if (i++ != PanelHeight - 1)
                    sBuilder.Append('\n');
                else
                    break;
            }
            if (sBuilder.Length > 0) {
                sBuilders.Enqueue(sBuilder);
                fgcList.Enqueue(cFgc);
                bgcList.Enqueue(cBgc);
            }

            // 输出缓冲区中所有待输出内容
            while (sBuilders.Count != 0) {
                Console.ForegroundColor = fgcList.Dequeue();
                Console.BackgroundColor = bgcList.Dequeue();
                Console.Write(sBuilders.Dequeue());
            }

            // 尝试将控制台其他部分的颜色置为默认色（在某些情况下可能部分生效或不生效）
            Console.ResetColor();
            Console.Write('\0');

            Console.SetCursorPosition(left >= Console.WindowWidth - 2 ? Console.WindowWidth - 2 : left,
                top >= Console.WindowHeight - 2 ? Console.WindowHeight - 2 : top);
            Console.CursorVisible = _cursorVisible;
        }

        /// <summary>以指定位置为左上角和右下角设置矩形区域的绘制内容并在下一次刷新时更新。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="endRow">结束位置行号</param>
        /// <param name="endCol">结束位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, int endRow, int endCol, char content) {
            if (startRow < 0 || startRow > endRow)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol > endCol)
                throw new ArgumentOutOfRangeException(nameof(startCol));
            if (endRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(endRow));
            if (endCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(endCol));

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startRow; i <= endRow; i++)
                for (var j = startCol; j <= endCol; j++)
                    _panelBuffer[i, j] = content;

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>以指定位置为左上角和右下角设置矩形区域的前景背景色并在下一次刷新时更新。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="endRow">结束位置行号</param>
        /// <param name="endCol">结束位置列号</param>
        /// <param name="foregroundColor">前景色</param>
        /// <param name="backgroundColor">背景色</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, int endRow, int endCol, ConsoleColor foregroundColor, ConsoleColor backgroundColor) {
            if (startRow < 0 || startRow > endRow)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol > endCol)
                throw new ArgumentOutOfRangeException(nameof(startCol));
            if (endRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(endRow));
            if (endCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(endCol));

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startRow; i <= endRow; i++) {
                for (var j = startCol; j <= endCol; j++) {
                    _fgColorSet[i, j] = foregroundColor;
                    _bgColorSet[i, j] = backgroundColor;
                }
            }

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>以指定位置为左上角和右下角设置矩形区域的前景背景色并在下一次刷新时更新。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="endRow">结束位置行号</param>
        /// <param name="endCol">结束位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <param name="foregroundColor">前景色</param>
        /// <param name="backgroundColor">背景色</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, int endRow, int endCol, char content, ConsoleColor foregroundColor, ConsoleColor backgroundColor) {
            if (startRow < 0 || startRow > endRow)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol > endCol)
                throw new ArgumentOutOfRangeException(nameof(startCol));
            if (endRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(endRow));
            if (endCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(endCol));

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startRow; i <= endRow; i++) {
                for (var j = startCol; j <= endCol; j++) {
                    _panelBuffer[i, j] = content;
                    _fgColorSet[i, j] = foregroundColor;
                    _bgColorSet[i, j] = backgroundColor;
                }
            }

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>以指定位置为左上角绘制矩形区域并在下一次刷新时更新，超出部分将不会绘制。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, char[,] content) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startRow; i < (content.GetLength(0) + startRow >= PanelHeight ? PanelHeight : content.GetLength(0) + startRow); i++)
                for (var j = startCol; j < (content.GetLength(1) + startCol >= PanelWidth ? PanelWidth : content.GetLength(1) + startCol); j++)
                    _panelBuffer[i, j] = content[i - startRow, j - startCol];

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>以指定位置为左上角设置矩形区域的前景背景色并在下一次刷新时更新。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="foregroundColor">前景色</param>
        /// <param name="backgroundColor">背景色</param>
        public void DrawPanel(int startRow, int startCol, ConsoleColor[,] foregroundColor, ConsoleColor[,] backgroundColor) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));
            if (foregroundColor.GetLength(0) != backgroundColor.GetLength(0) || foregroundColor.GetLength(1) != backgroundColor.GetLength(1))
                throw new InvalidOperationException("矩阵的大小不一致。");

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startRow; i < (foregroundColor.GetLength(0) + startRow >= PanelHeight ? PanelHeight : foregroundColor.GetLength(0) + startRow); i++)
                for (var j = startCol; j < (foregroundColor.GetLength(1) + startCol >= PanelWidth ? PanelWidth : foregroundColor.GetLength(1) + startCol); j++) {
                    _fgColorSet[i, j] = foregroundColor[i - startRow, j - startCol];
                    _bgColorSet[i, j] = backgroundColor[i - startRow, j - startCol];
                }

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>以指定位置为左上角并指定前景背景色绘制矩形区域并在下一次刷新时更新，超出部分将不会绘制。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <param name="foregroundColor">前景色</param>
        /// <param name="backgroundColor">背景色</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, char[,] content, ConsoleColor foregroundColor, ConsoleColor backgroundColor) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startRow; i < (content.GetLength(0) + startRow >= PanelHeight ? PanelHeight : content.GetLength(0) + startRow); i++)
                for (var j = startCol; j < (content.GetLength(1) + startCol >= PanelWidth ? PanelWidth : content.GetLength(1) + startCol); j++)
                    _panelBuffer[i, j] = content[i - startRow, j - startCol];
            for (var i = startRow; i < (content.GetLength(0) + startRow >= PanelHeight ? PanelHeight : content.GetLength(0) + startRow); i++) {
                for (var j = startCol; j < (content.GetLength(1) + startCol >= PanelWidth ? PanelWidth : content.GetLength(1) + startCol); j++) {
                    _fgColorSet[i, j] = foregroundColor;
                    _bgColorSet[i, j] = backgroundColor;
                }
            }

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>以指定位置为左上角并指定前景背景色绘制矩形区域并在下一次刷新时更新，超出部分将不会绘制。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <param name="foregroundColor">前景色</param>
        /// <param name="backgroundColor">背景色</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, char[,] content, ConsoleColor[,] foregroundColor, ConsoleColor[,] backgroundColor) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));
            if (foregroundColor.GetLength(0) != backgroundColor.GetLength(0) || foregroundColor.GetLength(1) != backgroundColor.GetLength(1) ||
                content.GetLength(0) != backgroundColor.GetLength(0) || content.GetLength(1) != backgroundColor.GetLength(1))
                throw new InvalidOperationException("矩阵的大小不一致。");

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startRow; i < (content.GetLength(0) + startRow >= PanelHeight ? PanelHeight : content.GetLength(0) + startRow); i++)
                for (var j = startCol; j < (content.GetLength(1) + startCol >= PanelWidth ? PanelWidth : content.GetLength(1) + startCol); j++) {
                    _panelBuffer[i, j] = content[i - startRow, j - startCol];
                    _fgColorSet[i, j] = foregroundColor[i - startRow, j - startCol];
                    _bgColorSet[i, j] = backgroundColor[i - startRow, j - startCol];
                }

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>以指定位置为左上角绘制矩形区域并在下一次刷新时更新，超出部分将不会绘制。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, string content) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            for (var i = startCol; i < (content.Length + startCol >= PanelWidth ? PanelWidth : content.Length + startCol); i++)
                _panelBuffer[startRow, i] = content[i - startCol];

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }


        /// <summary>以指定位置为左上角并指定前景背景色绘制矩形区域并在下一次刷新时更新，超出部分将不会绘制。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <param name="foregroundColor">前景色</param>
        /// <param name="backgroundColor">背景色</param>
        /// <exception cref="ArgumentOutOfRangeException" />
        /// <exception cref="TimeoutException" />
        public void DrawPanel(int startRow, int startCol, string content, ConsoleColor foregroundColor, ConsoleColor backgroundColor) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));

            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始


            for (var i = startCol; i < (content.Length + startCol >= PanelWidth ? PanelWidth : content.Length + startCol); i++) {
                _panelBuffer[startRow, i] = content[i - startCol];
                _fgColorSet[startRow, i] = foregroundColor;
                _bgColorSet[startRow, i] = backgroundColor;
            }

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>
        ///     获取控制台窗口大小，并返回一个值指示窗口大小是否已更改。
        ///     若窗口大小发生变化，则赋值给面板行数与列数，更新面板缓冲区大小，并唤醒窗口大小改变后触发事件。
        /// </summary>
        /// <returns>返回 true 表示窗口大小已更改，返回 false 表示窗口大小未更改。</returns>
        private bool InitPanelSize() {
            if (PanelWidth == Console.WindowWidth - 1 && PanelHeight == Console.WindowHeight - 1) return false;

            // 确保窗口大小调整是通过鼠标拖动时能够延迟更新
            int tempWidth, tempHeight;
            do {
                tempWidth = Console.WindowWidth;
                tempHeight = Console.WindowHeight;
                Thread.Sleep(_updateRate >= 100 ? _updateRate : 100);
            } while (tempWidth != Console.WindowWidth || tempHeight != Console.WindowHeight);

            Console.Clear();
            Console.ResetColor();
            Console.CursorVisible = _cursorVisible;
            PanelWidth = Console.WindowWidth - 1;
            PanelHeight = Console.WindowHeight - 1;

            try {
                var tempBuffer = _panelBuffer;
                ConsoleColor[,] tempFg = _fgColorSet, tempBg = _bgColorSet;
                _panelBuffer = new char[PanelHeight, PanelWidth];
                _fgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
                _bgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
                for (var i = 0; i < PanelHeight; i++) {
                    for (var j = 0; j < PanelWidth; j++) {
                        _fgColorSet[i, j] = DefaultfgColor;
                        _bgColorSet[i, j] = DefaultbgColor;
                    }
                }

                for (var i = 0; i < (PanelHeight <= tempBuffer.GetLength(0) ? PanelHeight : tempBuffer.GetLength(0)); i++)
                    for (var j = 0; j < (PanelWidth <= tempBuffer.GetLength(1) ? PanelWidth : tempBuffer.GetLength(1)); j++)
                        _panelBuffer[i, j] = tempBuffer[i, j];
                for (var i = 0; i < (PanelHeight <= tempFg.GetLength(0) ? PanelHeight : tempFg.GetLength(0)); i++)
                    for (var j = 0; j < (PanelWidth <= tempFg.GetLength(1) ? PanelWidth : tempFg.GetLength(1)); j++)
                        _fgColorSet[i, j] = tempFg[i, j];
                for (var i = 0; i < (PanelHeight <= tempBg.GetLength(0) ? PanelHeight : tempBg.GetLength(0)); i++)
                    for (var j = 0; j < (PanelWidth <= tempBg.GetLength(1) ? PanelWidth : tempBg.GetLength(1)); j++)
                        _bgColorSet[i, j] = tempBg[i, j];

                Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);
            }
            catch (Exception e) {
                Console.WriteLine(e);
                Environment.Exit(-1);
            }

            // 唤醒窗口大小改变后触发事件
            _afterResize?.Invoke(this);

            return true;
        }

        /// <summary>设置窗口的大小，缓冲区实际长宽为窗口长宽减一。</summary>
        /// <param name="width">窗口宽度</param>
        /// <param name="height">窗口高度</param>
        public void SetWindowSize(int width, int height) {
            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            Console.SetWindowSize(width, height);
            InitPanelSize();
            DrawPanel();

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束
        }

        /// <summary>设置光标位置。</summary>
        /// <param name="row">光标的行位置。从上到下，从 0 开始为行编号。</param>
        /// <param name="col">光标的列位置。将从 0 开始从左到右对列进行编号。</param>
        public void SetCursorPosition(int row, int col) {
            EnterLock(ref _globalLock);
            // 获得缓冲区锁，临界区开始

            Console.SetCursorPosition(col, row);

            ExitLock(ref _globalLock);
            // 释放缓冲区锁，临界区结束
        }

        /// <summary>主动更新方法，强制在每个更新周期刷新窗口内容。</summary>
        /// <param name="cManager">传入参数，指向当前 <see cref="ConsoleManager" /> 实例（不使用）</param>
        private void ActiveUpdate(ConsoleManager cManager) {
            InitPanelSize();
            DrawPanel();
        }

        /// <summary>被动更新方法，只有当缓冲区发生变化时刷新窗口内容。</summary>
        /// <param name="cManager">传入参数，指向当前 <see cref="ConsoleManager" /> 实例（不使用）</param>
        private void PassiveUpdate(ConsoleManager cManager) {
            if (InitPanelSize()) {
                DrawPanel();
            }
            else if (_panelBufferChanged) {
                DrawPanel();
                _panelBufferChanged = false;
            }
        }

        /// <summary>清空当前窗口的所有输出并复位缓冲区。</summary>
        public void Clear() {
            InitPanelSize();
            _panelBuffer = new char[PanelHeight, PanelWidth];
            _fgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
            _bgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
            for (var i = 0; i < PanelHeight; i++) {
                for (var j = 0; j < PanelWidth; j++) {
                    _fgColorSet[i, j] = DefaultfgColor;
                    _bgColorSet[i, j] = DefaultbgColor;
                }
            }
            DrawPanel();
        }

        /// <summary>结束当前的 <see cref="ConsoleManager" /> 实例运行。</summary>
        public void Exit() {
            _consoleUpdaterHandler.Abort();
            _keyPressMonitorHandler.Abort();
            Clear();
        }

        /// <summary>获取指定对象上的互斥锁。</summary>
        /// <param name="spinLock">要获取锁的对象</param>
        /// <returns>指示当前线程是否已持有该锁</returns>
        /// <exception cref="TimeoutException" />
        private void EnterLock(ref SpinLock spinLock) {
            if (spinLock.IsHeldByCurrentThread) return;
            var lockToken = false;
            spinLock.TryEnter(_updateRate * 30 <= 30000 ? 30000 : _updateRate * 30, ref lockToken);
            if (!lockToken)
                throw new TimeoutException("尝试获取锁的时间过长。");
        }

        /// <summary>释放指定对象上的互斥锁。</summary>
        /// <param name="spinLock">要释放锁的对象</param>
        /// <returns>指示当前线程是否释放了锁</returns>
        private void ExitLock(ref SpinLock spinLock) {
            if (spinLock.IsHeldByCurrentThread)
                spinLock.Exit();
        }

        // 由于命名冲突，从此处开始取消ReSharper的命名规则检查
        // ReSharper disable InconsistentNaming

        /// <summary>更新前触发事件内部存储。</summary>
        private event ConsoleManagerEventHandler _beforeUpdate;

        /// <summary>更新前触发事件，参数指向当前 <see cref="ConsoleManager" /> 实例。</summary>
        public event ConsoleManagerEventHandler BeforeUpdate {
            add {
                EnterLock(ref _globalLock);
                _beforeUpdate += value;
                ExitLock(ref _globalLock);
            }
            remove {
                EnterLock(ref _globalLock);
                _beforeUpdate -= value;
                ExitLock(ref _globalLock);
            }
        }

        /// <summary>更新时触发事件内部存储（仅内部可见）。</summary>
        private event ConsoleManagerEventHandler _duringUpdate;

        /// <summary>更新时触发事件，参数指向当前 <see cref="ConsoleManager" /> 实例（仅内部可见）。</summary>
        private event ConsoleManagerEventHandler DuringUpdate {
            add {
                EnterLock(ref _globalLock);
                _duringUpdate += value;
                ExitLock(ref _globalLock);
            }
            remove {
                EnterLock(ref _globalLock);
                _duringUpdate -= value;
                ExitLock(ref _globalLock);
            }
        }

        /// <summary>更新后触发事件内部存储。</summary>
        private event ConsoleManagerEventHandler _afterUpdate;

        /// <summary>更新后触发事件，参数指向当前 <see cref="ConsoleManager" /> 实例。</summary>
        public event ConsoleManagerEventHandler AfterUpdate {
            add {
                EnterLock(ref _globalLock);
                _afterUpdate += value;
                ExitLock(ref _globalLock);
            }
            remove {
                EnterLock(ref _globalLock);
                _afterUpdate -= value;
                ExitLock(ref _globalLock);
            }
        }

        /// <summary>窗口大小改变后触发事件内部存储。</summary>
        private event ConsoleManagerEventHandler _afterResize;

        /// <summary>窗口大小改变后触发事件，参数指向当前 <see cref="ConsoleManager" /> 实例。</summary>
        public event ConsoleManagerEventHandler AfterResize {
            add {
                EnterLock(ref _globalLock);
                _afterResize += value;
                ExitLock(ref _globalLock);
            }
            remove {
                EnterLock(ref _globalLock);
                _afterResize -= value;
                ExitLock(ref _globalLock);
            }
        }

        /// <summary>事件委托，提供按键响应事件处理方法规范。</summary>
        /// <param name="cManager">传入事件对应的 <see cref="ConsoleManager" /> 实例</param>
        /// <param name="keyInfo">传入事件对应的按键信息</param>
        public delegate void KeyPressEventHandler(ConsoleManager cManager, ConsoleKeyInfo keyInfo);

        /// <summary>按键响应事件内部存储。</summary>
        private event KeyPressEventHandler _keyPressed;

        /// <summary>按键响应事件，参数指向当前 <see cref="ConsoleManager" /> 实例及按键信息。</summary>
        public event KeyPressEventHandler KeyPressed {
            add {
                EnterLock(ref _globalLock);
                _keyPressed += value;
                ExitLock(ref _globalLock);
            }
            remove {
                EnterLock(ref _globalLock);
                _keyPressed -= value;
                ExitLock(ref _globalLock);
            }
        }

        // 恢复ReSharper的命名规则检查
        // ReSharper restore InconsistentNaming
    }
}