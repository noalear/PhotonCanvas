using PhotonCanvas.Core;
using PhotonCanvas.Doc.Colour;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace PhotonCanvas.Doc;

/// <summary>The kinds of machine-readable code that can be marked.</summary>
public enum CodeKind { QR, DataMatrix, Code128, Code39, Ean13, UpcA, Ean8, UpcE, Itf, Code93, Codabar, Pdf417, Aztec }

/// <summary>QR error correction: how much of the code can be damaged and still read (L 7%, M 15%, Q 25%, H 30%).</summary>
public enum CodeEcc { L, M, Q, H }

/// <summary>What a code says and how it is drawn.</summary>
/// <param name="Content">What it encodes; may hold fields such as <c>{serial}</c>.</param>
/// <param name="ModuleMm">One square (2D codes) or the narrowest bar (barcodes), mm.</param>
/// <param name="BarHeightMm">Barcodes only: how tall the bars are, mm.</param>
/// <param name="Invert">Mark the light parts instead of the dark ones (for materials that mark light, such as anodised aluminium or dark slate).</param>
/// <param name="QuietModules">Clear margin around the code, in modules (marked when inverted).</param>
public sealed record CodeSpec(
    CodeKind Kind = CodeKind.QR,
    string Content = "",
    double ModuleMm = 0.5,
    double BarHeightMm = 12,
    bool Invert = false,
    int QuietModules = 0,
    CodeEcc Ecc = CodeEcc.M)
{
    public bool IsBarcode => Kind is CodeKind.Code128 or CodeKind.Code39 or CodeKind.Ean13 or CodeKind.UpcA or CodeKind.Ean8 or CodeKind.UpcE or CodeKind.Itf or CodeKind.Code93 or CodeKind.Codabar;

    /// <summary>Codes drawn as a grid of modules (not one row of bars).</summary>
    public bool IsMatrix => !IsBarcode;
}

/// <summary>
/// A QR code, Data Matrix or barcode, drawn as filled squares and bars (put it on an engrave
/// layer). Like text, its shape is rebuilt whenever what it says changes, including fields.
/// </summary>
public sealed class CodeShape : PathShape
{
    public required CodeSpec Spec { get; init; }
    /// <summary>The content with its fields filled in; null when it has none.</summary>
    public string? Shown { get; init; }
    /// <summary>Why the content can't be made into this kind of code (null = fine).</summary>
    public string? Error { get; init; }

    public static string KindName(CodeKind k) => k switch
    {
        CodeKind.QR => "QR code",
        CodeKind.DataMatrix => "Data Matrix",
        CodeKind.Code128 => "Code 128",
        CodeKind.Code39 => "Code 39",
        CodeKind.Ean13 => "EAN-13",
        CodeKind.UpcA => "UPC-A",
        CodeKind.Ean8 => "EAN-8",
        CodeKind.UpcE => "UPC-E",
        CodeKind.Itf => "ITF (Interleaved 2 of 5)",
        CodeKind.Code93 => "Code 93",
        CodeKind.Codabar => "Codabar",
        CodeKind.Pdf417 => "PDF417",
        _ => "Aztec",
    };

    public static CodeShape Create(CodeSpec spec, string? shown, Guid layerId, Affine transform, Guid? id = null, string? name = null, Guid? groupId = null)
    {
        var (geometry, error) = Build(spec, shown ?? spec.Content);
        return new CodeShape
        {
            Id = id ?? Guid.NewGuid(),
            Name = name ?? DefaultName(spec),
            LayerId = layerId,
            Transform = transform,
            GroupId = groupId,
            Spec = spec,
            Shown = shown,
            Error = error,
            Geometry = geometry,
        };
    }

    static string DefaultName(CodeSpec spec)
    {
        var c = spec.Content.Replace('\n', ' ');
        return $"{KindName(spec.Kind)} “{(c.Length > 20 ? c[..20] + "…" : c)}”";
    }

    /// <summary>The same code object (place, layer, group) with new settings.</summary>
    public CodeShape Edit(CodeSpec spec) => Keep(Create(spec, null, LayerId, Transform, Id, Name == DefaultName(Spec) ? null : Name, GroupId));

    /// <summary>The same code showing other values for its fields.</summary>
    public CodeShape WithShown(string? shown) => Keep(Create(Spec, shown, LayerId, Transform, Id, Name, GroupId));

