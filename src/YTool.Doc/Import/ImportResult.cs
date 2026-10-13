using YTool.Core;

namespace YTool.Doc.Import;

/// <summary>
/// A picture found in an imported file. In its own space it covers (0, 0)–<see cref="SizeMm"/>;
/// <see cref="Transform"/> places it on the canvas (millimetres, +Y down), exactly like
/// <see cref="ImageShape"/>.
/// </summary>
/// <param name="Encoded">The picture file bytes (PNG, JPEG, …) as stored in the source file.</param>
/// <param name="SizeMm">Its size before rotation, in millimetres.</param>
/// <param name="Transform">Local millimetres → canvas millimetres.</param>
/// <param name="Name">A label for the object list.</param>
/// <param name="Color">The layer colour the picture belonged to, when the file had one.</param>
public sealed record ImportedImage(byte[] Encoded, Vec2 SizeMm, Affine Transform, string Name, uint? Color);

/// <summary>
/// Everything read from a design file: vector paths and pictures in machine millimetres
/// (+Y down, origin top-left), plus notes about anything that could not be brought in.
/// </summary>
public sealed record ImportResult(List<ImportedPath> Paths, List<ImportedImage> Images, List<string> Warnings)
{
    /// <summary>
    /// Layer names from the file, keyed by layer colour (0xRRGGBB). Empty when the file
    /// has no named layers.
    /// </summary>
    public Dictionary<uint, string> LayerNames { get; init; } = [];

    /// <summary>The area covered by every path and picture.</summary>
    public Rect2 Bounds
    {
        get
        {
            var r = Paths.Aggregate(Rect2.Empty, (acc, p) => acc.Union(p.Geometry.Bounds()));
            foreach (var img in Images)
            {
                var t = img.Transform;
                var s = img.SizeMm;
                r = r.Include(t.Apply(new(0, 0))).Include(t.Apply(new(s.X, 0)))
                     .Include(t.Apply(s)).Include(t.Apply(new(0, s.Y)));
            }
            return r;
        }
    }

    /// <summary>True when nothing drawable was found.</summary>
    public bool IsEmpty => Paths.Count == 0 && Images.Count == 0;
}

/// <summary>
/// Opens any supported design file by its extension: SVG, DXF, PDF, Adobe Illustrator
/// (.ai, saved with PDF compatibility) and xTool Studio projects (.xs, .xcs).
/// </summary>
public static class Importer
{
    /// <summary>File extensions (lower case, with the dot) that <see cref="Load"/> understands.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".svg", ".dxf", ".pdf", ".ai", ".xs", ".xcs"];

    /// <summary>Whether <paramref name="path"/> has an extension <see cref="Load"/> can open.</summary>
    public static bool CanLoad(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Read a design file.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <param name="svgPxPerInch">
    /// For SVG files without a physical size: how many pixels make an inch
    /// (96 for Inkscape and browsers, 72 for old Illustrator exports).
    /// </param>
    /// <exception cref="FormatException">The file type is not supported or the file is damaged.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static ImportResult Load(string path, double svgPxPerInch = 96)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".svg" => FromSvg(SvgImport.Load(path, new SvgImportOptions { PxPerInch = svgPxPerInch })),
            ".dxf" => DxfImport.Load(path),
            ".pdf" => PdfImport.Load(path),
            ".ai" => PdfImport.LoadIllustrator(path),
            ".xs" or ".xcs" => XsImport.Load(path),
            "" => throw new FormatException(
                $"\"{Path.GetFileName(path)}\" has no file extension, so its type is unknown. Supported: {string.Join(", ", Extensions)}."),
            _ => throw new FormatException(
                $"{ext} files can't be imported. Supported: {string.Join(", ", Extensions)}."),
        };
    }

    static ImportResult FromSvg(SvgImportResult r) => new(r.Paths, [], r.Warnings);
}
