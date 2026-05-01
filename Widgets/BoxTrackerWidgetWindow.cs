using BO2.Services;
using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.UI;

namespace BO2.Widgets
{
    internal sealed class BoxTrackerWidgetWindow
    {
        private const string WindowClassName = "BO2BoxTrackerWidgetWindow";
        private const string WindowTitle = "Box Tracker";
        private const int DefaultX = 100;
        private const int DefaultY = 100;
        private const int Padding = 12;
        private const int TransparentColorKey = 0x00FF00FF;
        private const int GwlExStyle = -20;
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsExLayered = 0x00080000;
        private const int SwShow = 5;
        private const int WmPaint = 0x000F;
        private const int WmClose = 0x0010;
        private const int WmDestroy = 0x0002;
        private const int WmEraseBkgnd = 0x0014;
        private const int WmLButtonDown = 0x0201;
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;
        private const int StockDefaultGuiFont = 17;
        private const int TransparentBkMode = 1;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint LwaColorKey = 0x00000001;
        private const uint DtCenter = 0x00000001;
        private const uint DtLeft = 0x00000000;
        private const uint DtTop = 0x00000000;
        private const uint DtWordBreak = 0x00000010;
        private const uint DtNoPrefix = 0x00000800;
        private const uint DtCalcRect = 0x00000400;
        private const int IdcArrow = 32512;

        private static readonly WndProcDelegate s_wndProc = WndProc;
        private static readonly Dictionary<nint, BoxTrackerWidgetWindow> s_windows = new();
        private static readonly object s_windowsLock = new();
        private static bool s_classRegistered;

        private nint _windowHandle;
        private string _text = string.Empty;
        private Color _backgroundColor = Colors.White;
        private Color _textColor = Colors.Black;
        private bool _transparentBackground;
        private bool _centerAlign = true;
        private bool _closed;

        public BoxTrackerWidgetWindow()
        {
            EnsureWindowClass();

            nint hInstance = GetModuleHandle(null);
            _windowHandle = CreateWindowEx(
                0,
                WindowClassName,
                WindowTitle,
                WsPopup,
                DefaultX,
                DefaultY,
                WidgetSettings.DefaultWidth,
                WidgetSettings.DefaultHeight,
                nint.Zero,
                nint.Zero,
                hInstance,
                nint.Zero);

            if (_windowHandle == nint.Zero)
            {
                throw new InvalidOperationException("Failed to create Box Tracker widget window.");
            }

            lock (s_windowsLock)
            {
                s_windows[_windowHandle] = this;
            }
        }

        public event EventHandler? Closed;

        public void Activate()
        {
            ThrowIfClosed();
            ShowWindow(_windowHandle, SwShow);
            UpdateWindow(_windowHandle);
        }

        public void Close()
        {
            if (_closed || _windowHandle == nint.Zero)
            {
                return;
            }

            DestroyWindow(_windowHandle);
        }

        public void UpdateText(string text)
        {
            _text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
            Invalidate();
        }

        public void ApplySettings(WidgetSettings settings)
        {
            ThrowIfClosed();
            settings.Normalize();

            _backgroundColor = WidgetColorSerializer.ParseOrDefault(settings.BackgroundColor, Colors.White);
            _textColor = WidgetColorSerializer.ParseOrDefault(settings.TextColor, Colors.Black);
            _transparentBackground = settings.TransparentBackground;
            _centerAlign = settings.CenterAlign;

            ApplyLayeredStyle();

            uint flags = SwpNoActivate | SwpFrameChanged;
            int x = 0;
            int y = 0;
            if (settings.X.HasValue && settings.Y.HasValue)
            {
                x = settings.X.Value;
                y = settings.Y.Value;
            }
            else
            {
                flags |= SwpNoMove;
            }

            nint insertAfter = settings.AlwaysOnTop ? new nint(-1) : new nint(-2);
            SetWindowPos(
                _windowHandle,
                insertAfter,
                x,
                y,
                settings.Width,
                settings.Height,
                flags);

            Invalidate();
        }

        public void CapturePlacement(WidgetSettings settings)
        {
            if (_closed || _windowHandle == nint.Zero || !GetWindowRect(_windowHandle, out NativeRect rect))
            {
                return;
            }

            settings.Width = rect.Right - rect.Left;
            settings.Height = rect.Bottom - rect.Top;
            settings.X = rect.Left;
            settings.Y = rect.Top;
            settings.Normalize();
        }

        private static void EnsureWindowClass()
        {
            if (s_classRegistered)
            {
                return;
            }

            nint hInstance = GetModuleHandle(null);
            WindowClassEx windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                Instance = hInstance,
                Cursor = LoadCursor(nint.Zero, new nint(IdcArrow)),
                ClassName = WindowClassName,
                WindowProc = s_wndProc
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException("Failed to register Box Tracker widget window class.");
            }

            s_classRegistered = true;
        }

        private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            BoxTrackerWidgetWindow? window;
            lock (s_windowsLock)
            {
                _ = s_windows.TryGetValue(hwnd, out window);
            }

            if (window is null)
            {
                return DefWindowProc(hwnd, message, wParam, lParam);
            }

