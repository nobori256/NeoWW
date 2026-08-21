#import <Metal/Metal.h>

// Minimal Metal bridge for the MapLibre Unity plugin.
//
// Unity does not expose the app's MTLDevice to C# scripts, so this bridge creates and
// owns a reference to the system default Metal device. The MapLibre render session
// renders offscreen on this device and the plugin reads frames back on the CPU, so the
// device does not need to be the same object Unity renders with.

extern "C" void* mlu_metal_create_system_default_device(void) {
    id<MTLDevice> device = MTLCreateSystemDefaultDevice();
    if (device == nil) {
        return NULL;
    }
    // __bridge_retained: transfer ownership to the caller; released via mlu_metal_release_device.
    return (__bridge_retained void*)device;
}

extern "C" void mlu_metal_release_device(void* device) {
    if (device != NULL) {
        CFRelease(device);
    }
}
