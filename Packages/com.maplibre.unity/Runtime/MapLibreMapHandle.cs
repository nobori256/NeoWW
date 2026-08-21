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
        private bool _renderPending = true;
        private bool _disposed;

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
                handle.RequestRepaint();

                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static System.Runtime.InteropServices.GCHandle? _persistentAssetPathHandle;
        private static System.Runtime.InteropServices.GCHandle? _persistentCachePathHandle;

        private void CreateRuntime()
        {
            mln_runtime_options options = NativeMethods.mln_runtime_options_default();
            options.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_runtime_options>();

            string assetPathStr = Application.persistentDataPath + "\0";
            byte[] assetPathUtf8 = System.Text.Encoding.UTF8.GetBytes(assetPathStr);
            
            if (_persistentAssetPathHandle.HasValue) _persistentAssetPathHandle.Value.Free();
            _persistentAssetPathHandle = System.Runtime.InteropServices.GCHandle.Alloc(assetPathUtf8, System.Runtime.InteropServices.GCHandleType.Pinned);

            byte[] cachePathUtf8 = System.Text.Encoding.UTF8.GetBytes(":memory:\0");
            if (_persistentCachePathHandle.HasValue) _persistentCachePathHandle.Value.Free();
            _persistentCachePathHandle = System.Runtime.InteropServices.GCHandle.Alloc(cachePathUtf8, System.Runtime.InteropServices.GCHandleType.Pinned);

            fixed (byte* assetPathPtr = assetPathUtf8)
            fixed (byte* cachePathPtr = cachePathUtf8)
            {
                options.asset_path = (sbyte*)assetPathPtr;
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

            options.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_map_options>();
            options.width = (uint)safeWidth;
            options.height = (uint)safeHeight;
            options.scale_factor = scaleFactor > 0.0 ? scaleFactor : 1.0;
            options.map_mode = (uint)mln_map_mode.MLN_MAP_MODE_CONTINUOUS;

#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
            // Android等で必要なフラグのみ明示的に立てる（昨晩の成功コード）
            options.event_mask = (1ul << (int)mln_runtime_event_type.MLN_RUNTIME_EVENT_MAP_RENDER_UPDATE_AVAILABLE) |
                                 (1ul << (int)mln_runtime_event_type.MLN_RUNTIME_EVENT_MAP_RENDER_FRAME_FINISHED);
#endif

            mln_map* map;
            mln_status status = NativeMethods.mln_map_create(_runtime, &options, &map);
            ThrowIfNotOk(status, "mln_map_create");
            _map = map;
        }

        private void AttachTexture(int width, int height, double scaleFactor)
        {
#if UNITY_IOS && !UNITY_EDITOR
            mln_metal_owned_texture_descriptor metalDescriptor = NativeMethods.mln_metal_owned_texture_descriptor_default();
            metalDescriptor.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_metal_owned_texture_descriptor>();
            metalDescriptor.extent.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_render_target_extent>();
            metalDescriptor.extent.width = (uint)width;
            metalDescriptor.extent.height = (uint)height;
            metalDescriptor.extent.scale_factor = scaleFactor;
            metalDescriptor.context.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_metal_context_descriptor>();
            metalDescriptor.context.device = (void*)_metalContext.Device;

            mln_render_session* metalSession;
            mln_status metalStatus = NativeMethods.mln_metal_owned_texture_attach(_map, &metalDescriptor, &metalSession);
            ThrowIfNotOk(metalStatus, "mln_metal_owned_texture_attach");
            _session = metalSession;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            mln_opengl_owned_texture_descriptor_legacy descriptor = default;
            descriptor.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_opengl_owned_texture_descriptor_legacy>();

            descriptor.extent.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_render_target_extent>();
            descriptor.extent.width = (uint)width;
            descriptor.extent.height = (uint)height;
            descriptor.extent.scale_factor = scaleFactor;

            descriptor.context.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_opengl_context_descriptor_legacy>();
            descriptor.context.platform = mln_opengl_context_platform.MLN_OPENGL_CONTEXT_PLATFORM_WGL;
            descriptor.context.data.wgl.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_wgl_context_descriptor>();
            descriptor.context.data.wgl.device_context = (void*)_wglContext.DeviceContext;
            descriptor.context.data.wgl.share_context = (void*)_wglContext.ShareContext;
            descriptor.context.data.wgl.get_proc_address = null;

            mln_render_session* session;
            mln_status status = NativeMethods.mln_opengl_owned_texture_attach(_map, (mln_opengl_owned_texture_descriptor*)&descriptor, &session);
            ThrowIfNotOk(status, "mln_opengl_owned_texture_attach");
            _session = session;
#else
            mln_opengl_owned_texture_descriptor descriptor = NativeMethods.mln_opengl_owned_texture_descriptor_default();
            descriptor.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_opengl_owned_texture_descriptor>();

            descriptor.extent.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_render_target_extent>();
            descriptor.extent.width = (uint)width;
            descriptor.extent.height = (uint)height;
            descriptor.extent.scale_factor = scaleFactor;

            descriptor.context.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_opengl_context_descriptor>();
            descriptor.context.platform = mln_opengl_context_platform.MLN_OPENGL_CONTEXT_PLATFORM_EGL;
            descriptor.context.ownership = mln_opengl_context_ownership.MLN_OPENGL_CONTEXT_OWNERSHIP_SHARED;

            descriptor.context.data.egl.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<mln_egl_context_descriptor>();
            descriptor.context.data.egl.display = (void*)_eglContext.Display;
            descriptor.context.data.egl.config = (void*)_eglContext.Config;
            descriptor.context.data.egl.share_context = (void*)_eglContext.ShareContext;
            descriptor.context.data.egl.client_api = mln_opengl_client_api.MLN_OPENGL_CLIENT_API_GLES;
            descriptor.context.data.egl.get_proc_address = null;

            mln_render_session* session;
            mln_status status = NativeMethods.mln_opengl_owned_texture_attach(_map, &descriptor, &session);
            ThrowIfNotOk(status, "mln_opengl_owned_texture_attach");
            _session = session;
#endif
        }

        public void SetStyleUrl(string styleUrl)
        {
            if (string.IsNullOrEmpty(styleUrl)) return;

            // 余計な file:// 追加等の小細工を一切やめ、MapViewControllerから来たパスをそのまま渡す
            byte[] urlUtf8 = System.Text.Encoding.UTF8.GetBytes(styleUrl + "\0");
            fixed (byte* urlPtr = urlUtf8)
            {
                mln_status status = NativeMethods.mln_map_set_style_url(_map, (sbyte*)urlPtr);
                if (status != mln_status.MLN_STATUS_OK)
                {
                    Debug.LogWarning($"mln_map_set_style_url failed: {status}");
                }
            }
        }

        public void SetCamera(double latitude, double longitude, double zoom, double bearing, double pitch)
        {
            // ★ ここだけが唯一の機能追加（Androidで操作した瞬間のクラッシュを防ぐ安全装置）
            latitude = Math.Max(-85.0511, Math.Min(85.0511, latitude));
            zoom = Math.Max(0.0, Math.Min(22.0, zoom));
            pitch = Math.Max(0.0, Math.Min(60.0, pitch));

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
            if (status != mln_status.MLN_STATUS_OK)
            {
                Debug.LogWarning($"mln_map_jump_to failed: {status}");
            }
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
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            mln_status runStatus = NativeMethods.mln_runtime_run_once(_runtime);
            if (runStatus != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"mln_runtime_run_once failed: {runStatus}");
                return;
            }

            DrainEventsLegacy();

            if (_renderPending)
            {
                mln_status renderStatus = NativeMethods.mln_render_session_render_update_legacy(_session);
                if (renderStatus == mln_status.MLN_STATUS_OK)
                {
                    _renderPending = false;
                }
            }
#else
            mln_status pumpStatus = NativeMethods.mln_runtime_pump(_runtime, 0, -1);
            if (pumpStatus != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"mln_runtime_pump failed: {pumpStatus}");
                return;
            }

            DrainEventsNew();

            if (_renderPending)
            {
                mln_render_result result = mln_render_result.MLN_RENDER_RESULT_NO_UPDATE;
                bool needsRepaint = false;
                mln_status renderStatus = NativeMethods.mln_render_session_render_update_new(_session, &result, out needsRepaint);
                if (renderStatus == mln_status.MLN_STATUS_OK)
                {
                    _renderPending = needsRepaint;
                }
            }
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private void DrainEventsLegacy()
        {
            bool hasEvent;

            while (true)
            {
                mln_runtime_event evt = default;
                evt.size = (uint)sizeof(mln_runtime_event);

                mln_status status = NativeMethods.mln_runtime_poll_event(_runtime, &evt, out hasEvent);
                if (status != mln_status.MLN_STATUS_OK)
                {
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
        }
#else
        private void DrainEventsNew()
        {
            mln_runtime_event_batch batch = NativeMethods.mln_runtime_event_batch_default();
            batch.size = (uint)sizeof(mln_runtime_event_batch);

            mln_status status = NativeMethods.mln_runtime_drain_events(_runtime, 0, &batch);
            if (status != mln_status.MLN_STATUS_OK)
            {
                Debug.LogError($"mln_runtime_drain_events failed: {status}");
                return;
            }

            if (batch.event_count == 0 || batch.events == null)
            {
                return;
            }

            byte* eventsPtr = (byte*)batch.events;
            uint stride = batch.event_size > 0 ? batch.event_size : (uint)sizeof(mln_runtime_event);

            for (nuint i = 0; i < batch.event_count; i++)
            {
                mln_runtime_event* evt = (mln_runtime_event*)(eventsPtr + (i * stride));
                if (evt->source_type != (uint)mln_runtime_event_source_type.MLN_RUNTIME_EVENT_SOURCE_MAP || evt->source != _map)
                {
                    continue;
                }

                if (evt->type == (uint)mln_runtime_event_type.MLN_RUNTIME_EVENT_MAP_RENDER_UPDATE_AVAILABLE)
                {
                    _renderPending = true;
                }
                else if (evt->type == (uint)mln_runtime_event_type.MLN_RUNTIME_EVENT_MAP_RENDER_FRAME_FINISHED &&
                         evt->payload_type == (uint)mln_runtime_event_payload_type.MLN_RUNTIME_EVENT_PAYLOAD_RENDER_FRAME &&
                         evt->payload != null)
                {
                    var frame = (mln_runtime_event_render_frame*)evt->payload;
                    if (frame->needs_repaint != 0)
                    {
                        _renderPending = true;
                    }
                }
            }
        }
#endif

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
            if (requiredLength <= 0)
            {
                width = 0;
                height = 0;
                stride = 0;
                return false;
            }

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
            _renderPending = true;
            if (_map != null)
            {
                mln_status status = NativeMethods.mln_map_request_repaint(_map);
                if (status != mln_status.MLN_STATUS_OK)
                {
                    Debug.LogError($"mln_map_request_repaint failed: {status}");
                }
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