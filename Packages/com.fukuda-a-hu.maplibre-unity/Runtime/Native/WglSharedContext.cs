#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

// Creates a hidden 1x1 window with a valid WGL pixel format and a "seed" HGLRC, purely so that MapLibre Native's
// FFI can create its own OpenGL context that shares the same share-group (via mln_wgl_context_descriptor). This
// context is never made current for actual rendering by this plugin - it exists only to give the native side a
// device context (HDC) and a share context (HGLRC) to join.
//
// Gated to Windows builds only: this type P/Invokes into user32.dll / gdi32.dll / opengl32.dll, which do not
// exist on other platforms (e.g. Android). See EglSharedContext.cs for the Android/EGL equivalent.
namespace MapLibre.Unity.Native
{
    internal sealed unsafe class WglSharedContext : IDisposable
    {
        private const uint CS_OWNDC = 0x0020;
        private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
        private const uint PFD_SUPPORT_OPENGL = 0x00000020;
        private const uint PFD_DOUBLEBUFFER = 0x00000001;
        private const byte PFD_TYPE_RGBA = 0;
        private const byte PFD_MAIN_PLANE = 0;

        private readonly string _className;
        private readonly WndProcDelegate _wndProcDelegate;
        private IntPtr _hInstance;
        private IntPtr _hWnd;
        private IntPtr _hdc;
        private IntPtr _hglrc;
        private bool _disposed;

        public IntPtr DeviceContext => _hdc;

        public IntPtr ShareContext => _hglrc;

        public WglSharedContext()
        {
            _className = "MapLibreUnityWglHiddenWindow_" + Guid.NewGuid().ToString("N");
            _wndProcDelegate = DefWindowProcSafe;
            _hInstance = GetModuleHandle(null);

            RegisterWindowClass();
            CreateHiddenWindow();
            SetUpPixelFormat();
            CreateGlContext();
        }

        private void RegisterWindowClass()
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = CS_OWNDC,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = _hInstance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = _className,
                hIconSm = IntPtr.Zero,
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed with error " + Marshal.GetLastWin32Error());
            }
        }

        private void CreateHiddenWindow()
        {
            _hWnd = CreateWindowEx(
                0,
                _className,
                "MapLibreUnityOffscreen",
                WS_OVERLAPPEDWINDOW,
                0, 0, 1, 1,
                IntPtr.Zero,
                IntPtr.Zero,
                _hInstance,
                IntPtr.Zero);

            if (_hWnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateWindowEx failed with error " + Marshal.GetLastWin32Error());
            }

            // Intentionally never shown (no ShowWindow call).
            _hdc = GetDC(_hWnd);
            if (_hdc == IntPtr.Zero)
            {
                throw new InvalidOperationException("GetDC failed with error " + Marshal.GetLastWin32Error());
            }
        }

        private void SetUpPixelFormat()
        {
            var pfd = new PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
                nVersion = 1,
                dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
                iPixelType = PFD_TYPE_RGBA,
                cColorBits = 32,
                cRedBits = 0,
                cRedShift = 0,
                cGreenBits = 0,
                cGreenShift = 0,
                cBlueBits = 0,
                cBlueShift = 0,
                cAlphaBits = 0,
                cAlphaShift = 0,
                cAccumBits = 0,
                cAccumRedBits = 0,
                cAccumGreenBits = 0,
                cAccumBlueBits = 0,
                cAccumAlphaBits = 0,
                cDepthBits = 24,
                cStencilBits = 8,
                cAuxBuffers = 0,
                iLayerType = PFD_MAIN_PLANE,
                bReserved = 0,
                dwLayerMask = 0,
                dwVisibleMask = 0,
                dwDamageMask = 0,
            };

            int pixelFormat = ChoosePixelFormat(_hdc, ref pfd);
            if (pixelFormat == 0)
            {
                throw new InvalidOperationException("ChoosePixelFormat failed with error " + Marshal.GetLastWin32Error());
            }

            if (!SetPixelFormat(_hdc, pixelFormat, ref pfd))
            {
                throw new InvalidOperationException("SetPixelFormat failed with error " + Marshal.GetLastWin32Error());
            }
        }

        private void CreateGlContext()
        {
            _hglrc = wglCreateContext(_hdc);
            if (_hglrc == IntPtr.Zero)
            {
                throw new InvalidOperationException("wglCreateContext failed with error " + Marshal.GetLastWin32Error());
            }

            // Briefly make current to validate the context is usable, then immediately release it. This context
            // is never left current - MapLibre's own context (created by the native side, sharing this context's
            // share-group) does the actual rendering.
            if (!wglMakeCurrent(_hdc, _hglrc))
            {
                throw new InvalidOperationException("wglMakeCurrent failed with error " + Marshal.GetLastWin32Error());
            }

            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        }

        private static IntPtr DefWindowProcSafe(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_hglrc != IntPtr.Zero)
            {
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                wglDeleteContext(_hglrc);
                _hglrc = IntPtr.Zero;
            }

            if (_hdc != IntPtr.Zero && _hWnd != IntPtr.Zero)
            {
                ReleaseDC(_hWnd, _hdc);
                _hdc = IntPtr.Zero;
            }

            if (_hWnd != IntPtr.Zero)
            {
                DestroyWindow(_hWnd);
                _hWnd = IntPtr.Zero;
            }

            if (_hInstance != IntPtr.Zero)
            {
                UnregisterClass(_className, _hInstance);
                _hInstance = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        ~WglSharedContext()
        {
            Dispose();
        }

        // ---- Win32 interop -----------------------------------------------------------------------------------------

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize;
            public ushort nVersion;
            public uint dwFlags;
            public byte iPixelType;
            public byte cColorBits;
            public byte cRedBits;
            public byte cRedShift;
            public byte cGreenBits;
            public byte cGreenShift;
            public byte cBlueBits;
            public byte cBlueShift;
            public byte cAlphaBits;
            public byte cAlphaShift;
            public byte cAccumBits;
            public byte cAccumRedBits;
            public byte cAccumGreenBits;
            public byte cAccumBlueBits;
            public byte cAccumAlphaBits;
            public byte cDepthBits;
            public byte cStencilBits;
            public byte cAuxBuffers;
            public byte iLayerType;
            public byte bReserved;
            public uint dwLayerMask;
            public uint dwVisibleMask;
            public uint dwDamageMask;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int ChoosePixelFormat(IntPtr hdc, [In] ref PIXELFORMATDESCRIPTOR ppfd);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool SetPixelFormat(IntPtr hdc, int format, [In] ref PIXELFORMATDESCRIPTOR ppfd);

        [DllImport("opengl32.dll", SetLastError = true)]
        private static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll", SetLastError = true)]
        private static extern bool wglDeleteContext(IntPtr hglrc);

        [DllImport("opengl32.dll", SetLastError = true)]
        private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    }
}
#endif
