using static PhotonCanvas.Addon.Borders.BandProfile;
using static PhotonCanvas.Addon.Borders.Ornament;

namespace PhotonCanvas.Addon.Borders;

/// <summary>
/// The designs that come with the maker: picture frames, cabinet and furniture edges, and every
/// ornament on its own as a band. Bands go from the outer edge in; heights are mm above the deepest
/// point. An edge's outer side is the board's edge, so its profiles mostly fall toward it (Flip).
/// </summary>
public static class BorderPresets
{
    public const string Frames = "Picture frames";
    public const string Edges = "Cabinet and furniture edges";
    public const string Bands = "Ornament bands";
    public const string MyDesigns = "My designs";

    public static readonly IReadOnlyList<string> Categories = [Frames, Edges, Bands, MyDesigns];

    static BorderBand B(double w, double top, BandProfile p = Flat, double drop = 0, Ornament o = None, double carve = 0, int count = 3, bool flip = false, double repeat = 0) =>
        new() { WidthMm = w, TopMm = top, Profile = p, DropMm = drop, Ornament = o, CarveMm = carve, Count = count, Flip = flip, RepeatMm = repeat };

    static BorderDesign D(string category, string name, string about, params BorderBand[] bands) =>
        new() { Category = category, Name = name, About = about, Bands = bands };

