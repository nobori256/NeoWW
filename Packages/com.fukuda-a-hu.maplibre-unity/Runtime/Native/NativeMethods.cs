using System.Runtime.InteropServices;

// P/Invoke declarations for the maplibre-native-c FFI, hand-ported from the upstream C# sample bindings
// (base.g.cs, runtime.g.cs, map.g.cs, camera.g.cs, render_target.g.cs, texture.g.cs, render_session.g.cs,
// logging.g.cs). Only the subset needed by this Unity plugin (Windows x64, Android arm64, iOS arm64) is
// declared here.
//
// The DLL name intentionally omits any extension/path - Unity's PluginImporter resolves
// "maplibre-native-c" to Runtime/Plugins/Windows/x86_64/maplibre-native-c.dll on Windows x64 and to
// Runtime/Plugins/Android/arm64-v8a/libmaplibre-native-c.so on Android.
namespace MapLibre.Unity.Native
{
    internal static unsafe class NativeMethods
    {
#if UNITY_IOS && !UNITY_EDITOR
        // iOS plugins are statically linked into the player binary, so P/Invoke must target
        // the host image itself rather than a dynamic library name.
        private const string LibName = "__Internal";
#else
        private const string LibName = "maplibre-native-c";
#endif

        // ---- base.h -------------------------------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern uint mln_c_version();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern uint mln_supported_render_backend_mask();

        // ---- runtime.h ------------------------------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_runtime_options mln_runtime_options_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_runtime_create(mln_runtime_options* options, mln_runtime** out_runtime);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_runtime_destroy(mln_runtime* runtime);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_runtime_run_once(mln_runtime* runtime);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_runtime_poll_event(mln_runtime* runtime, mln_runtime_event* out_event, bool* out_has_event);

        // ---- map.h ----------------------------------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_map_options mln_map_options_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_map_create(mln_runtime* runtime, mln_map_options* options, mln_map** out_map);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_map_request_repaint(mln_map* map);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_map_destroy(mln_map* map);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_map_set_style_url(mln_map* map, sbyte* url);

        // ---- camera.h -------------------------------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_camera_options mln_camera_options_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_map_jump_to(mln_map* map, mln_camera_options* camera);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_map_move_by(mln_map* map, double delta_x, double delta_y);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_map_scale_by(mln_map* map, double scale, mln_screen_point* anchor);

        // ---- render_target.h / opengl context ---------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern uint mln_opengl_supported_context_provider_mask();

        // ---- texture.h --------------------------------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_opengl_owned_texture_descriptor mln_opengl_owned_texture_descriptor_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_texture_image_info mln_texture_image_info_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_opengl_owned_texture_attach(mln_map* map, mln_opengl_owned_texture_descriptor* descriptor, mln_render_session** out_session);

        // iOS is Metal-only. Declared unconditionally (not gated behind UNITY_IOS) since a DllImport declaration
        // is only resolved when actually called - the Windows/Android binaries simply never invoke this entry
        // point.
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_metal_owned_texture_descriptor mln_metal_owned_texture_descriptor_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_metal_owned_texture_attach(mln_map* map, mln_metal_owned_texture_descriptor* descriptor, mln_render_session** out_session);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_texture_read_premultiplied_rgba8(mln_render_session* session, byte* out_data, nuint out_data_capacity, mln_texture_image_info* out_info);

        // ---- render_session.h -------------------------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_render_session_resize(mln_render_session* session, uint width, uint height, double scale_factor);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_render_session_render_update(mln_render_session* session);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_render_session_destroy(mln_render_session* session);

        // ---- logging.h --------------------------------------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_log_set_callback(delegate* unmanaged[Cdecl]<void*, uint, uint, long, sbyte*, uint> callback, void* user_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern mln_status mln_log_clear_callback();
    }
}
