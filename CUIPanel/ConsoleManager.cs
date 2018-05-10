using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CUIPanel {
    /// <summary>主面板管理器，管理整个面板及缓冲区，子面板通过向此类递交请求间接控制其所有面板区域</summary>
    public class ConsoleManager {
        /// <summary>面板列数（宽度）内部存储</summary>
        private int _panelCol;
        /// <summary>获取面板宽度</summary>
        public int PanelWidth => _panelCol;

        /// <summary>面板行数（高度）内部存储</summary>
        private int _panelRow;
        /// <summary>获取面板高度</summary>
        public int PanelHeight => _panelRow;

        /// <summary>面板缓冲区内部存储，维度存储方式为 [ 行 , 列 ]，起始位置为左上角</summary>
        private char[,] _panelBuffer;
        /// <summary>获取面板缓冲区的浅表副本</summary>
        public char[,] PanelBuffer => (char[,])_panelBuffer.Clone();

        /// <summary>缓冲区互斥锁</summary>
        private SpinLock _globalLock = new SpinLock();

        /// <summary>当前面板默认前景色</summary>
        private readonly ConsoleColor _defaultfgColor = Console.ForegroundColor;
        /// <summary>当前面板默认背景色</summary>
        private readonly ConsoleColor _defaultbgColor = Console.BackgroundColor;
        /// <summary>面板前景色缓冲区内部存储</summary>
        private ConsoleColor[,] _fgColorSet;
        /// <summary>获取面板前景色缓冲区的浅表副本</summary>
        public ConsoleColor[,] FGColorSet => (ConsoleColor[,])_fgColorSet.Clone();
        /// <summary>面板背景色缓冲区内部存储</summary>
        private ConsoleColor[,] _bgColorSet;
        /// <summary>获取面板背景色缓冲区的浅表副本</summary>
        public ConsoleColor[,] BGColorSet => (ConsoleColor[,])_bgColorSet.Clone();

        /// <summary>面板刷新率内部存储</summary>
        private int _updateRate = 100;
        /// <summary>获取或设置面板刷新率（10-10000）</summary>
        public int UpdateRate {
            get => _updateRate;
            set {
                if (value < 10 || value > 10000)
                    throw new InvalidOperationException("无效的面板刷新率数值。");
                _updateRate = value;
            }
        }

        /// <summary>事件委托，提供标准事件处理方法规范。</summary>
        /// <param name="obj">传入事件的参数集合</param>
        public delegate void ConsoleManagerEventHandler(ConsoleManager cManager);
        
        /// <summary>缓冲区互斥锁</summary>
        private SpinLock _eventLock = new SpinLock();

        /// <summary>更新前触发事件内部存储</summary>
        private event ConsoleManagerEventHandler _beforeUpdate;
        /// <summary>更新前触发事件，参数指向当前 <see cref="ConsoleManager"/> 实例</summary>
        public event ConsoleManagerEventHandler BeforeUpdate {
            add {
                EnterLock(_eventLock);
                _beforeUpdate += value;
                ExitLock(_eventLock);
            }
            remove {
                EnterLock(_eventLock);
                _beforeUpdate -= value;
                ExitLock(_eventLock);
            }
        }
        /// <summary>更新时触发事件内部存储（仅内部可见）</summary>
        private event ConsoleManagerEventHandler _duringUpdate;
        /// <summary>更新时触发事件，参数指向当前 <see cref="ConsoleManager"/> 实例（仅内部可见）</summary>
        private event ConsoleManagerEventHandler DuringUpdate {
            add {
                EnterLock(_eventLock);
                _duringUpdate += value;
                ExitLock(_eventLock);
            }
            remove {
                EnterLock(_eventLock);
                _duringUpdate -= value;
                ExitLock(_eventLock);
            }
        }
        /// <summary>更新后触发事件内部存储</summary>
        private event ConsoleManagerEventHandler _afterUpdate;
        /// <summary>更新后触发事件，参数指向当前 <see cref="ConsoleManager"/> 实例</summary>
        public event ConsoleManagerEventHandler AfterUpdate {
            add {
                EnterLock(_eventLock);
                _afterUpdate += value;
                ExitLock(_eventLock);
            }
            remove {
                EnterLock(_eventLock);
                _afterUpdate -= value;
                ExitLock(_eventLock);
            }
        }
        /// <summary>窗口大小改变后触发事件内部存储</summary>
        private event ConsoleManagerEventHandler _afterResize;
        /// <summary>窗口大小改变后触发事件，参数指向当前 <see cref="ConsoleManager"/> 实例</summary>
        public event ConsoleManagerEventHandler AfterResize {
            add {
                EnterLock(_eventLock);
                _afterResize += value;
                ExitLock(_eventLock);
            }
            remove {
                EnterLock(_eventLock);
                _afterResize -= value;
                ExitLock(_eventLock);
            }
        }

        /// <summary>光标可见性内部存储</summary>
        private bool _cursorVisible = false;
        /// <summary>获取或设置一个值，用以指示光标是否可见（默认不可见）</summary>
        public bool CursorVisible {
            get => _cursorVisible;
            set => Console.CursorVisible = _cursorVisible = value;
        }

        /// <summary>指示面板缓冲区是否有过更改（供 <see cref="PassiveUpdate(ConsoleManager)"/> 方法使用）</summary>
        private bool _panelBufferChanged = false;

        /// <summary>指示是否使用被动刷新策略（默认不使用）</summary>
        private bool _usePassiveUpdate = false;
        /// <summary>获取或设置是否使用被动刷新策略（默认不使用）</summary>
        public bool UsePassiveUpdate {
            get => _usePassiveUpdate;
            set {
                if (value && !_usePassiveUpdate) {
                    EnterLock(_eventLock);
                    DuringUpdate -= ActiveUpdate;
                    DuringUpdate += PassiveUpdate;
                    ExitLock(_eventLock);
                } else if (!value && _usePassiveUpdate) {
                    EnterLock(_eventLock);
                    DuringUpdate -= PassiveUpdate;
                    DuringUpdate += ActiveUpdate;
                    ExitLock(_eventLock);
                }
                _usePassiveUpdate = value;
            }
        }

        /// <summary>窗口更新器线程句柄内部存储</summary>
        private Thread _consoleUpdaterHandler;
        
        /// <summary>
        ///     初始化类 <see cref="ConsoleManager"/> 的实例，此实例将接管 <see cref="Console"/> 的操作。<br/>
        ///     初始化此类后请勿直接操作 <see cref="Console"/> 类或者初始化第二个 <see cref="ConsoleManager"/> 类的实例，否则可能导致意料之外的错误。
        /// </summary>
        /// <exception cref="TimeoutException"/>
        public ConsoleManager() {
            InitPanelRowColSize();
            _panelBuffer = new char[_panelRow, _panelCol];
            _fgColorSet = new ConsoleColor[_panelRow, _panelCol];
            _bgColorSet = new ConsoleColor[_panelRow, _panelCol];
            for (int i = 0; i < PanelHeight; i++) {
                for (int j = 0; j < PanelWidth; j++) {
                    _fgColorSet[i, j] = _defaultfgColor;
                    _bgColorSet[i, j] = _defaultbgColor;
                }
            }
            DuringUpdate += ActiveUpdate;
            _consoleUpdaterHandler = new Thread(ConsoleUpdater);
            _consoleUpdaterHandler.Start();
        }

        /// <summary>控制台窗口更新器，以指定 <see cref="_updateRate"/> 刷新窗口内容，当窗口大小发生改变时对应更新面板大小。</summary>
        /// <exception cref="TimeoutException"/>
        private void ConsoleUpdater() {
            try {
                while (true) {
                    EnterLock(_globalLock);
                    EnterLock(_eventLock);
                    // 获得缓冲区锁，临界区开始

                    _beforeUpdate?.Invoke(this);
                    _duringUpdate?.Invoke(this);
                    _afterUpdate?.Invoke(this);

                    ExitLock(_eventLock);
                    ExitLock(_globalLock);
                    // 释放缓冲区锁，临界区结束
                    Thread.Sleep(_updateRate);
                }
            }
            catch (ThreadAbortException) {
                if (_globalLock.IsHeldByCurrentThread)
                    _globalLock.Exit();
            }
            catch (Exception e) {
                Console.Clear();
                Console.WriteLine(e);
                Console.WriteLine("按任意键继续...");
                Console.ReadKey();
                Environment.Exit(-1);
            }
        }

        /// <summary>根据 <see cref="PanelWidth"/> 和 <see cref="PanelHeight"/> 设定值初始化/重新初始化 <see cref="_panelBuffer"/> 的大小。</summary>
        private void InitPanelBufferSize() {
            try {
                char[,] tempBuffer = _panelBuffer;
                ConsoleColor[,] tempFG = _fgColorSet, tempBG = _bgColorSet;
                _panelBuffer = new char[PanelHeight, PanelWidth];
                _fgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
                _bgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
                for (int i = 0; i < PanelHeight; i++) {
                    for (int j = 0; j < PanelWidth; j++) {
                        _fgColorSet[i, j] = _defaultfgColor;
                        _bgColorSet[i, j] = _defaultbgColor;
                    }
                }
                for (var i = 0; i < (PanelHeight <= tempBuffer.GetLength(0) ? PanelHeight : tempBuffer.GetLength(0)); i++)
                    for (var j = 0; j < (PanelWidth <= tempBuffer.GetLength(1) ? PanelWidth : tempBuffer.GetLength(1)); j++)
                        _panelBuffer[i, j] = tempBuffer[i, j];
                for (var i = 0; i < (PanelHeight <= tempFG.GetLength(0) ? PanelHeight : tempFG.GetLength(0)); i++)
                    for (var j = 0; j < (PanelWidth <= tempFG.GetLength(1) ? PanelWidth : tempFG.GetLength(1)); j++)
                        _fgColorSet[i, j] = tempFG[i, j];
                for (var i = 0; i < (PanelHeight <= tempBG.GetLength(0) ? PanelHeight : tempBG.GetLength(0)); i++)
                    for (var j = 0; j < (PanelWidth <= tempBG.GetLength(1) ? PanelWidth : tempBG.GetLength(1)); j++)
                        _bgColorSet[i, j] = tempBG[i, j];
                EnterLock(_eventLock);
                _afterResize?.Invoke(this);
                ExitLock(_eventLock);
            } catch (Exception e) {
                Console.WriteLine(e);
                Environment.Exit(-1);
            }
        }
        
        /// <summary>以当前缓冲区内容及设置绘制整个窗口。</summary>
        private void DrawPanel() {
            Console.SetCursorPosition(0, 0);
            Queue<StringBuilder> sBuilders = new Queue<StringBuilder>();
            Queue<ConsoleColor> fgcList = new Queue<ConsoleColor>();
            Queue<ConsoleColor> bgcList = new Queue<ConsoleColor>();
            StringBuilder sBuilder = new StringBuilder();
            ConsoleColor cFGC = _defaultfgColor, cBGC = _defaultbgColor;
            for (int i = 0; i < PanelHeight; i++) {
                for (int j = 0; j < PanelWidth; j++) {
                    if (cFGC != _fgColorSet[i, j] || cBGC != _bgColorSet[i, j]) {
                        sBuilders.Enqueue(sBuilder);
                        fgcList.Enqueue(cFGC);
                        bgcList.Enqueue(cBGC);
                        sBuilder = new StringBuilder();
                        cFGC = _fgColorSet[i, j];
                        cBGC = _bgColorSet[i, j];
                    }
                    sBuilder.Append(_panelBuffer[i, j]);
                }
                sBuilder.Append('\n');
            }
            sBuilders.Enqueue(sBuilder);
            fgcList.Enqueue(cFGC);
            bgcList.Enqueue(cBGC);

            // 输出缓冲区中所有待输出内容
            while (sBuilders.Count != 0) {
                Console.ForegroundColor = fgcList.Dequeue();
                Console.BackgroundColor = bgcList.Dequeue();
                Console.Write(sBuilders.Dequeue());
            }

            // 尝试将控制台其他部分的颜色置为默认色（在某些情况下可能部分生效或不生效）
            Console.ResetColor();
            Console.Write('\0');
        }

        /// <summary>以指定位置为左上角绘制矩形区域并在下一次刷新时更新，超出部分将不会绘制。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <exception cref="ArgumentOutOfRangeException"/>
        /// <exception cref="TimeoutException"/>
        public void DrawPanel(int startRow, int startCol, char[,] content) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));

            EnterLock(_globalLock);
            // 获得缓冲区锁，临界区开始
            for (int i = startRow; i < (content.GetLength(0) + startRow >= PanelHeight ? PanelHeight : content.GetLength(0) + startRow); i++)
                for (int j = startCol; j < (content.GetLength(1) + startCol >= PanelWidth ? PanelWidth : content.GetLength(1) + startCol); j++)
                    _panelBuffer[i, j] = content[i - startRow, j - startCol];
            ExitLock(_globalLock);
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
        /// <exception cref="ArgumentOutOfRangeException"/>
        /// <exception cref="TimeoutException"/>
        public void DrawPanel(int startRow, int startCol, int endRow, int endCol, ConsoleColor foregroundColor, ConsoleColor backgroundColor) {
            if (startRow < 0 || startRow > endRow)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol > endCol)
                throw new ArgumentOutOfRangeException(nameof(startCol));
            if (endRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(endRow));
            if (endCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(endCol));

            EnterLock(_globalLock);
            // 获得缓冲区锁，临界区开始

            for (int i = startRow; i <= endRow; i++) {
                for (int j = startCol; j <= endCol; j++) {
                    _fgColorSet[i, j] = foregroundColor;
                    _bgColorSet[i, j] = backgroundColor;
                }
            }

            ExitLock(_globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }


        /// <summary>以指定位置为左上角并指定前景背景色绘制矩形区域并在下一次刷新时更新，超出部分将不会绘制。</summary>
        /// <param name="startRow">起始位置行号</param>
        /// <param name="startCol">起始位置列号</param>
        /// <param name="content">绘制内容</param>
        /// <param name="foregroundColor">前景色</param>
        /// <param name="backgroundColor">背景色</param>
        /// <exception cref="ArgumentOutOfRangeException"/>
        /// <exception cref="TimeoutException"/>
        public void DrawPanel(int startRow, int startCol, char[,] content, ConsoleColor foregroundColor, ConsoleColor backgroundColor) {
            if (startRow < 0 || startRow >= PanelHeight)
                throw new ArgumentOutOfRangeException(nameof(startRow));
            if (startCol < 0 || startCol >= PanelWidth)
                throw new ArgumentOutOfRangeException(nameof(startCol));

            EnterLock(_globalLock);
            // 获得缓冲区锁，临界区开始

            for (int i = startRow; i < (content.GetLength(0) + startRow >= PanelHeight ? PanelHeight : content.GetLength(0) + startRow); i++)
                for (int j = startCol; j < (content.GetLength(1) + startCol >= PanelWidth ? PanelWidth : content.GetLength(1) + startCol); j++)
                    _panelBuffer[i, j] = content[i - startRow, j - startCol];
            for (int i = startRow; i < (content.GetLength(0) + startRow >= PanelHeight ? PanelHeight : content.GetLength(0) + startRow); i++) {
                for (int j = startCol; j < (content.GetLength(1) + startCol >= PanelWidth ? PanelWidth : content.GetLength(1) + startCol); j++) {
                    _fgColorSet[i, j] = foregroundColor;
                    _bgColorSet[i, j] = backgroundColor;
                }
            }

            ExitLock(_globalLock);
            // 释放缓冲区锁，临界区结束

            _panelBufferChanged = true;
        }

        /// <summary>设置窗口的大小。</summary>
        /// <param name="width">窗口宽度</param>
        /// <param name="height">窗口高度</param>
        public void SetWindowSize(int width, int height) {
            Console.SetWindowSize(width, height);
        }

        /// <summary>清空当前窗口的所有输出并复位缓冲区。</summary>
        public void Clear() {
            InitPanelRowColSize();
            _panelBuffer = new char[PanelHeight, PanelWidth];
            _fgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
            _bgColorSet = new ConsoleColor[PanelHeight, PanelWidth];
            for (int i = 0; i < PanelHeight; i++) {
                for (int j = 0; j < PanelWidth; j++) {
                    _fgColorSet[i, j] = _defaultfgColor;
                    _bgColorSet[i, j] = _defaultbgColor;
                }
            }
            DrawPanel();
        }

        /// <summary>结束当前的 <see cref="ConsoleManager"/> 实例运行。</summary>
        public void Exit() {
            _consoleUpdaterHandler.Abort();
            Clear();
        }

        /// <summary>获取控制台窗口大小并赋值给面板行数与列数，并返回一个值指示窗口大小是否已更改。</summary>
        /// <returns>返回 true 表示窗口大小已更改，返回 false 表示窗口大小未更改。</returns>
        private bool InitPanelRowColSize() {
            if (_panelCol != Console.WindowWidth - 1 || _panelRow != Console.WindowHeight - 1) {
                Console.Clear();
                Console.ResetColor();
                Console.CursorVisible = _cursorVisible;
                _panelCol = Console.WindowWidth - 1;
                _panelRow = Console.WindowHeight - 1;
                return true;
            }
            return false;
        }

        /// <summary>主动更新方法，强制在每个更新周期刷新窗口内容。</summary>
        /// <param name="cManager">传入参数，指向当前 <see cref="ConsoleManager"/> 实例（不使用）</param>
        private void ActiveUpdate(ConsoleManager cManager) {
            if (InitPanelRowColSize())
                InitPanelBufferSize();
            DrawPanel();
        }

        /// <summary>被动更新方法，强制在每个更新周期刷新窗口内容。</summary>
        /// <param name="cManager">传入参数，指向当前 <see cref="ConsoleManager"/> 实例（不使用）</param>
        private void PassiveUpdate(ConsoleManager cManager) {
            if (InitPanelRowColSize()) {
                InitPanelBufferSize();
                DrawPanel();
            } else if (_panelBufferChanged) {
                DrawPanel();
                _panelBufferChanged = false;
            }
        }

        /// <summary>获取指定对象上的互斥锁。</summary>
        /// <param name="spinLock">要获取锁的对象</param>
        /// <returns>指示当前线程是否已持有该锁</returns>
        /// <exception cref="TimeoutException"/>
        private bool EnterLock(SpinLock spinLock) {
            bool lockToken = false;
            spinLock.TryEnter(_updateRate * 30 <= 30000 ? 30000 : _updateRate * 30, ref lockToken);
            if (!lockToken)
                throw new TimeoutException("尝试获取锁的时间过长。");
            return spinLock.IsHeldByCurrentThread;
        }

        /// <summary>释放指定对象上的互斥锁。</summary>
        /// <param name="spinLock">要释放锁的对象</param>
        /// <returns>指示当前线程是否释放了锁</returns>
        private bool ExitLock(SpinLock spinLock) {
            if (spinLock.IsHeldByCurrentThread) {
                spinLock.Exit();
                return !spinLock.IsHeldByCurrentThread;
            } else
                return false;
        }
    }
}