    /// <summary>The code's modules, dark = true: columns × rows (one row for barcodes).</summary>
    public static (int Cols, int Rows, bool[] Dark) Encode(CodeKind kind, string text, CodeEcc ecc = CodeEcc.M)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 };
        BitMatrix m;
        switch (kind)
        {
            case CodeKind.QR:
                hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
                hints[EncodeHintType.ERROR_CORRECTION] = ecc switch
                {
                    CodeEcc.L => ErrorCorrectionLevel.L,
                    CodeEcc.Q => ErrorCorrectionLevel.Q,
                    CodeEcc.H => ErrorCorrectionLevel.H,
                    _ => ErrorCorrectionLevel.M,
                };
                m = new ZXing.QrCode.QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
                break;
            case CodeKind.DataMatrix:
                hints[EncodeHintType.DATA_MATRIX_SHAPE] = ZXing.Datamatrix.Encoder.SymbolShapeHint.FORCE_SQUARE;
                m = new ZXing.Datamatrix.DataMatrixWriter().encode(text, BarcodeFormat.DATA_MATRIX, 0, 0, hints);
                break;
            case CodeKind.Code128:
                m = new ZXing.OneD.Code128Writer().encode(text, BarcodeFormat.CODE_128, 0, 1, hints);
                break;
            case CodeKind.Code39:
                m = new ZXing.OneD.Code39Writer().encode(text.ToUpperInvariant(), BarcodeFormat.CODE_39, 0, 1, hints);
                break;
            case CodeKind.Ean13:
                m = new ZXing.OneD.EAN13Writer().encode(text, BarcodeFormat.EAN_13, 0, 1, hints);
                break;
            case CodeKind.UpcA:
                m = new ZXing.OneD.UPCAWriter().encode(text, BarcodeFormat.UPC_A, 0, 1, hints);
                break;
            case CodeKind.Ean8:
                m = new ZXing.OneD.EAN8Writer().encode(text, BarcodeFormat.EAN_8, 0, 1, hints);
                break;
            case CodeKind.UpcE:
                m = new ZXing.OneD.UPCEWriter().encode(text, BarcodeFormat.UPC_E, 0, 1, hints);
                break;
            case CodeKind.Itf:
                m = new ZXing.OneD.ITFWriter().encode(text, BarcodeFormat.ITF, 0, 1, hints);
                break;
            case CodeKind.Code93:
                m = new ZXing.OneD.Code93Writer().encode(text.ToUpperInvariant(), BarcodeFormat.CODE_93, 0, 1, hints);
                break;
            case CodeKind.Codabar:
                m = new ZXing.OneD.CodaBarWriter().encode(text.ToUpperInvariant(), BarcodeFormat.CODABAR, 0, 1, hints);
                break;
            case CodeKind.Pdf417:
                hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
                m = new ZXing.PDF417.PDF417Writer().encode(text, BarcodeFormat.PDF_417, 0, 0, hints);
                break;
            default:
                hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
                m = new ZXing.Aztec.AztecWriter().encode(text, BarcodeFormat.AZTEC, 0, 0, hints);
                break;
        }
        int rows = kind is CodeKind.QR or CodeKind.DataMatrix or CodeKind.Pdf417 or CodeKind.Aztec ? m.Height : 1;
        var dark = new bool[m.Width * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < m.Width; x++)
                dark[y * m.Width + x] = m[x, y];
        return (m.Width, rows, dark);
    }

    /// <summary>The code as filled outlines in mm (top-left at 0, 0), or empty and the reason it can't be made.</summary>
    public static (PathGeometry Geometry, string? Error) Build(CodeSpec spec, string text)
    {
        if (string.IsNullOrEmpty(text)) return (PathGeometry.Empty, "there's nothing to encode.");
        if (!(spec.ModuleMm > 0) || !double.IsFinite(spec.ModuleMm)) return (PathGeometry.Empty, "the module size must be more than 0.");
        (int Cols, int Rows, bool[] Dark) code;
        try { code = Encode(spec.Kind, text, spec.Ecc); }
        catch (Exception e) when (e is ArgumentException or WriterException or InvalidOperationException or IndexOutOfRangeException)
        {
            return (PathGeometry.Empty, spec.Kind switch
            {
                CodeKind.Ean13 => "EAN-13 needs 12 digits (the 13th, the check digit, is worked out).",
                CodeKind.UpcA => "UPC-A needs 11 digits (the 12th, the check digit, is worked out).",
                CodeKind.Code39 => "Code 39 takes only A–Z, 0–9, space and - . $ / + %.",
                CodeKind.Code128 => "Code 128 takes only plain (ASCII) characters.",
                CodeKind.Ean8 => "EAN-8 needs 7 digits (the 8th, the check digit, is worked out).",
                CodeKind.UpcE => "UPC-E needs 7 digits starting with 0 or 1 (the check digit is worked out).",
                CodeKind.Itf => "ITF takes an even number of digits only.",
                CodeKind.Code93 => "Code 93 takes only A–Z, 0–9, space and - . $ / + %.",
                CodeKind.Codabar => "Codabar takes digits and - $ : / . +, optionally between start/stop letters A–D.",
                _ => $"it can't be encoded ({e.Message}).",
            });
        }
        bool bars = spec.IsBarcode;
        int q = Math.Clamp(spec.QuietModules, 0, 20);
        if (spec.Invert) q = Math.Max(q, 1);
        int qy = bars ? 0 : q;
        int cols = code.Cols + 2 * q, rows = code.Rows + 2 * qy;
        var cells = new int[cols * rows];
        for (int y = 0; y < code.Rows; y++)
            for (int x = 0; x < code.Cols; x++)
                if (code.Dark[y * code.Cols + x]) cells[(y + qy) * cols + x + q] = 1;
        double cw = spec.ModuleMm, ch = bars ? Math.Max(0.1, spec.BarHeightMm) : spec.ModuleMm;
        return (new PathGeometry(PixelGrid.Shape(cols, rows, cells, spec.Invert ? 0 : 1, cw, ch, Affine.Identity)), null);
    }
}
