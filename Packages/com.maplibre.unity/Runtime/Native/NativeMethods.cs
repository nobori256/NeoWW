using System.Runtime.InteropServices;

namespace MapLibre.Unity.Native
{
    internal static unsafe partial class NativeMethods
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string LibName = "__Internal";
#else
        private const string LibName = "maplibre-native-c";
#endif

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_runtime_options mln_runtime_options_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_runtime_create(mln_runtime_options* options, mln_runtime** out_runtime);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_runtime_destroy(mln_runtime* runtime);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_runtime_event_batch mln_runtime_event_batch_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_runtime_pump(mln_runtime* runtime, long timeout_ms, long budget_ms);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_runtime_drain_events(mln_runtime* runtime, nuint max_events, mln_runtime_event_batch* out_batch);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_render_session_render_update")]
        public static extern mln_status mln_render_session_render_update_new(mln_render_session* session, mln_render_result* out_result, [MarshalAs(UnmanagedType.I1)] out bool out_needs_repaint);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_map_options mln_map_options_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_map_create(mln_runtime* runtime, mln_map_options* options, mln_map** out_map);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_map_destroy(mln_map* map);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_map_set_style_url(mln_map* map, sbyte* style_url);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_camera_options mln_camera_options_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_map_jump_to(mln_map* map, mln_camera_options* camera);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_map_move_by(mln_map* map, double delta_x, double delta_y);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_map_scale_by(mln_map* map, double scale, mln_screen_point* anchor);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_map_request_repaint(mln_map* map);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_opengl_owned_texture_descriptor mln_opengl_owned_texture_descriptor_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_metal_owned_texture_descriptor mln_metal_owned_texture_descriptor_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_opengl_owned_texture_attach(mln_map* map, mln_opengl_owned_texture_descriptor* descriptor, mln_render_session** out_session);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_metal_owned_texture_attach(mln_map* map, mln_metal_owned_texture_descriptor* descriptor, mln_render_session** out_session);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_render_session_destroy(mln_render_session* session);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_render_session_resize(mln_render_session* session, uint width, uint height, double scale_factor);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_texture_image_info mln_texture_image_info_default();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_texture_read_premultiplied_rgba8(mln_render_session* session, byte* out_buffer, nuint buffer_size, mln_texture_image_info* out_info);

        // Windows用旧イベントAPI・レガシー定義
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_runtime_run_once(mln_runtime* runtime);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern mln_status mln_runtime_poll_event(mln_runtime* runtime, mln_runtime_event* evt, [MarshalAs(UnmanagedType.I1)] out bool has_event);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_render_session_render_update")]
        public static extern mln_status mln_render_session_render_update_legacy(mln_render_session* session);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_runtime_options_default")]
        public static extern mln_runtime_options_legacy mln_runtime_options_default_legacy();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_runtime_create")]
        public static extern mln_status mln_runtime_create_legacy(mln_runtime_options_legacy* options, mln_runtime** out_runtime);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_map_options_default")]
        public static extern mln_map_options_legacy mln_map_options_default_legacy();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_map_create")]
        public static extern mln_status mln_map_create_legacy(mln_runtime* runtime, mln_map_options_legacy* options, mln_map** out_map);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_camera_options_default")]
        public static extern mln_camera_options_legacy mln_camera_options_default_legacy();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_map_jump_to")]
        public static extern mln_status mln_map_jump_to_legacy(mln_map* map, mln_camera_options_legacy* camera);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_texture_image_info_default")]
        public static extern mln_texture_image_info_legacy mln_texture_image_info_default_legacy();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_texture_read_premultiplied_rgba8")]
        public static extern mln_status mln_texture_read_premultiplied_rgba8_legacy(mln_render_session* session, byte* out_buffer, nuint buffer_size, mln_texture_image_info_legacy* out_info);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mln_opengl_owned_texture_attach")]
        public static extern mln_status mln_opengl_owned_texture_attach_legacy(mln_map* map, mln_opengl_owned_texture_descriptor_legacy* descriptor, mln_render_session** out_session);
    }
}