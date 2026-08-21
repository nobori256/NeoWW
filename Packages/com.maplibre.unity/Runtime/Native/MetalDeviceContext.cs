#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

// iOS/Metal counterpart to WglSharedContext.cs / EglSharedContext.cs. Obtains an id<MTLDevice> for MapLibre
// Native's FFI to render into (via mln_metal_context_descriptor), purely so the native side has a device to
// build its own Metal render pipeline from.
//
// Design notes:
//
// - Unity does not expose the app's MTLDevice to C# scripts, so this type calls into a small bundled
//   Objective-C bridge (Runtime/Plugins/iOS/MapLibreUnityMetalBridge.mm) that wraps MTLCreateSystemDefaultDevice().
//
// - MapLibre renders offscreen on this device and this plugin reads the rendered frame back to the CPU
//   (mln_texture_read_premultiplied_rgba8) rather than consuming a shared GPU texture/handle directly, so the
//   device does not need to be the same object Unity's own renderer uses. (In practice, on iOS there is only
//   one physical GPU, so it ends up being the same underlying device either way.)
namespace MapLibre.Unity.Native
{
    internal sealed class MetalDeviceContext : IDisposable
    {
        private IntPtr _device;
        private bool _disposed;

        public IntPtr Device => _device;

        public MetalDeviceContext()
        {
            _device = mlu_metal_create_system_default_device();
            if (_device == IntPtr.Zero)
            {
                throw new InvalidOperationException("mlu_metal_create_system_default_device returned null (MTLCreateSystemDefaultDevice failed).");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_device != IntPtr.Zero)
            {
                mlu_metal_release_device(_device);
                _device = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        ~MetalDeviceContext()
        {
            Dispose();
        }

        [DllImport("__Internal")]
        private static extern IntPtr mlu_metal_create_system_default_device();

        [DllImport("__Internal")]
        private static extern void mlu_metal_release_device(IntPtr device);
    }
}
#endif