    public static readonly IReadOnlyList<BorderDesign> FrameDesigns =
    [
        D(Frames, "Classic gilt", "Pearls on the outer bead, a cove up to egg and dart on an ovolo, a plain frieze, bead and reel, and a chamfer down to the picture.",
            B(3, 1.6, Bead, 0.8, Pearls, 0.5), B(6, 2.6, Cove, 1.0, flip: true), B(8, 3.0, RoundOver, 1.2, EggAndDart, 0.9),
            B(1.5, 1.8), B(12, 1.4), B(3, 1.6, Bead, 0.6, BeadAndReel, 0.5), B(4, 1.2, Slope, 1.2)),
        D(Frames, "Louis XVI", "A laurel-wrapped torus, a fluted frieze and a pearled sight edge.",
            B(2, 2.0), B(9, 2.8, Bead, 1.4, Laurel, 0.8), B(1.5, 1.6), B(10, 1.6, Flat, 0, FlutesAcross, 0.7),
            B(1.5, 1.6), B(4, 1.8, Bead, 0.8, Pearls, 0.6), B(4, 1.0, Cove, 1.0)),
        D(Frames, "Greek key", "A broad Greek key between two beads, with a cove down to the picture.",
            B(3, 2.0, Bead, 0.8), B(16, 2.0, Flat, 0, GreekKey, 1.2), B(3, 2.0, Bead, 0.8), B(5, 1.4, Cove, 1.4)),
        D(Frames, "Rope twist", "A bold rope between fillets, then a slope to the picture.",
            B(2, 1.6), B(10, 2.6, Flat, 0, Rope, 2.0, count: 3), B(2, 1.4), B(8, 1.4, Slope, 1.4)),
        D(Frames, "Acanthus", "Baroque: acanthus leaves on a big ovolo, pearls outside, bead and reel at the sight edge.",
            B(3, 1.6, Bead, 0.8, Pearls, 0.5), B(14, 3.2, RoundOver, 1.6, Acanthus, 1.2), B(1.5, 1.6), B(8, 1.6, Cove, 0.8),
            B(4, 1.2, Bead, 0.6, BeadAndReel, 0.5), B(3, 0.8, Slope, 0.8)),
        D(Frames, "Shaker", "Plain and flat with a rounded outside edge and a small bead round the picture.",
            B(3, 2.0, Thumbnail, 0.8, flip: true), B(20, 2.0), B(2, 1.0), B(3, 1.6, Bead, 0.6), B(2, 1.0, Slope, 1.0)),
        D(Frames, "Victorian cushion", "A deep, rounded cushion with a bead round the picture.",
            B(3, 1.5), B(28, 3.0, Cushion, 2.0), B(3, 1.0, Bead, 0.6), B(2, 0.4)),
        D(Frames, "Scoop", "Modern: a rounded lip, then a long hollow sweeping down to the picture.",
            B(3, 2.5, RoundOver, 0.5, flip: true), B(25, 2.5, Cove, 2.5)),
        D(Frames, "Ogee", "A tall ogee from the outer edge down to a small bead.",
            B(3, 3.0), B(20, 3.0, Ogee, 2.4), B(3, 0.6, Bead, 0.4), B(4, 0.6)),
        D(Frames, "Art Deco", "Stepped like a skyscraper, then a zigzag band.",
            B(2, 2.4), B(9, 2.4, Steps, 1.6, count: 4), B(10, 0.8, Flat, 0, Chevron, 0.6), B(2, 0.8, Slope, 0.8)),
        D(Frames, "Gothic", "Quatrefoils between beads, and dogtooth round the picture.",
            B(3, 2.4, Bead, 0.8), B(14, 2.4, Flat, 0, Quatrefoil, 1.4), B(3, 2.4, Bead, 0.8), B(6, 1.6, Flat, 0, Dogtooth, 1.0), B(3, 0.6, Slope, 0.6)),
        D(Frames, "Celtic plait", "A three-strand plait woven over and under, between beads.",
            B(3, 2.2, Bead, 0.8), B(16, 2.2, Flat, 0, Plait, 1.6, count: 3), B(3, 2.2, Bead, 0.8), B(3, 1.2, Slope, 1.2)),
        D(Frames, "Arts and Crafts", "A wide flat with a gentle outer bevel and three reeds round the picture.",
            B(5, 2.0, Slope, 0.5, flip: true), B(20, 1.5), B(6, 1.5, Reeds, 0.8, count: 3), B(2, 0.7, Slope, 0.7)),
        D(Frames, "Federal", "Pearls, a guilloche band and fluting: the Adam style.",
            B(3, 2.0, Bead, 0.7, Pearls, 0.5), B(1.5, 1.3), B(10, 1.8, Flat, 0, Guilloche, 1.0), B(1.5, 1.3),
            B(8, 1.8, Flutes, 0.8, count: 4), B(3, 1.0, Slope, 1.0)),
        D(Frames, "Baroque scroll", "A wave scroll on a broad ovolo, shells on a cove, pearls outside.",
            B(4, 2.0, Bead, 0.8, Pearls, 0.5), B(14, 2.8, RoundOver, 0.6, WaveScroll, 1.2), B(1.5, 2.0),
            B(10, 2.0, Cove, 1.2, Shells, 0.8), B(3, 0.8, Bead, 0.4)),
        D(Frames, "Bamboo", "A bamboo cane all the way round.",
            B(12, 2.5, Flat, 0, Bamboo, 2.5), B(2, 0.6, Slope, 0.6)),
        D(Frames, "Basketweave", "A woven band between beads.",
            B(3, 2.0, Bead, 0.8), B(16, 2.0, Flat, 0, Basketweave, 1.2), B(3, 2.0, Bead, 0.8), B(3, 1.0, Slope, 1.0)),
        D(Frames, "Rococo shells", "Scallop shells on an ovolo with a hollow inside them.",
            B(3, 2.0, Bead, 0.6), B(14, 2.6, RoundOver, 0.8, Shells, 1.2), B(2, 1.6), B(8, 1.6, Cove, 1.2), B(3, 0.4, Bead, 0.4)),
        D(Frames, "Dutch ripple", "Ebony-style ripple bands, as on 17th-century Dutch frames.",
            B(4, 2.0, Flat, 0, Ripple, 0.4), B(2, 2.4), B(14, 2.4, Cushion, 1.0, Ripple, 0.5), B(2, 1.4),
            B(5, 1.4, Flat, 0, Ripple, 0.4), B(3, 1.0, Slope, 1.0)),
        D(Frames, "Pearled", "A string of pearls along each edge of a plain flat.",
            B(4, 2.0, Bead, 0.9, Pearls, 0.6), B(16, 1.6), B(4, 2.0, Bead, 0.9, Pearls, 0.6), B(3, 0.8, Slope, 0.8)),
        D(Frames, "Reeded", "Five reeds side by side between a rounded edge and a slope.",
            B(3, 2.4, RoundOver, 0.6, flip: true), B(18, 2.4, Reeds, 1.0, count: 5), B(4, 1.4, Slope, 1.4)),
        D(Frames, "Anthemion", "Bead and reel outside, a frieze of palmettes, leaf and dart on a reverse ogee.",
            B(3, 2.0, Bead, 0.8, BeadAndReel, 0.5), B(14, 2.0, Flat, 0, Palmette, 1.2), B(9, 2.6, ReverseOgee, 1.4, LeafAndDart, 0.9), B(2, 1.2)),
        D(Frames, "Romanesque", "Interlaced arches over a row of billets.",
            B(3, 2.0, Bead, 0.6), B(12, 2.0, Flat, 0, Arcade, 1.2), B(6, 2.0, Flat, 0, Billet, 1.0), B(3, 1.0, Slope, 1.0)),
        D(Frames, "Hammered", "A hand-hammered flat between rounded edges: good on metal.",
            B(3, 1.6, RoundOver, 0.6, flip: true), B(22, 1.6, Flat, 0, Hammered, 0.5), B(3, 1.6, RoundOver, 0.8)),
        D(Frames, "Country hearts", "A row of hearts between two small beads.",
            B(3, 1.8, Bead, 0.6), B(12, 1.8, Flat, 0, Hearts, 1.0), B(3, 1.8, Bead, 0.6), B(2, 0.8, Slope, 0.8)),
        D(Frames, "Starry", "A row of faceted stars between beads.",
            B(3, 1.8, Bead, 0.6), B(12, 1.8, Flat, 0, Stars, 1.0), B(3, 1.8, Bead, 0.6), B(2, 0.8, Slope, 0.8)),
        D(Frames, "Ivy", "An ivy vine winding round between fillets.",
            B(2, 1.6), B(16, 1.6, Flat, 0, IvyVine, 1.2), B(2, 1.6), B(4, 1.0, Slope, 1.0)),
        D(Frames, "Fish scale", "Overlapping scales between beads.",
            B(3, 1.8, Bead, 0.6), B(14, 1.8, Flat, 0, FishScale, 1.0), B(3, 1.8, Bead, 0.6), B(3, 0.8, Slope, 0.8)),
    ];

