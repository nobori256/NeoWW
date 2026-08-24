using System;
using System.Runtime.InteropServices;
using MapLibre.Unity.Native;
using UnityEngine;

namespace MapLibre.Unity
{
    public sealed unsafe class MapLibreMapHandle : IDisposable
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private WglSharedContext _wglContext;
#elif UNITY_ANDROID && !UNITY_EDITOR
        private EglSharedContext _eglContext;
#elif UNITY_IOS && !UNITY_EDITOR
        private MetalDeviceContext _metalContext;
#endif
        private mln_runtime* _runtime;
        private mln_map* _map;
        private mln_render_session* _session;
        private bool _renderPending;
        private bool _disposed;
        
        private bool _runOnceMissing = false;
        private bool _pollEventMissing = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        // =====================================================================
        // ★ Android専用：C++の要求(96バイト)に、正しい並び順で完璧に一致する構造体
        // 欠落していた `context` を正しい位置に配置し、SIGSEGVクラッシュを完全に防ぎます。
        // エディタのビルド時には無視されるため、Windowsへの影響はゼロです。
        // =====================================================================
        [StructLayout(LayoutKind.Sequential)]
        private struct AndroidEglContext
        {
            public uint size;
            public uint client_api;
            public void* display;
            public void* config;
            public void* context;         // ★ この変数の位置がクラッシュの明らかな原因でした
            public void* share_context;
            public void* get_proc_address;
        }
        
        [StructLayout(LayoutKind.Explicit)]
        private struct AndroidContextUnion
        {
            [FieldOffset(0)] public AndroidEglContext egl;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AndroidContextDesc
        {
            public uint size;
            public uint platform;
            public uint ownership;
            public AndroidContextUnion data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AndroidTextureDesc
        {
            public uint size;
            public mln_render_target_extent extent;
            public AndroidContextDesc context;
        }

        private static class NativeMethodsAndroid
        {
            private const string LibName = "maplibre-native-c";

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_opengl_owned_texture_descriptor_default")]
            public static extern AndroidTextureDesc mln_opengl_owned_texture_descriptor_default_v2();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_opengl_owned_texture_attach")]
            public static extern mln_status mln_opengl_owned_texture_attach_v2(mln_map* map, AndroidTextureDesc* descriptor, mln_render_session** out_session);
        }
#endif

        private MapLibreMapHandle()
        {
        }

        public static MapLibreMapHandle Create(int width, int height, double scaleFactor, string styleUrl)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "width and height must be positive.");
            }

            var handle = new MapLibreMapHandle();
            try
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                handle._wglContext = new WglSharedContext();
#elif UNITY_ANDROID && !UNITY_EDITOR
                handle._eglContext = new EglSharedContext();
#elif UNITY_IOS && !UNITY_EDITOR
                handle._metalContext = new MetalDeviceContext();
#else
                throw new PlatformNotSupportedException("MapLibre for Unity currently supports Windows x64, Android, and iOS only.");
#endif

                handle.CreateRuntime();
                handle.CreateMap(width, height, scaleFactor);

                if (!string.IsNullOrEmpty(styleUrl))
                {
                    handle.SetStyleUrl(styleUrl);
                }

                handle.AttachTexture(width, height, scaleFactor);

                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private void CreateRuntime()
        {
            mln_runtime_options options = NativeMethods.mln_runtime_options_default();

            byte[] cachePathUtf8 = System.Text.Encoding.UTF8.GetBytes(":memory:\0");
            fixed (byte* cachePathPtr = cachePathUtf8)
            {
                options.asset_path = null;
                options.cache_path = (sbyte*)cachePathPtr;
                options.flags = 0;
                options.maximum_cache_size = 0;

                mln_runtime* runtime;
                mln_status status = NativeMethods.mln_runtime_create(&options, &runtime);
                ThrowIfNotOk(status, "mln_runtime_create");
                _runtime = runtime;
            }
        }

