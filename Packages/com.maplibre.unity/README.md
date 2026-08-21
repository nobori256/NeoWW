# MapLibre for Unity (Package)

Experimental MapLibre Native bindings for Unity. Windows x64 is supported; Android arm64 and iOS arm64 are
experimental (the C# layer is implemented for both, but the native binaries are not bundled - see
[Android](#android-arm64-experimental) / [iOS](#ios-arm64-experimental-metal) below).

See the [repository README](https://github.com/fukuda-A-HU/maplibre-unity) for installation instructions, a quick
start guide, and architecture notes.

## Quick Start

1. In **Window > Package Manager**, select this package, open the **Samples** tab, and click **Import** next to
   **Basic Map**.
2. Open the imported `BasicMap.unity` scene and press **Play**.

Or add `MapLibreMapView` (`MapLibre.Unity` namespace) to any `GameObject` directly and read its `Texture` property
once the map starts rendering.

## Contents

- `Runtime/MapLibreMapView.cs` - `MonoBehaviour` that renders a MapLibre map into a `Texture2D`.
- `Runtime/MapLibreMapHandle.cs` - Unity-independent core wrapping the native `mln_*` C API lifecycle.
- `Runtime/Native/` - P/Invoke declarations, native structs/enums, and the WGL (Windows) / EGL (Android) /
  Metal (iOS) shared-context helpers.
- `Runtime/Plugins/Windows/x86_64/maplibre-native-c.dll` - bundled native binary (see `THIRD_PARTY_NOTICES.md` at the
  repository root for license information).
- `Runtime/Plugins/Android/arm64-v8a/` - Android native binary directory. **Not bundled** - see
  [Android](#android-arm64-experimental) below.
- `Runtime/Plugins/iOS/` - iOS native static library directory plus the bundled `MapLibreUnityMetalBridge.mm`
  Objective-C bridge. Library **not bundled** - see [iOS](#ios-arm64-experimental-metal) below.
- `Runtime/Geo/GeoMath.cs` - WGS84/ECEF/ENU geodesy and Web Mercator tile math shared by the 3D terrain and PLATEAU
  layers.
- `Runtime/Terrain/GsiTerrainLayer.cs` - `MonoBehaviour` that streams GSI elevation + aerial photo tiles into a
  textured 3D terrain mesh.
- `Runtime/Plateau/B3dm.cs`, `Runtime/Plateau/PlateauTilesetLayer.cs` - b3dm parsing and a `MonoBehaviour` that
  streams PLATEAU 3D Tiles buildings via glTFast.
- `Samples~/BasicMap` - the "Basic Map" sample (see the Samples tab in Package Manager), a full-screen `RawImage`
  demo with mouse-drag panning and scroll-wheel zooming.
- `Samples~/PlateauTerrain3D` - the "3D Terrain + PLATEAU (Japan)" sample (see below).

## 3D Terrain + PLATEAU (v0.3.0)

`GsiTerrainLayer` and `PlateauTilesetLayer` add an experimental 3D mode on top of the existing 2D map, using two
Japan-specific open data sources:

- **Terrain**: [GSI (Geospatial Information Authority of Japan)](https://www.gsi.go.jp/) `dem_png` elevation tiles
  and `seamlessphoto` aerial photo tiles, streamed as a small grid of textured meshes in a local East-North-Up
  frame centered on a configurable geodetic origin.
- **Buildings**: [Project PLATEAU](https://www.mlit.go.jp/plateau/) 3D city models, published as 3D Tiles 1.0 /
  b3dm, streamed and positioned using each tile's `CESIUM_RTC` center.

### Usage

1. Open **Window > Package Manager**, select **MapLibre for Unity**, open the **Samples** tab, and import
   **3D Terrain + PLATEAU (Japan)**.
2. Open the imported `PlateauTerrain3D.unity` scene and press **Play**. `com.unity.cloud.gltfast`,
   `com.unity.cloud.draco`, and `com.unity.nuget.newtonsoft-json` are declared as package dependencies and will be
   resolved automatically by the Unity Package Manager the first time the package is added - no manual setup is
   required.
3. To add either layer to your own scene, add a `GsiTerrainLayer` and/or `PlateauTilesetLayer` component
   (`MapLibre.Unity.Terrain` / `MapLibre.Unity.Plateau` namespaces) to a `GameObject` and configure the origin
   latitude/longitude in the Inspector.

### Data sources and terms of use

- **GSI tiles**: free to use, but the [GSI terms of use](https://www.gsi.go.jp/kikakuchousei/kikakuchousei40182.html)
  require attribution (e.g. "国土地理院" / "GSI Japan") wherever the tiles are displayed. The bundled sample
  includes an on-screen attribution label.
- **PLATEAU data**: published by MLIT (Japan's Ministry of Land, Infrastructure, Transport and Tourism) under a
  CC BY 4.0-equivalent open license; attribution to Project PLATEAU is required.
- Both tile services used by this package (`cyberjapandata.gsi.go.jp` and `assets.cms.plateau.reearth.io`) are
  provided on a best-effort, no-warranty basis by their respective operators - do not rely on them for
  production-critical availability.

## Android arm64 (Experimental)

As of v0.4.0, the C# layer supports Android via an EGL shared context (`EglSharedContext`), mirroring the
Windows/WGL path (`WglSharedContext`). `MapLibreMapHandle` selects WGL or EGL at compile time depending on the
build target.

**The native binary is not bundled.** `libmaplibre-native-c.so` must be built from
[maplibre-native-ffi](https://github.com/maplibre/maplibre-native-ffi) and placed under
`Runtime/Plugins/Android/arm64-v8a/` - see that folder's `README.md` for build instructions and the required
Unity PluginImporter settings.

## iOS arm64 (Experimental, Metal)

As of v0.5.0, the C# layer supports iOS via Metal (`MetalDeviceContext`). iOS's maplibre-native-ffi build is
Metal-only (there is no OpenGL/EGL context provider on iOS), so `MapLibreMapHandle` attaches a Metal
session-owned texture target (`mln_metal_owned_texture_attach`) instead of the WGL/EGL `mln_opengl_owned_texture_attach`
path used on Windows/Android; frame readback still goes through the same backend-agnostic
`mln_texture_read_premultiplied_rgba8` call. Since iOS plugins are statically linked into the player binary,
P/Invoke targets `__Internal` on iOS instead of a named library.

**The native static library is not bundled.** `libmaplibre-native-c.a` (the `ios-arm64-metal` variant) must be
built on macOS from [maplibre-native-ffi](https://github.com/maplibre/maplibre-native-ffi) and placed under
`Runtime/Plugins/iOS/` - see that folder's `README.md` for build instructions and the required Unity
PluginImporter settings. The bundled `MapLibreUnityMetalBridge.mm` in that same folder is compiled into the
Xcode project automatically and supplies the `MTLDevice` used by `MetalDeviceContext`, since Unity does not
expose one to C#.
