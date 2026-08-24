using System.Runtime.InteropServices;

namespace MapLibre.Unity.Native
{
    internal unsafe partial struct mln_runtime { }
    internal unsafe partial struct mln_map { }
    internal unsafe partial struct mln_render_session { }

    internal enum mln_status : int
    {
        MLN_STATUS_OK = 0,
        MLN_STATUS_INVALID_ARGUMENT = -1,
        MLN_STATUS_INVALID_STATE = -2,
        MLN_STATUS_WRONG_THREAD = -3,
        MLN_STATUS_UNSUPPORTED = -4,
        MLN_STATUS_NATIVE_ERROR = -5,
    }

    internal enum mln_render_backend_flag : uint
    {
        MLN_RENDER_BACKEND_FLAG_METAL = 1u << 0,
        MLN_RENDER_BACKEND_FLAG_VULKAN = 1u << 1,
        MLN_RENDER_BACKEND_FLAG_OPENGL = 1u << 2,
    }

    internal enum mln_runtime_option_flag : uint
    {
        MLN_RUNTIME_OPTION_MAXIMUM_CACHE_SIZE = 1u << 0,
    }

    internal unsafe partial struct mln_runtime_options
    {
        public uint size;
        public uint flags; 
        public sbyte* asset_path; 
        public sbyte* cache_path; 
        public ulong maximum_cache_size;
    }

    internal enum mln_runtime_event_type : uint
    {
        MLN_RUNTIME_EVENT_MAP_CAMERA_WILL_CHANGE = 1,
        MLN_RUNTIME_EVENT_MAP_CAMERA_IS_CHANGING = 2,
        MLN_RUNTIME_EVENT_MAP_CAMERA_DID_CHANGE = 3,
        MLN_RUNTIME_EVENT_MAP_STYLE_LOADED = 4,
        MLN_RUNTIME_EVENT_MAP_LOADING_STARTED = 5,
        MLN_RUNTIME_EVENT_MAP_LOADING_FINISHED = 6,
        MLN_RUNTIME_EVENT_MAP_LOADING_FAILED = 7,
        MLN_RUNTIME_EVENT_MAP_IDLE = 8,
        MLN_RUNTIME_EVENT_MAP_RENDER_UPDATE_AVAILABLE = 9,
        MLN_RUNTIME_EVENT_MAP_RENDER_ERROR = 10,
        MLN_RUNTIME_EVENT_MAP_STILL_IMAGE_FINISHED = 11,
        MLN_RUNTIME_EVENT_MAP_STILL_IMAGE_FAILED = 12,
        MLN_RUNTIME_EVENT_MAP_RENDER_FRAME_STARTED = 13,
        MLN_RUNTIME_EVENT_MAP_RENDER_FRAME_FINISHED = 14,
        MLN_RUNTIME_EVENT_MAP_RENDER_MAP_STARTED = 15,
        MLN_RUNTIME_EVENT_MAP_RENDER_MAP_FINISHED = 16,
        MLN_RUNTIME_EVENT_MAP_STYLE_IMAGE_MISSING = 17,
        MLN_RUNTIME_EVENT_MAP_TILE_ACTION = 18,
        MLN_RUNTIME_EVENT_OFFLINE_REGION_STATUS_CHANGED = 19,
        MLN_RUNTIME_EVENT_OFFLINE_REGION_RESPONSE_ERROR = 20,
        MLN_RUNTIME_EVENT_OFFLINE_REGION_TILE_COUNT_LIMIT_EXCEEDED = 21,
        MLN_RUNTIME_EVENT_OFFLINE_OPERATION_COMPLETED = 22,
    }

    internal enum mln_runtime_event_source_type : uint
    {
        MLN_RUNTIME_EVENT_SOURCE_RUNTIME = 0,
        MLN_RUNTIME_EVENT_SOURCE_MAP = 1,
    }