    public static readonly IReadOnlyList<BorderDesign> EdgeDesigns =
    [
        D(Edges, "Roman ogee", "The router bit's classic S curve, down to a small step at the edge.",
            B(2, 0.4), B(14, 2.4, Ogee, 2.0, flip: true), B(3, 2.4)),
        D(Edges, "Classical ogee", "A cove, a fillet and a round-over: the classical ogee bit.",
            B(2, 0.4), B(7, 1.4, Cove, 1.0, flip: true), B(1.5, 1.4), B(7, 2.4, RoundOver, 1.0, flip: true), B(2, 2.4)),
        D(Edges, "Cove and bead", "A bead at the edge and a cove rising to the face.",
            B(2, 0.5), B(5, 1.5, Bead, 1.0), B(9, 2.6, Cove, 2.1, flip: true), B(2, 2.6)),
        D(Edges, "Cock bead", "A bead along the edge with a small groove beside it, as on drawer fronts.",
            B(5, 2.0, Bead, 1.2), B(1.5, 0.8), B(10, 2.0)),
        D(Edges, "Chamfer", "A plain 45° bevel along the edge.",
            B(8, 2.0, Slope, 2.0, flip: true), B(6, 2.0)),
        D(Edges, "Round-over", "A quarter-round rounding the edge over.",
            B(10, 2.4, RoundOver, 2.4, flip: true), B(5, 2.4)),
        D(Edges, "Thumbnail", "A flat top rounding over tightly at the edge, as on a table top.",
            B(16, 2.0, Thumbnail, 1.8, flip: true)),
        D(Edges, "Bullnose", "A full half-round edge.",
            B(12, 2.6, Bead, 2.6)),
        D(Edges, "Reeded edge", "Five reeds along the edge.",
            B(15, 2.0, Reeds, 1.2, count: 5)),
        D(Edges, "Fluted", "Five flutes between fillets, as on a pilaster.",
            B(2, 2.0), B(16, 2.0, Flutes, 1.2, count: 5), B(2, 2.0)),
        D(Edges, "Dentil crown", "A crown moulding: a row of dentils over a cove and a small bead.",
            B(3, 2.6), B(10, 2.6, Flat, 0, Dentil, 1.4), B(2, 1.8), B(8, 1.8, Cove, 1.6), B(3, 0.8, Bead, 0.6)),
        D(Edges, "Cornice", "A round-over, dentils, egg and dart and a cove: a classical cornice.",
            B(4, 3.0, RoundOver, 0.8), B(1.5, 2.2), B(8, 2.2, Flat, 0, Dentil, 1.2), B(1.5, 1.2),
            B(8, 2.0, RoundOver, 1.0, EggAndDart, 0.8), B(6, 1.0, Cove, 1.0)),
        D(Edges, "Rope edge", "A rope along the edge between fillets.",
            B(2, 1.0), B(10, 2.6, Flat, 0, Rope, 2.0, count: 3), B(2, 1.0)),
        D(Edges, "Gadrooned", "Rounded lobes along the edge, as on a table top or a tray.",
            B(2, 0.6), B(14, 2.8, Flat, 0, Gadroon, 2.2), B(2, 0.8)),
        D(Edges, "Twisted gadroons", "Gadroons swept into S curves.",
            B(2, 0.6), B(14, 2.8, Flat, 0, TwistedGadroon, 2.2), B(2, 0.8)),
        D(Edges, "Egg and dart crown", "Egg and dart on an ovolo over a cove.",
            B(2, 2.6), B(10, 2.6, RoundOver, 1.0, EggAndDart, 1.0), B(2, 1.6), B(6, 1.6, Cove, 1.2)),
        D(Edges, "Meander frieze", "A running fret between fillets.",
            B(2, 2.0), B(12, 2.0, Flat, 0, Fret, 1.2), B(2, 2.0)),
        D(Edges, "Acanthus frieze", "Acanthus leaves on a cove rising from the edge.",
            B(2, 0.8), B(16, 2.8, Cove, 1.6, Acanthus, 1.2, flip: true), B(2, 2.8)),
        D(Edges, "Bead and reel", "An astragal of beads and reels between fillets.",
            B(2, 1.4), B(6, 2.0, Bead, 1.0, BeadAndReel, 0.8), B(2, 1.4)),
        D(Edges, "Billet", "Romanesque billets between fillets.",
            B(2, 2.0), B(10, 2.0, Flat, 0, Billet, 1.2), B(2, 2.0)),
        D(Edges, "Dogtooth", "Early English dogtooth in a hollow.",
            B(2, 2.0), B(10, 2.0, Flat, 0, Dogtooth, 1.4), B(2, 2.0)),
        D(Edges, "Laurel garland", "Bay leaves wrapped round a torus.",
            B(2, 1.6), B(12, 2.6, Bead, 1.2, Laurel, 0.9), B(2, 1.6)),
        D(Edges, "Wave frieze", "A Vitruvian scroll between fillets.",
            B(2, 2.0), B(14, 2.0, Flat, 0, Vitruvian, 1.2), B(2, 2.0)),
        D(Edges, "Ribbon and reed", "A ribbon wound round a rod.",
            B(2, 1.0), B(12, 2.4, Flat, 0, RibbonAndStick, 1.6), B(2, 1.0)),
        D(Edges, "Bamboo edge", "A bamboo cane along the edge.",
            B(10, 2.4, Flat, 0, Bamboo, 2.0)),
        D(Edges, "Barley twist", "A deep two-strand twist, like a turned leg laid flat.",
            B(12, 2.6, Flat, 0, BarleyTwist, 2.2)),
        D(Edges, "Shelf front", "A plain fillet, a bead and a cove: a simple shelf or cupboard edge.",
            B(2, 1.0), B(4, 1.8, Bead, 0.8), B(8, 2.4, Cove, 1.4, flip: true), B(3, 2.4)),
        D(Edges, "Linen chest", "A flat with a pair of reeds and a chamfered edge.",
            B(4, 2.0, Slope, 1.0, flip: true), B(3, 2.0), B(6, 2.0, Reeds, 0.8, count: 2), B(6, 2.0)),
    ];

