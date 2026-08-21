#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

// Android/EGL counterpart to WglSharedContext.cs. Obtains an EGLDisplay/EGLConfig and creates a small,
// independent EGLContext purely so that MapLibre Native's FFI has an EGLDisplay/EGLConfig/EGLContext to
// build its own rendering context from (via mln_egl_context_descriptor). This context is never made
// current for actual rendering by this plugin.
//
// Design notes:
//
// - Unlike WglSharedContext (which hands MapLibre a real *share* context so both sides join the same
//   WGL share-group), the EGLContext created here is deliberately NOT shared with anything
//   (share_context = EGL_NO_CONTEXT). This plugin reads MapLibre's rendered frame back to the CPU
//   (mln_texture_read_premultiplied_rgba8) rather than consuming a shared GPU texture/handle directly,
//   so there is no need for MapLibre's context to be part of a share-group with Unity's own context -
//   only a valid EGLDisplay/EGLConfig combination that the native side can create its own context from.
//   There is also no stable, public Unity API to obtain Unity's own EGLContext to share with anyway.
//
// - eglTerminate() is intentionally never called. eglGetDisplay(EGL_DEFAULT_DISPLAY) returns a
//   process-wide singleton EGLDisplay that Unity's own renderer is already using; eglTerminate would
//   tear down that shared display (and Unity's EGL state along with it). Only eglDestroyContext() is
//   called in Dispose(), which destroys just the context this type created.
//
// - The chosen EGLConfig must include EGL_SURFACE_TYPE = EGL_PBUFFER_BIT: this is a requirement of
//   MapLibre Native's FFI/EGL context descriptor contract on the native side, independent of the fact
//   that this plugin never actually creates a pbuffer surface itself.
namespace MapLibre.Unity.Native
{
    internal sealed unsafe class EglSharedContext : IDisposable
    {
        private const uint EGL_OPENGL_ES_API = 0x30A0;

        private const int EGL_ALPHA_SIZE = 0x3021;
        private const int EGL_BLUE_SIZE = 0x3022;
        private const int EGL_GREEN_SIZE = 0x3023;
        private const int EGL_RED_SIZE = 0x3024;
        private const int EGL_DEPTH_SIZE = 0x3025;
        private const int EGL_STENCIL_SIZE = 0x3026;
        private const int EGL_SURFACE_TYPE = 0x3033;
        private const int EGL_NONE = 0x3038;
        private const int EGL_RENDERABLE_TYPE = 0x3040;
        private const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;

        private const int EGL_PBUFFER_BIT = 0x0001;
        private const int EGL_OPENGL_ES2_BIT = 0x0004;
        private const int EGL_OPENGL_ES3_BIT = 0x0040;

        private static readonly IntPtr EGL_DEFAULT_DISPLAY = IntPtr.Zero;
        private static readonly IntPtr EGL_NO_CONTEXT = IntPtr.Zero;

        private IntPtr _display;
        private IntPtr _config;
        private IntPtr _shareContext;
        private bool _disposed;

        public IntPtr Display => _display;

        public IntPtr Config => _config;

        public IntPtr ShareContext => _shareContext;

        public EglSharedContext()
        {
            _display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
            if (_display == IntPtr.Zero)
            {
                throw new InvalidOperationException("eglGetDisplay returned EGL_NO_DISPLAY.");
            }

            // Unity has already initialized EGL on this display for its own rendering. eglInitialize is
            // safe to call again on an already-initialized display (it is reference-counted / idempotent
            // per the EGL spec), so this does not disturb Unity's own EGL state.
            if (eglInitialize(_display, out _, out _) == 0)
            {
                throw new InvalidOperationException("eglInitialize failed with error 0x" + eglGetError().ToString("X") + ".");
            }

            if (eglBindAPI(EGL_OPENGL_ES_API) == 0)
            {
                throw new InvalidOperationException("eglBindAPI(EGL_OPENGL_ES_API) failed with error 0x" + eglGetError().ToString("X") + ".");
            }

            ChooseConfig();
            CreateContext();
        }

        private void ChooseConfig()
        {
            int* attribs = stackalloc int[]
            {
                EGL_RED_SIZE, 8,
                EGL_GREEN_SIZE, 8,
                EGL_BLUE_SIZE, 8,
                EGL_ALPHA_SIZE, 8,
                EGL_DEPTH_SIZE, 24,
                EGL_STENCIL_SIZE, 8,
                EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
                EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT | EGL_OPENGL_ES2_BIT,
                EGL_NONE,
            };

            IntPtr* configs = stackalloc IntPtr[1];
            int ok = eglChooseConfig(_display, attribs, configs, 1, out int numConfigs);
            if (ok == 0 || numConfigs == 0)
            {
                throw new InvalidOperationException(
                    "eglChooseConfig failed (error 0x" + eglGetError().ToString("X") + ") or returned no matching EGLConfig.");
            }

            _config = configs[0];
        }

        private void CreateContext()
        {
            IntPtr context = TryCreateContext(clientVersion: 3);
            if (context == IntPtr.Zero)
            {
                // Retry once with an ES2 context, in case the driver's EGL implementation rejects
                // EGL_CONTEXT_CLIENT_VERSION=3 on the chosen config despite advertising ES3 support.
                context = TryCreateContext(clientVersion: 2);
            }

            if (context == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "eglCreateContext failed (error 0x" + eglGetError().ToString("X") + ") for both ES3 and ES2 client versions.");
            }

            _shareContext = context;
        }

        private IntPtr TryCreateContext(int clientVersion)
        {
            int* attribs = stackalloc int[]
            {
                EGL_CONTEXT_CLIENT_VERSION, clientVersion,
                EGL_NONE,
            };

            // share_context = EGL_NO_CONTEXT: this context intentionally does not join any share-group.
            // See the type-level comment for why.
            return eglCreateContext(_display, _config, EGL_NO_CONTEXT, attribs);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_shareContext != IntPtr.Zero)
            {
                eglDestroyContext(_display, _shareContext);
                _shareContext = IntPtr.Zero;
            }

            // Deliberately never call eglTerminate(_display) - see the type-level comment.
            _display = IntPtr.Zero;
            _config = IntPtr.Zero;

            GC.SuppressFinalize(this);
        }

        ~EglSharedContext()
        {
            Dispose();
        }

        // ---- EGL interop --------------------------------------------------------------------------------------

        [DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr eglGetDisplay(IntPtr display_id);

        [DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
        private static extern int eglInitialize(IntPtr display, out int major, out int minor);

        [DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
        private static extern int eglBindAPI(uint api);

        [DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
        private static extern int eglChooseConfig(IntPtr display, int* attrib_list, IntPtr* configs, int config_size, out int num_config);

        [DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr eglCreateContext(IntPtr display, IntPtr config, IntPtr share_context, int* attrib_list);

        [DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
        private static extern int eglDestroyContext(IntPtr display, IntPtr context);

        [DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
        private static extern int eglGetError();
    }
}
#endif