    internal enum mln_runtime_event_payload_type : uint
    {
        MLN_RUNTIME_EVENT_PAYLOAD_NONE = 0,
        MLN_RUNTIME_EVENT_PAYLOAD_RENDER_FRAME = 1,
        MLN_RUNTIME_EVENT_PAYLOAD_RENDER_MAP = 2,
        MLN_RUNTIME_EVENT_PAYLOAD_STYLE_IMAGE_MISSING = 3,
        MLN_RUNTIME_EVENT_PAYLOAD_TILE_ACTION = 4,
        MLN_RUNTIME_EVENT_PAYLOAD_OFFLINE_REGION_STATUS = 5,
        MLN_RUNTIME_EVENT_PAYLOAD_OFFLINE_REGION_RESPONSE_ERROR = 6,
        MLN_RUNTIME_EVENT_PAYLOAD_OFFLINE_REGION_TILE_COUNT_LIMIT = 7,
        MLN_RUNTIME_EVENT_PAYLOAD_OFFLINE_OPERATION_COMPLETED = 8,
    }

    internal enum mln_render_mode : uint
    {
        MLN_RENDER_MODE_PARTIAL = 0,
        MLN_RENDER_MODE_FULL = 1,
    }

    internal partial struct mln_rendering_stats
    {
        public uint size;
        public double encoding_time;
        public double rendering_time;
        public long frame_count;
        public long draw_call_count;
        public long total_draw_call_count;
    }

    internal partial struct mln_runtime_event_render_frame
    {
        public uint size;
        public uint mode; 
        public byte needs_repaint; 
        public byte placement_changed; 
        public mln_rendering_stats stats;
    }

    internal partial struct mln_runtime_event_render_map
    {
        public uint size;
        public uint mode;
    }

    internal unsafe partial struct mln_runtime_event
    {
        public uint size;
        public uint type; 
        public uint source_type; 
        public void* source; 
        public int code;
        public uint payload_type; 
        public void* payload; 
        public nuint payload_size;
        public sbyte* message; 
        public nuint message_size;
    }

    internal enum mln_map_mode : uint
    {
        MLN_MAP_MODE_CONTINUOUS = 0,
        MLN_MAP_MODE_STATIC = 1,
        MLN_MAP_MODE_TILE = 2,
    }

    internal partial struct mln_map_options
    {
        public uint size;
        public uint width;
        public uint height;
        public double scale_factor;
        public uint map_mode; 
    }

    internal partial struct mln_screen_point
    {
        public double x;
        public double y;
    }

    internal partial struct mln_edge_insets
    {
        public double top;
        public double left;
        public double bottom;
        public double right;
    }

    internal partial struct mln_lat_lng
    {
        public double latitude;
        public double longitude;
    }

    internal enum mln_camera_option_field : uint
    {
        MLN_CAMERA_OPTION_CENTER = 1u << 0,
        MLN_CAMERA_OPTION_ZOOM = 1u << 1,
        MLN_CAMERA_OPTION_BEARING = 1u << 2,
        MLN_CAMERA_OPTION_PITCH = 1u << 3,
        MLN_CAMERA_OPTION_CENTER_ALTITUDE = 1u << 4,
        MLN_CAMERA_OPTION_PADDING = 1u << 5,
        MLN_CAMERA_OPTION_ANCHOR = 1u << 6,
        MLN_CAMERA_OPTION_ROLL = 1u << 7,
        MLN_CAMERA_OPTION_FOV = 1u << 8,
    }

    internal partial struct mln_camera_options
    {
        public uint size;
        public uint fields; 
        public double latitude;
        public double longitude;
        public double center_altitude;
        public mln_edge_insets padding;
        public mln_screen_point anchor;
        public double zoom;
        public double bearing;
        public double pitch;
        public double roll;
        public double field_of_view;
    }

    internal partial struct mln_render_target_extent
    {
        public uint size;
        public uint width; 
        public uint height; 
        public double scale_factor; 
    }

    internal enum mln_opengl_context_provider_flag : uint
    {
        MLN_OPENGL_CONTEXT_PROVIDER_FLAG_WGL = 1u << 0,
        MLN_OPENGL_CONTEXT_PROVIDER_FLAG_EGL = 1u << 1,
    }

