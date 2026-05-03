using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BO2.Services;
using Microsoft.UI;
using Windows.UI;

namespace BO2.Widgets
{
    internal sealed class BoxTrackerWidgetWindow : IBoxTrackerWidgetNativeWindow
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
        private static readonly ConcurrentDictionary<nint, BoxTrackerWidgetWindow> s_windows = new();
        private static readonly object s_windowClassLock = new();
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

            // Native class/window creation needs the current module instance handle.
            // codeql[cs/call-to-unmanaged-code]
            nint hInstance = GetModuleHandle(null);
            // The Box Tracker widget is an owner-drawn popup HWND with a custom WndProc.
            // codeql[cs/call-to-unmanaged-code]
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

            s_windows[_windowHandle] = this;
        }

        public event EventHandler? Closed;

        public void Activate()
        {
            ThrowIfClosed();
            // The owner-drawn native popup is not a XAML Window; showing it requires HWND interop.
            // codeql[cs/call-to-unmanaged-code]
            ShowWindow(_windowHandle, SwShow);
            // Force the initial WM_PAINT for the native popup after it is shown.
            // codeql[cs/call-to-unmanaged-code]
            UpdateWindow(_windowHandle);
        }

        public void Close()
        {
            if (_closed || _windowHandle == nint.Zero)
            {
                return;
            }

            // Close owns the raw HWND lifecycle and destroys the window created by CreateWindowEx.
            // codeql[cs/call-to-unmanaged-code]
            _ = DestroyWindow(_windowHandle);
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
            // Set position, size, topmost state, and no-activate behavior atomically for the raw HWND.
            // codeql[cs/call-to-unmanaged-code]
            _ = SetWindowPos(
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
            // Persist the raw popup HWND geometry for restore; this window is not a XAML Window.
            // codeql[cs/call-to-unmanaged-code]
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
            lock (s_windowClassLock)
            {
                if (s_classRegistered)
                {
                    return;
                }

                // RegisterClassEx needs the current module instance for this native class.
                // codeql[cs/call-to-unmanaged-code]
                nint hInstance = GetModuleHandle(null);
                WindowClassEx windowClass = new()
                {
                    Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                    Instance = hInstance,
                    // Assign the standard arrow cursor to the registered native window class.
                    // codeql[cs/call-to-unmanaged-code]
                    Cursor = LoadCursor(nint.Zero, new nint(IdcArrow)),
                    ClassName = WindowClassName,
                    WindowProc = s_wndProc
                };

                // Register the custom owner-drawn popup class and WndProc before creating windows.
                // codeql[cs/call-to-unmanaged-code]
                if (RegisterClassEx(ref windowClass) == 0)
                {
                    throw new InvalidOperationException("Failed to register Box Tracker widget window class.");
                }

                s_classRegistered = true;
            }
        }

        private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            if (!s_windows.TryGetValue(hwnd, out BoxTrackerWidgetWindow? window))
            {
                // Unknown HWND messages must be delegated back to the OS default procedure.
                // codeql[cs/call-to-unmanaged-code]
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
                    // Caption-style dragging requires releasing capture before forwarding the native message.
                    // codeql[cs/call-to-unmanaged-code]
                    _ = ReleaseCapture();
                    // Forward the drag request to the app-owned borderless widget HWND.
                    // codeql[cs/call-to-unmanaged-code]
                    _ = SendMessage(hwnd, WmNcLButtonDown, new nint(HtCaption), nint.Zero);
                    return nint.Zero;

                case WmClose:
                    // WM_CLOSE owns the same raw HWND destruction path as Close().
                    // codeql[cs/call-to-unmanaged-code]
                    _ = DestroyWindow(hwnd);
                    return nint.Zero;

                case WmDestroy:
                    window.MarkClosed();
                    return nint.Zero;
            }

            // Unhandled widget messages must retain default Windows behavior.
            // codeql[cs/call-to-unmanaged-code]
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        private void Paint()
        {
            // Owner-drawn WM_PAINT must acquire the paint DC from the app-owned HWND.
            // codeql[cs/call-to-unmanaged-code]
            nint hdc = BeginPaint(_windowHandle, out PaintStruct paintStruct);
            try
            {
                // Painting is clipped to the current client area of the app-owned HWND.
                // codeql[cs/call-to-unmanaged-code]
                if (!GetClientRect(_windowHandle, out NativeRect clientRect))
                {
                    return;
                }

                Color background = _transparentBackground
                    ? Color.FromArgb(255, 255, 0, 255)
                    : _backgroundColor;
                // The WM_PAINT path uses a transient GDI brush for the configured background.
                // codeql[cs/call-to-unmanaged-code]
                nint brush = CreateSolidBrush(ToColorRef(background));
                try
                {
                    // Fill the app-owned widget client rectangle using the current paint DC.
                    // codeql[cs/call-to-unmanaged-code]
                    _ = FillRect(hdc, ref clientRect, brush);
                }
                finally
                {
                    // Release the transient GDI brush allocated for this paint pass.
                    // codeql[cs/call-to-unmanaged-code]
                    _ = DeleteObject(brush);
                }

                // Use the stock GUI font for native text drawn into the widget paint DC.
                // codeql[cs/call-to-unmanaged-code]
                nint font = GetStockObject(StockDefaultGuiFont);
                // Select the stock font and keep the previous object so it can be restored.
                // codeql[cs/call-to-unmanaged-code]
                nint oldFont = SelectObject(hdc, font);
                try
                {
                    // Owner-drawn HWND text requires configuring the native paint DC.
                    // codeql[cs/call-to-unmanaged-code]
                    _ = SetBkMode(hdc, TransparentBkMode);
                    // Owner-drawn HWND text requires configuring the native paint DC.
                    // codeql[cs/call-to-unmanaged-code]
                    _ = SetTextColor(hdc, ToColorRef(_textColor));
                    DrawText(hdc, _text, clientRect);
                }
                finally
                {
                    if (oldFont != nint.Zero)
                    {
                        // Restore the paint DC font selected before owner drawing.
                        // codeql[cs/call-to-unmanaged-code]
                        _ = SelectObject(hdc, oldFont);
                    }
                }
            }
            finally
            {
                // Release the WM_PAINT DC paired with BeginPaint.
                // codeql[cs/call-to-unmanaged-code]
                _ = EndPaint(_windowHandle, ref paintStruct);
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
                // Measure wrapped native text before vertical centering in the widget DC.
                // codeql[cs/call-to-unmanaged-code]
                _ = DrawTextW(hdc, text, -1, ref measureRect, flags | DtCalcRect);
                int availableHeight = textRect.Bottom - textRect.Top;
                int textHeight = measureRect.Bottom - measureRect.Top;
                if (textHeight < availableHeight)
                {
                    textRect.Top += (availableHeight - textHeight) / 2;
                }
            }

            // Render the configured widget text directly into the WM_PAINT DC.
            // codeql[cs/call-to-unmanaged-code]
            _ = DrawTextW(hdc, text, -1, ref textRect, flags | DtTop);
        }

        private void ApplyLayeredStyle()
        {
            // Read the current extended style before toggling layered transparency.
            // codeql[cs/call-to-unmanaged-code]
            int extendedStyle = GetWindowLong(_windowHandle, GwlExStyle);
            if (_transparentBackground)
            {
                // Layered HWND transparency is a native window style with no managed equivalent here.
                // codeql[cs/call-to-unmanaged-code]
                _ = SetWindowLong(_windowHandle, GwlExStyle, extendedStyle | WsExLayered);
                // Color-key transparency is applied only to the app-owned layered HWND.
                // codeql[cs/call-to-unmanaged-code]
                _ = SetLayeredWindowAttributes(_windowHandle, TransparentColorKey, 255, LwaColorKey);
                return;
            }

            if ((extendedStyle & WsExLayered) != 0)
            {
                // Layered HWND transparency is a native window style with no managed equivalent here.
                // codeql[cs/call-to-unmanaged-code]
                _ = SetWindowLong(_windowHandle, GwlExStyle, extendedStyle & ~WsExLayered);
            }
        }

        private void MarkClosed()
        {
            if (_closed)
            {
                return;
            }

            nint windowHandle = _windowHandle;
            try
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                s_windows.TryRemove(windowHandle, out _);
                _windowHandle = nint.Zero;
                _closed = true;
            }
        }

        private void Invalidate()
        {
            if (!_closed && _windowHandle != nint.Zero)
            {
                // Repaint requests target only the app-owned Box Tracker HWND.
                // codeql[cs/call-to-unmanaged-code]
                _ = InvalidateRect(_windowHandle, nint.Zero, true);
            }
        }

        private void ThrowIfClosed()
        {
            ObjectDisposedException.ThrowIf(_closed || _windowHandle == nint.Zero, this);
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

        // Required to register the private Box Tracker HWND class and local WndProc.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        // Required to create the private popup HWND used by the Box Tracker widget.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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

        // Required to close the owned HWND from Close and WM_CLOSE.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DestroyWindow(nint hWnd);

        // Required to delegate unhandled messages for the private HWND to Windows.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

        // Required to show the owned Box Tracker HWND.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        // Required to force the first paint of the owned Box Tracker HWND.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool UpdateWindow(nint hWnd);

        // Required to persist the owned HWND placement back into widget settings.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

        // Required to constrain owner drawing to the owned HWND client area.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool GetClientRect(nint hWnd, out NativeRect lpRect);

        // Required to enter WM_PAINT and acquire the paint DC for the owner-drawn Box Tracker HWND.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint BeginPaint(nint hWnd, out PaintStruct lpPaint);

        // Required to release the paint DC paired with BeginPaint after owner-drawn HWND rendering.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool EndPaint(nint hWnd, ref PaintStruct lpPaint);

        // Required to fill the owner-drawn Box Tracker client rectangle using the paint DC.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool FillRect(nint hdc, ref NativeRect lprc, nint hbr);

        // Required to repaint the owned HWND after Box Tracker text or setting changes.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

        // Required to render and measure Unicode text directly into the owner-drawn paint DC.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DrawTextW(nint hdc, string lpchText, int cchText, ref NativeRect lprc, uint format);

        // Required to assign the default OS arrow cursor to the private HWND class.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint LoadCursor(nint hInstance, nint lpCursorName);

        // Required to read extended styles before toggling the layered HWND style.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        // Required to toggle WS_EX_LAYERED for color-keyed widget transparency.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        // Required to apply size, placement, and topmost state to the owned HWND.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        // Required for color-keyed transparency after WS_EX_LAYERED is applied.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetLayeredWindowAttributes(nint hwnd, int crKey, byte bAlpha, uint dwFlags);

        // Required to hand mouse capture to Windows before caption-style dragging.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool ReleaseCapture();

        // Required to request OS caption drag behavior for the borderless widget HWND.
        // codeql[cs/unmanaged-code]
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

        // Required to allocate the solid background brush used for owner-drawn Box Tracker painting.
        // codeql[cs/unmanaged-code]
        [DllImport("gdi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint CreateSolidBrush(int colorRef);

        // Required to release the transient solid brush created for the paint pass.
        // codeql[cs/unmanaged-code]
        [DllImport("gdi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DeleteObject(nint hObject);

        // Required to obtain the stock GUI font for native text drawn into the paint DC.
        // codeql[cs/unmanaged-code]
        [DllImport("gdi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint GetStockObject(int i);

        // Required to select and restore the font in the owner-drawn paint DC.
        // codeql[cs/unmanaged-code]
        [DllImport("gdi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint SelectObject(nint hdc, nint hObject);

        // Required to make native text drawing preserve the already-painted widget background.
        // codeql[cs/unmanaged-code]
        [DllImport("gdi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SetBkMode(nint hdc, int mode);

        // Required to apply the configured Box Tracker text color to native text drawing.
        // codeql[cs/unmanaged-code]
        [DllImport("gdi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SetTextColor(nint hdc, int colorRef);

        // Required to bind class registration and window creation to the current module.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint GetModuleHandle(string? lpModuleName);
    }
}