            switch (message)
            {
                case WmPaint:
                    window.Paint();
                    return nint.Zero;

                case WmEraseBkgnd:
                    return new nint(1);

                case WmLButtonDown:
                    ReleaseCapture();
                    SendMessage(hwnd, WmNcLButtonDown, new nint(HtCaption), nint.Zero);
                    return nint.Zero;

                case WmClose:
                    DestroyWindow(hwnd);
                    return nint.Zero;

                case WmDestroy:
                    window.MarkClosed();
                    return nint.Zero;
            }

            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        private void Paint()
        {
            nint hdc = BeginPaint(_windowHandle, out PaintStruct paintStruct);
            try
            {
                if (!GetClientRect(_windowHandle, out NativeRect clientRect))
                {
                    return;
                }

                Color background = _transparentBackground
                    ? Color.FromArgb(255, 255, 0, 255)
                    : _backgroundColor;
                nint brush = CreateSolidBrush(ToColorRef(background));
                try
                {
                    FillRect(hdc, ref clientRect, brush);
                }
                finally
                {
                    DeleteObject(brush);
                }

                nint font = GetStockObject(StockDefaultGuiFont);
                nint oldFont = SelectObject(hdc, font);
                try
                {
                    SetBkMode(hdc, TransparentBkMode);
                    SetTextColor(hdc, ToColorRef(_textColor));
                    DrawText(hdc, _text, clientRect);
                }
                finally
                {
                    if (oldFont != nint.Zero)
                    {
                        SelectObject(hdc, oldFont);
                    }
                }
            }
            finally
            {
                EndPaint(_windowHandle, ref paintStruct);
            }
        }

        private void DrawText(nint hdc, string text, NativeRect clientRect)
        {
            NativeRect textRect = new()
            {
                Left = clientRect.Left + Padding,
                Top = clientRect.Top + Padding,
                Right = Math.Max(clientRect.Left + Padding, clientRect.Right - Padding),
                Bottom = Math.Max(clientRect.Top + Padding, clientRect.Bottom - Padding)
            };

            uint flags = DtWordBreak | DtNoPrefix | (_centerAlign ? DtCenter : DtLeft);
            if (_centerAlign)
            {
                NativeRect measureRect = textRect;
                _ = DrawTextW(hdc, text, -1, ref measureRect, flags | DtCalcRect);
                int availableHeight = textRect.Bottom - textRect.Top;
                int textHeight = measureRect.Bottom - measureRect.Top;
                if (textHeight < availableHeight)
                {
                    textRect.Top += (availableHeight - textHeight) / 2;
                }
            }

            _ = DrawTextW(hdc, text, -1, ref textRect, flags | DtTop);
        }

        private void ApplyLayeredStyle()
        {
            int extendedStyle = GetWindowLong(_windowHandle, GwlExStyle);
            if (_transparentBackground)
            {
                SetWindowLong(_windowHandle, GwlExStyle, extendedStyle | WsExLayered);
                SetLayeredWindowAttributes(_windowHandle, TransparentColorKey, 255, LwaColorKey);
                return;
            }

            if ((extendedStyle & WsExLayered) != 0)
            {
                SetWindowLong(_windowHandle, GwlExStyle, extendedStyle & ~WsExLayered);
            }
        }

        private void MarkClosed()
        {
            if (_closed)
            {
                return;
            }

            nint windowHandle = _windowHandle;
            lock (s_windowsLock)
            {
                s_windows.Remove(windowHandle);
            }

            _windowHandle = nint.Zero;
            _closed = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void Invalidate()
        {
            if (!_closed && _windowHandle != nint.Zero)
            {
                InvalidateRect(_windowHandle, nint.Zero, true);
            }
        }

        private void ThrowIfClosed()
        {
            if (_closed || _windowHandle == nint.Zero)
            {
                throw new ObjectDisposedException(nameof(BoxTrackerWidgetWindow));
            }
        }

        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClassEx
        {
            public uint Size;
            public uint Style;
            public WndProcDelegate WindowProc;
            public int ClassExtra;
            public int WindowExtra;
            public nint Instance;
            public nint Icon;
            public nint Cursor;
            public nint Background;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? MenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ClassName;
            public nint IconSmall;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintStruct
        {
            public nint DeviceContext;
            public bool Erase;
            public NativeRect Paint;
            public bool Restore;
            public bool IncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll")]
        private static extern nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(nint hWnd, out NativeRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint BeginPaint(nint hWnd, out PaintStruct lpPaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EndPaint(nint hWnd, ref PaintStruct lpPaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool FillRect(nint hdc, ref NativeRect lprc, nint hbr);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DrawTextW(nint hdc, string lpchText, int cchText, ref NativeRect lprc, uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint LoadCursor(nint hInstance, nint lpCursorName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(nint hwnd, int crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint CreateSolidBrush(int colorRef);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(nint hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint GetStockObject(int i);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint SelectObject(nint hdc, nint hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int SetBkMode(nint hdc, int mode);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int SetTextColor(nint hdc, int colorRef);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint GetModuleHandle(string? lpModuleName);
    }
}
