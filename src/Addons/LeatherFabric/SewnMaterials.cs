namespace PhotonCanvas.Addon.LeatherFabric;

/// <summary>What the pieces are cut from: it sets the usual settings and what to watch out for.</summary>
public enum SewnMaterial { VegTan, ChromeTan, FauxPu, Vinyl, Cotton, Synthetic, WoolFelt, Other }

/// <summary>A material's name, whether it is a fabric, the library category it lives in, and its warning.</summary>
/// <param name="Never">Must not go in a laser at all (nothing is made for it).</param>
public sealed record SewnMaterialInfo(SewnMaterial Kind, string Name, bool Fabric, string Category, string Note, bool Danger, bool Never);

public static class SewnMaterials
{
    public static readonly IReadOnlyList<SewnMaterialInfo> All =
    [
        new(SewnMaterial.VegTan, "Vegetable-tanned leather", false, "Leather",
            "Cuts and marks well. It smokes and smells: run the air assist and the extraction. Cut edges come out dark brown; soot on the top wipes off with a damp cloth (or put transfer tape on first).",
            false, false),
        new(SewnMaterial.ChromeTan, "Chrome-tanned leather", false, "Leather",
            "Most soft, coloured leather (garment, upholstery, many wallets) is chrome-tanned. Lasered, it can give off hexavalent chromium, which causes cancer. Best not lasered; if at all, only with the extraction vented outdoors. Vegetable-tanned leather is the safe choice.",
            true, false),
        new(SewnMaterial.FauxPu, "Faux leather (PU)", false, "Leather",
            "Polyurethane faux leather gives off harmful fumes when lasered: only with the extraction vented outdoors. Make sure it is PU and not PVC (\"vinyl\", \"leatherette\"): ask the seller if the label doesn't say.",
            true, false),
        new(SewnMaterial.Vinyl, "Vinyl or PVC faux leather", false, "Leather",
            "Never laser PVC (\"vinyl\", \"leatherette\", many coated fabrics): it gives off chlorine gas that harms your lungs and eats the machine's metal and optics. Cut it by hand or with a blade cutter instead. Nothing is made for it here.",
            true, true),
        new(SewnMaterial.Cotton, "Cotton, linen, canvas or denim", true, "Fabric",
            "Natural fibres char slightly at the edge and can catch fire: air assist on, never leave the machine running. Hold the cloth flat (tape the edges, or pins or magnets on the honeycomb) and try a scrap first.",
            false, false),
        new(SewnMaterial.Synthetic, "Polyester, nylon or fleece", true, "Fabric",
            "The edges melt and seal, so they don't fray. It gives off fumes: run the extraction. Waterproof and coated fabrics are often PVC-coated: check before cutting those.",
            false, false),
        new(SewnMaterial.WoolFelt, "Wool, felt or silk", true, "Fabric",
            "Cuts cleanly with a smell of burnt hair. Acrylic craft felt melts at the edge instead. Hold it flat and try a scrap first.",
            false, false),
        new(SewnMaterial.Other, "Something else", false, "Leather",
            "Find out what it is made of before lasering it: anything with PVC or vinyl in it must not go in the machine.",
            false, false),
    ];

    public static SewnMaterialInfo Info(SewnMaterial m) => All.FirstOrDefault(i => i.Kind == m) ?? All[^1];
}