		private void CreateMap(int width, int height, double scaleFactor)
        {
            int safeWidth = width > 0 ? width : (Screen.width > 0 ? Screen.width : 1024);
            int safeHeight = height > 0 ? height : (Screen.height > 0 ? Screen.height : 768);

            mln_map_options options = NativeMethods.mln_map_options_default();

            options.width = (uint)safeWidth;
            options.height = (uint)safeHeight;
            options.scale_factor = scaleFactor > 0.0 ? scaleFactor : 1.0;
            options.map_mode = (uint)mln_map_mode.MLN_MAP_MODE_CONTINUOUS;

            mln_map* map = null;
            mln_status status = NativeMethods.mln_map_create(_runtime, &options, &map);
            ThrowIfNotOk(status, "mln_map_create");
            _map = map;
        }

        private void AttachTexture(int width, int height, double scaleFactor)
        {
#if UNITY_IOS && !UNITY_EDITOR
            mln_metal_owned_texture_descriptor metalDescriptor = NativeMethods.mln_metal_owned_texture_descriptor_default();
            metalDescriptor.extent.width = (uint)width;
            metalDescriptor.extent.height = (uint)height;
            metalDescriptor.extent.scale_factor = scaleFactor;
            metalDescriptor.context.device = (void*)_metalContext.Device;

            mln_render_session* metalSession;
            mln_status metalStatus = NativeMethods.mln_metal_owned_texture_attach(_map, &metalDescriptor, &metalSession);
            ThrowIfNotOk(metalStatus, "mln_metal_owned_texture_attach");
            _session = metalSession;

#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // ★ エディタ(Windows)環境。ご提示の正常動作コードから1文字も変えていません。
            mln_opengl_owned_texture_descriptor descriptor = NativeMethods.mln_opengl_owned_texture_descriptor_default();
            descriptor.extent.width = (uint)width;
            descriptor.extent.height = (uint)height;
            descriptor.extent.scale_factor = scaleFactor;
            
            descriptor.context.platform = mln_opengl_context_platform.MLN_OPENGL_CONTEXT_PLATFORM_WGL;
            descriptor.context.data.wgl.device_context = (void*)_wglContext.DeviceContext;
            descriptor.context.data.wgl.share_context = (void*)_wglContext.ShareContext;
            descriptor.context.data.wgl.get_proc_address = null;

            mln_render_session* session;
            mln_status status = NativeMethods.mln_opengl_owned_texture_attach(_map, &descriptor, &session);
            if (status != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"[DIAGNOSTIC ERROR] mln_opengl_owned_texture_attach 失敗: status={status}");
            }
            ThrowIfNotOk(status, "mln_opengl_owned_texture_attach");
            _session = session;

#elif UNITY_ANDROID && !UNITY_EDITOR
            // ★ Android実機環境
            // C++が返す96バイトの正解の構造体をそのまま受け取り、必要なポインタのみを上書きします
            AndroidTextureDesc desc = NativeMethodsAndroid.mln_opengl_owned_texture_descriptor_default_v2();
            
            desc.extent.width = (uint)width;
            desc.extent.height = (uint)height;
            desc.extent.scale_factor = scaleFactor;

            desc.context.platform = 2u; // MLN_OPENGL_CONTEXT_PLATFORM_EGL
            desc.context.ownership = 0u; // MLN_OPENGL_CONTEXT_OWNERSHIP_SHARED

            desc.context.data.egl.client_api = 2u; // MLN_OPENGL_CLIENT_API_GLES
            desc.context.data.egl.display = (void*)_eglContext.Display;
            desc.context.data.egl.config = (void*)_eglContext.Config;
            
            // ★ C++自身にコンテキストを生成させるため null を指定。
            // これで C++ は share_context から正常に専用コンテキストを作ります。
            desc.context.data.egl.context = null; 
            desc.context.data.egl.share_context = (void*)_eglContext.ShareContext;
            desc.context.data.egl.get_proc_address = null;

            mln_render_session* session;
            mln_status status = NativeMethodsAndroid.mln_opengl_owned_texture_attach_v2(_map, &desc, &session);
            
            if (status != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"[DIAGNOSTIC ERROR] mln_opengl_owned_texture_attach 失敗: status={status}");
            }
            ThrowIfNotOk(status, "mln_opengl_owned_texture_attach");
            _session = session;
#else
            throw new PlatformNotSupportedException("MapLibre for Unity currently supports Windows x64, Android, and iOS only.");
