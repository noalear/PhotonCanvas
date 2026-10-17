# Third-party notices

PhotonCanvas is licensed under the GNU General Public License v3 or later (see `LICENSE`).
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

Test-only: xUnit (Apache 2.0), Microsoft.NET.Test.Sdk (MIT), coverlet (MIT).

The Python protocol prototype in `src/ytool` (not part of the app) uses websockets
(BSD 3-Clause), cryptography (Apache 2.0 / BSD) and Pillow (MIT-CMU).

## Trademarks

xTool is a trademark of its owner. It is used here only to say which machines and
file formats this program works with. This project is not affiliated with, endorsed
by or supported by xTool or Makeblock.

## Interoperability

This program contains no code from xTool Studio. The machine protocol and file
formats were worked out from the behaviour of the user's own machine and software,
for the purpose of interoperability. The three connection keys the machines expect
are not included: each user reads them from their own installed copy of xTool Studio.
