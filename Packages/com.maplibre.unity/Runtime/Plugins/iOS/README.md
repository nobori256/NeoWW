# iOS native library (arm64, Metal)

Place `libmaplibre-native-c.a` (static library, ios-arm64-metal variant) in this directory.

## How to obtain

The official maplibre-native-ffi project (https://github.com/maplibre/maplibre-native-ffi)
supports iOS via Metal (see `.mise/config.ios-arm64-metal.toml`) but does **not**
currently publish prebuilt iOS artifacts to GitHub Releases (as of 2026-07).

You must build the static library yourself on macOS with Xcode installed:

1. Clone https://github.com/maplibre/maplibre-native-ffi
2. Install `mise`
3. `MISE_ENV=ios-arm64-metal mise run configure && mise run build`
4. Copy the resulting `libmaplibre-native-c.a` here.

## Unity platform settings

When Unity imports the `.a`, set the following in the PluginImporter inspector:

- Select platforms: **iOS** only

The bundled `MapLibreUnityMetalBridge.mm` in this folder is compiled into the Xcode
project automatically and provides the MTLDevice used by the plugin.