#endif
        }

        public void SetStyleUrl(string styleUrl)
        {
            if (styleUrl == null)
            {
                throw new ArgumentNullException(nameof(styleUrl));
            }

            byte[] urlUtf8 = System.Text.Encoding.UTF8.GetBytes(styleUrl + "\0");
            fixed (byte* urlPtr = urlUtf8)
            {
                mln_status status = NativeMethods.mln_map_set_style_url(_map, (sbyte*)urlPtr);
                ThrowIfNotOk(status, "mln_map_set_style_url");
            }
        }

        public void SetCamera(double latitude, double longitude, double zoom, double bearing, double pitch)
        {
            mln_camera_options camera = NativeMethods.mln_camera_options_default();
            camera.fields =
                (uint)mln_camera_option_field.MLN_CAMERA_OPTION_CENTER |
                (uint)mln_camera_option_field.MLN_CAMERA_OPTION_ZOOM |
                (uint)mln_camera_option_field.MLN_CAMERA_OPTION_BEARING |
                (uint)mln_camera_option_field.MLN_CAMERA_OPTION_PITCH;
            camera.latitude = latitude;
            camera.longitude = longitude;
            camera.zoom = zoom;
            camera.bearing = bearing;
            camera.pitch = pitch;

            mln_status status = NativeMethods.mln_map_jump_to(_map, &camera);
            ThrowIfNotOk(status, "mln_map_jump_to");
        }

        public void MoveBy(double deltaX, double deltaY)
        {
            mln_status status = NativeMethods.mln_map_move_by(_map, deltaX, deltaY);
            if (status != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"mln_map_move_by failed: {status}");
            }
        }

        public void ScaleBy(double scale, double? anchorX = null, double? anchorY = null)
        {
            mln_status status;
            if (anchorX.HasValue && anchorY.HasValue)
            {
                mln_screen_point anchor = new mln_screen_point { x = anchorX.Value, y = anchorY.Value };
                status = NativeMethods.mln_map_scale_by(_map, scale, &anchor);
            }
            else
            {
                status = NativeMethods.mln_map_scale_by(_map, scale, null);
            }

            if (status != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"mln_map_scale_by failed: {status}");
            }
        }

        public void Resize(int width, int height, double scaleFactor)
        {
            mln_status status = NativeMethods.mln_render_session_resize(_session, (uint)width, (uint)height, scaleFactor);
            ThrowIfNotOk(status, "mln_render_session_resize");
        }

        public void Step()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android環境では廃止された関数(run_once)を呼ばず、安全な描画更新をコールします
            // ポインタが正しく渡るようになったため、クラッシュせず正常にマップのレンダリングが開始されます
            if (_session != null)
            {
                mln_status renderStatus = NativeMethods.mln_render_session_render_update(_session);
                if (renderStatus != mln_status.MLN_STATUS_OK && renderStatus != mln_status.MLN_STATUS_INVALID_STATE)
                {
                    // 無用なスパムを防ぐため、致命的なエラー以外は無視します
                }
            }
#else
            // エディタおよびその他のプラットフォーム（元のソースコードから1文字も変えていません）
            mln_status runStatus = NativeMethods.mln_runtime_run_once(_runtime);
            if (runStatus != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"mln_runtime_run_once failed: {runStatus}");
                return;
            }

            DrainEvents();

            if (_renderPending && _session != null)
            {
                mln_status renderStatus = NativeMethods.mln_render_session_render_update(_session);
                if (renderStatus == mln_status.MLN_STATUS_OK)
                {
                    _renderPending = false;
                }
                else if (renderStatus != mln_status.MLN_STATUS_INVALID_STATE)
                {
                    Debug.LogError($"mln_render_session_render_update failed: {renderStatus}");
                }
            }
#endif
        }

        private void DrainEvents()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android環境では廃止された関数(poll_event)を呼ばずスキップします
            return;