    internal enum mln_opengl_context_platform : uint
    {
        MLN_OPENGL_CONTEXT_PLATFORM_UNSPECIFIED = 0,
        MLN_OPENGL_CONTEXT_PLATFORM_WGL = 1,
        MLN_OPENGL_CONTEXT_PLATFORM_EGL = 2,
    }

    internal unsafe partial struct mln_wgl_context_descriptor
    {
        public uint size;
        public void* device_context; 
        public void* share_context; 
        public void* get_proc_address; 
    }

    internal unsafe partial struct mln_egl_context_descriptor
    {
        public uint size;
        public void* display;
        public void* config;
        public void* share_context;
        public void* get_proc_address;
    }

    internal partial struct mln_opengl_context_descriptor
    {
        public uint size;
        public mln_opengl_context_platform platform;
        public _data_e__Union data;

        [StructLayout(LayoutKind.Explicit)]
        internal unsafe partial struct _data_e__Union
        {
            [FieldOffset(0)]
            public mln_wgl_context_descriptor wgl;

            [FieldOffset(0)]
            public mln_egl_context_descriptor egl;
        }
    }

    internal unsafe partial struct mln_metal_context_descriptor
    {
        public uint size;
        public void* device; 
    }

    internal partial struct mln_opengl_owned_texture_descriptor
    {
        public uint size;
        public mln_render_target_extent extent;
        public mln_opengl_context_descriptor context; 
    }

    internal partial struct mln_metal_owned_texture_descriptor
    {
        public uint size;
        public mln_render_target_extent extent;
        public mln_metal_context_descriptor context; 
    }

    internal partial struct mln_texture_image_info
    {
        public uint size;
        public uint width; 
        public uint height; 
        public uint stride; 
        public nuint byte_length; 
    }

    internal enum mln_log_severity : uint
    {
        MLN_LOG_SEVERITY_INFO = 1,
        MLN_LOG_SEVERITY_WARNING = 2,
        MLN_LOG_SEVERITY_ERROR = 3,
    }

    internal enum mln_log_event : uint
    {
        MLN_LOG_EVENT_GENERAL = 0,
    }

    // =========================================================================================
    // ★ Android用V2構造体（96バイト版）
    // Unityのコンパイルエラーを防ぐため、元の構造体とは完全に別名（_v2）で定義します
    // =========================================================================================

    internal enum mln_opengl_context_ownership_v2 : uint
    {
        MLN_OPENGL_CONTEXT_OWNERSHIP_SHARED = 0u,
        MLN_OPENGL_CONTEXT_OWNERSHIP_DEDICATED = 1u,
    }

    internal enum mln_opengl_client_api_v2 : uint
    {
        MLN_OPENGL_CLIENT_API_UNSPECIFIED = 0u,
        MLN_OPENGL_CLIENT_API_GL = 1u,
        MLN_OPENGL_CLIENT_API_GLES = 2u,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct mln_wgl_context_descriptor_v2
    {
        public uint size;
        public void* device_context;
        public void* share_context;
        public void* get_proc_address;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct mln_egl_context_descriptor_v2
    {
        public uint size;
        public mln_opengl_client_api_v2 client_api;
        public void* display;
        public void* config;
        public void* share_context;
        public void* get_proc_address;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe partial struct _data_e__Union_v2
    {
        [FieldOffset(0)]
        public mln_wgl_context_descriptor_v2 wgl;

        [FieldOffset(0)]
        public mln_egl_context_descriptor_v2 egl;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal partial struct mln_opengl_context_descriptor_v2
    {
        public uint size;
        public mln_opengl_context_platform platform;
        public mln_opengl_context_ownership_v2 ownership;
        public _data_e__Union_v2 data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal partial struct mln_opengl_owned_texture_descriptor_v2
    {
        public uint size;
        public mln_render_target_extent extent;
        public mln_opengl_context_descriptor_v2 context;
    }
}