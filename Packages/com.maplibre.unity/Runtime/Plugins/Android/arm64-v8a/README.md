# Android native binary (arm64-v8a)

Place `libmaplibre-native-c.so` in this directory.

## How to obtain

The official maplibre-native-ffi project (https://github.com/maplibre/maplibre-native-ffi)
supports Android via EGL (see `.mise/config.android-arm64-egl.toml`) but does **not**
currently publish prebuilt Android artifacts to GitHub Releases (as of 2026-07).

You must build the shared library yourself:

1. Clone https://github.com/maplibre/maplibre-native-ffi
2. Install `mise` and the Android NDK
3. `MISE_ENV=android-arm64-egl mise run configure && mise run build`
4. Copy the resulting `libmaplibre-native-c.so` here.

## Unity platform settings

When Unity imports this `.so`, set the following in the PluginImporter inspector:

- Select platforms: **Android** only
- CPU: **ARM64**