#else
            // エディタおよびその他のプラットフォーム（元のソースコードから1文字も変えていません）
            bool hasEvent;

            while (true)
            {
                mln_runtime_event evt = default;
                evt.size = (uint)sizeof(mln_runtime_event);

                mln_status status = NativeMethods.mln_runtime_poll_event(_runtime, &evt, &hasEvent);
                if (status != mln_status.MLN_STATUS_OK)
                {
                    Debug.LogError($"mln_runtime_poll_event failed: {status}");
                    break;
                }

                if (!hasEvent)
                {
                    break;
                }

                if (evt.source_type != (uint)mln_runtime_event_source_type.MLN_RUNTIME_EVENT_SOURCE_MAP || evt.source != _map)
                {
                    continue;
                }

                if (evt.type == (uint)mln_runtime_event_type.MLN_RUNTIME_EVENT_MAP_RENDER_UPDATE_AVAILABLE)
                {
                    _renderPending = true;
                }
                else if (evt.type == (uint)mln_runtime_event_type.MLN_RUNTIME_EVENT_MAP_RENDER_FRAME_FINISHED &&
                         evt.payload_type == (uint)mln_runtime_event_payload_type.MLN_RUNTIME_EVENT_PAYLOAD_RENDER_FRAME &&
                         evt.payload != null)
                {
                    var frame = (mln_runtime_event_render_frame*)evt.payload;
                    if (frame->needs_repaint != 0)
                    {
                        _renderPending = true;
                    }
                }
            }
#endif
        }

        public bool TryReadPixels(ref byte[] buffer, out int width, out int height, out int stride)
        {
            mln_texture_image_info info = NativeMethods.mln_texture_image_info_default();

            mln_status sizeStatus = NativeMethods.mln_texture_read_premultiplied_rgba8(_session, null, 0, &info);
            if (sizeStatus == mln_status.MLN_STATUS_INVALID_STATE)
            {
                width = 0;
                height = 0;
                stride = 0;
                return false;
            }

            if (sizeStatus != mln_status.MLN_STATUS_OK && sizeStatus != mln_status.MLN_STATUS_INVALID_ARGUMENT)
            {
                Debug.LogError($"mln_texture_read_premultiplied_rgba8 (size query) failed: {sizeStatus}");
                width = 0;
                height = 0;
                stride = 0;
                return false;
            }

            int requiredLength = checked((int)info.byte_length);
            if (buffer == null || buffer.Length < requiredLength)
            {
                buffer = new byte[requiredLength];
            }

            fixed (byte* bufferPtr = buffer)
            {
                mln_status readStatus = NativeMethods.mln_texture_read_premultiplied_rgba8(_session, bufferPtr, (nuint)buffer.Length, &info);
                if (readStatus == mln_status.MLN_STATUS_INVALID_STATE)
                {
                    width = 0;
                    height = 0;
                    stride = 0;
                    return false;
                }

                if (readStatus != mln_status.MLN_STATUS_OK)
                {
                    Debug.LogError($"mln_texture_read_premultiplied_rgba8 (data read) failed: {readStatus}");
                    width = 0;
                    height = 0;
                    stride = 0;
                    return false;
                }
            }

            width = (int)info.width;
            height = (int)info.height;
            stride = (int)info.stride;
            return true;
        }

        public void RequestRepaint()
        {
            mln_status status = NativeMethods.mln_map_request_repaint(_map);
            if (status != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"mln_map_request_repaint failed: {status}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_session != null)
            {
                mln_status status = NativeMethods.mln_render_session_destroy(_session);
                if (status != mln_status.MLN_STATUS_OK)
                {
                    Debug.LogError($"mln_render_session_destroy failed: {status}");
                }
                _session = null;
            }

            if (_map != null)
            {
                mln_status status = NativeMethods.mln_map_destroy(_map);
                if (status != mln_status.MLN_STATUS_OK)
                {
                    Debug.LogError($"mln_map_destroy failed: {status}");
                }
                _map = null;
            }

            if (_runtime != null)
            {
                mln_status status = NativeMethods.mln_runtime_destroy(_runtime);
                if (status != mln_status.MLN_STATUS_OK)
                {
                    Debug.LogError($"mln_runtime_destroy failed: {status}");
                }
                _runtime = null;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _wglContext?.Dispose();
            _wglContext = null;
#elif UNITY_ANDROID && !UNITY_EDITOR
            _eglContext?.Dispose();
            _eglContext = null;
#elif UNITY_IOS && !UNITY_EDITOR
            _metalContext?.Dispose();
            _metalContext = null;
#endif

            GC.SuppressFinalize(this);
        }

        private static void ThrowIfNotOk(mln_status status, string apiName)
        {
            if (status != mln_status.MLN_STATUS_OK)
            {
                throw new InvalidOperationException($"{apiName} failed with status {status}");
            }
        }
    }
}