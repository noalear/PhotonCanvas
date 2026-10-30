# Third-party notices

PhotonCanvas is free to use; all rights reserved (see `LICENSE`). The components below are
not covered by that licence: each keeps its own, and nothing in PhotonCanvas's licence limits it.

It uses the following open-source components, each under its own licence. Their
licence texts ship inside their NuGet packages and are linked below.

| Component | Used for | Licence |
|-----------|----------|---------|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) 11.3 (incl. Fluent theme, ColorPicker) | User interface | MIT |
| [Inter](https://github.com/rsms/inter) font, via Avalonia.Fonts.Inter | Interface font | SIL Open Font License 1.1 |
| [SkiaSharp](https://github.com/mono/SkiaSharp) 2.88 | Drawing, pictures, text outlines | MIT |
| [Skia](https://skia.org) (native, bundled with SkiaSharp) | Graphics engine | BSD 3-Clause (plus the notices in SkiaSharp's `THIRD-PARTY-NOTICES.txt`: libpng, libjpeg-turbo, libwebp, FreeType, zlib, expat…) |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) / HarfBuzz (via Avalonia) | Text shaping | MIT / Old MIT |
| [Clipper2](https://github.com/AngusJohnson/Clipper2) 1.5 | Offsets and shape operations | Boost Software License 1.0 |
| [PdfPig](https://github.com/UglyToad/PdfPig) 0.1 | Importing PDF and AI drawings | Apache License 2.0 |
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) 0.16 | QR codes, Data Matrix and barcodes | Apache License 2.0 |
| [.NET](https://github.com/dotnet/runtime) runtime | Platform | MIT |
| [ASP.NET Core](https://github.com/dotnet/aspnetcore) (part of .NET) | The remote server for the phone | MIT |
| [System.IO.Ports](https://github.com/dotnet/runtime) | USB serial connection to GRBL machines | MIT |

Test-only: xUnit (Apache 2.0), Microsoft.NET.Test.Sdk (MIT), coverlet (MIT).

## earcut (mapbox)

`src/PhotonCanvas.Doc/Import/Step/Earcut.cs` follows the design of Mapbox's
[earcut](https://github.com/mapbox/earcut) polygon triangulation, under the ISC licence:

```
ISC License

Copyright (c) 2016, Mapbox

Permission to use, copy, modify, and/or distribute this software for any purpose
with or without fee is hereby granted, provided that the above copyright notice
and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND ISC DISCLAIMS ALL WARRANTIES WITH REGARD TO
THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
IN NO EVENT SHALL ISC BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR
CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA
OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## The Android app (PhotonCanvas Remote)

Besides Avalonia, SkiaSharp/Skia, HarfBuzzSharp/HarfBuzz and the Inter font (above), the
Android app bundles these libraries, through their .NET for Android binding packages (the
`Xamarin.*` packages, MIT, .NET Foundation):

| Libraries | Licence |
|-----------|---------|
| AndroidX: Activity, Annotation (incl. Experimental), AppCompat (incl. AppCompatResources), Arch Core, Collection, Compose Runtime Annotation, Concurrent Futures, Core (incl. Core KTX, ViewTree), CursorAdapter, CustomView, DrawerLayout, Emoji2 (incl. ViewsHelper), ExifInterface, Fragment, Interpolator, Lifecycle (Common, LiveData Core, Process, Runtime, Runtime KTX, ViewModel, ViewModel KTX, ViewModel SavedState), Loader, NavigationEvent, ProfileInstaller, ResourceInspection Annotation, SavedState (incl. KTX), Startup Runtime, Tracing, VectorDrawable (incl. Animated), VersionedParcelable, ViewPager | Apache License 2.0 |
| Kotlin standard library (incl. JDK 7 and JDK 8 extensions), kotlinx.coroutines (Core, Android), kotlinx.serialization (Core), kotlinx atomicfu | Apache License 2.0 |
| JetBrains Java annotations, JSpecify, javax.inject, Guava ListenableFuture, Error Prone annotations | Apache License 2.0 |
| Firebase Components, Annotations and Encoders (JSON, Proto); Google Android DataTransport (API, runtime, CCT backend) | Apache License 2.0 |
| Google Play services (Base, Basement, Tasks, Code Scanner), ML Kit (Common, Vision Common, Barcode Scanning Common), ODML Image | Google's terms: the Android Software Development Kit License and the Google APIs Terms of Service. Not open source. |

The Apache License 2.0 text is at <https://www.apache.org/licenses/LICENSE-2.0>.

The QR code scanner is Google Play services' code scanner, a Google component: it runs inside
Google Play services on the phone, under Google's terms, and may communicate with Google. The app
itself only receives the text of the scanned code.

The Python protocol prototype in `src/pcprobe` (not part of the app) uses websockets
(BSD 3-Clause), cryptography (Apache 2.0 / BSD) and Pillow (MIT-CMU).

## Trademarks

xTool is a trademark of its owner. It is used here only to say which machines and
file formats this program works with. This project is not affiliated with, endorsed
by or supported by xTool or Makeblock.

## Interoperability

This program contains no code from xTool Studio. The machine's network protocol and
the G-code, job and project file formats were studied for interoperability with the
author's own machine and software. The three connection keys the machines expect are
not included: PhotonCanvas reads them from the user's own installed copy of xTool
Studio, or the user types them in.