    /// <summary>Each ornament alone on a flat band between two fillets.</summary>
    public static readonly IReadOnlyList<BorderDesign> BandDesigns = [.. Ornaments.All.Where(o => o.Kind != None).Select(Band)];

    static BorderDesign Band(OrnamentInfo o)
    {
        // Round things (rods, ropes, beads) want carving all the way down so they stand out round; textures stay shallow.
        double carve = o.Kind switch
        {
            Rope or BarleyTwist or Bamboo or RibbonAndStick or Pearls or BeadAndReel or Gadroon or TwistedGadroon => 2.0,
            Ripple or Hammered => 0.5,
            _ => 1.2,
        };
        return D(Bands, o.Name, o.About, B(1.5, 2.0), B(14, 2.0, Flat, 0, o.Kind, carve, count: o.Kind == Plait ? 3 : 3), B(1.5, 2.0));
    }

    /// <summary>Every built-in design, frames first.</summary>
    public static readonly IReadOnlyList<BorderDesign> All = [.. FrameDesigns, .. EdgeDesigns, .. BandDesigns];

    /// <summary>The one the maker starts with.</summary>
    public static BorderDesign Default => FrameDesigns[0];

    /// <summary>What a design is usually made as: frames go round, edges and bands are strips.</summary>
    public static BorderLayout UsualLayout(BorderDesign d) => d.Category == Frames ? BorderLayout.Frame : BorderLayout.Strip;
}
