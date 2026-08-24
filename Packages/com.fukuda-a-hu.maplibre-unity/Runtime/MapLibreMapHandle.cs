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
        private bool _androidReadTested; // Androidでのテストを1回だけ実行するためのフラグ

#if UNITY_ANDROID && !UNITY_EDITOR
        private static class NativeMethodsAndroid
        {
            private const string LibName = "maplibre-native-c";

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_opengl_owned_texture_descriptor_default")]
            public static extern mln_opengl_owned_texture_descriptor_v2 mln_opengl_owned_texture_descriptor_default_v2();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_opengl_owned_texture_attach")]
            public static extern mln_status mln_opengl_owned_texture_attach_v2(mln_map* map, mln_opengl_owned_texture_descriptor_v2* descriptor, mln_render_session** out_session);
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
            // ★ エディタ(Windows)環境：元の正常動作コードを完全維持
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
            ThrowIfNotOk(status, "mln_opengl_owned_texture_attach");
            _session = session;

#elif UNITY_ANDROID && !UNITY_EDITOR
            // ★ Android実機環境：アタッチ成功したセッションを安全に保持し、後ほど安全にプローブします
            mln_opengl_owned_texture_descriptor_v2 descriptorV2 = NativeMethodsAndroid.mln_opengl_owned_texture_descriptor_default_v2();
            
            descriptorV2.extent.width = (uint)width;
            descriptorV2.extent.height = (uint)height;
            descriptorV2.extent.scale_factor = scaleFactor;

            descriptorV2.context.platform = mln_opengl_context_platform.MLN_OPENGL_CONTEXT_PLATFORM_EGL;
            descriptorV2.context.ownership = mln_opengl_context_ownership_v2.MLN_OPENGL_CONTEXT_OWNERSHIP_SHARED;

            descriptorV2.context.data.egl.client_api = mln_opengl_client_api_v2.MLN_OPENGL_CLIENT_API_GLES;
            descriptorV2.context.data.egl.display = (void*)_eglContext.Display;
            descriptorV2.context.data.egl.config = (void*)_eglContext.Config;
            descriptorV2.context.data.egl.share_context = (void*)_eglContext.ShareContext;
            descriptorV2.context.data.egl.get_proc_address = null;

            mln_render_session* session = null;
            mln_status attachStatus = NativeMethodsAndroid.mln_opengl_owned_texture_attach_v2(_map, &descriptorV2, &session);
            Debug.Log($"[ANDROID SAFE TEST] attach status={attachStatus}, session=0x{(IntPtr)session:X}");
            
            _session = session; // セッションを保持しますが、render_updateは呼び出しません
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
            if (_session == null) return;
            mln_status status = NativeMethods.mln_render_session_resize(_session, (uint)width, (uint)height, scaleFactor);
            ThrowIfNotOk(status, "mln_render_session_resize");
        }

        public void Step()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android実機での安全テスト：クラッシュする render_update は呼ばず、
            // 初回のみ安全に TryReadPixels を試してセッションの生存状態をログ出力します
            if (_session != null && !_androidReadTested)
            {
                _androidReadTested = true;
                try
                {
                    byte[] dummyBuf = null;
                    int w, h, s;
                    bool result = TryReadPixels(ref dummyBuf, out w, out h, out s);
                    Debug.Log(result ? 
                        $"[ANDROID SAFE TEST] ✓ TryReadPixels 成功！ 画面サイズ: {w}x{h}, stride: {s}, byteLength: {dummyBuf?.Length}" : 
                        "[ANDROID SAFE TEST] ✗ TryReadPixels はまだデータを持っていません（INVALID_STATE）。");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ANDROID SAFE TEST] 例外発生: {ex.Message}");
                }
            }
            return;
#else
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
            return;
#else
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
            if (_session == null)
            {
                width = 0;
                height = 0;
                stride = 0;
                return false;
            }

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