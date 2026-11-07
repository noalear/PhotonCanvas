namespace PhotonCanvas.App;

/// <summary>
/// Where ribbon buttons went when the ribbon was reorganised (November 2026): importing moved to a new
/// Import tab (Picture keeps what's done to a picture), Designer became Mini-Designer, Camera became Placement, Laser became Machine with its materials
/// and tests moved to Materials &amp; Tests and its tests into the Tests window. An add-on built for the
/// old places, and the tab that was open when the program last closed, land in the new ones.
/// </summary>
public static class RibbonPlaces
{
    /// <summary>The "tab" of the tests: not on the ribbon, they're picked in the Tests window (Materials &amp; Tests → Tests).</summary>
    public const string Tests = "Tests";

    /// <summary>A tab's name now.</summary>
    public static string Tab(string tab) => tab switch
    {
        "Laser" => "Machine",
        "Designer" => "Mini-Designer",
        "Camera" => "Placement",
        _ => tab,
    };

    /// <summary>Where a button for (<paramref name="tab"/>, <paramref name="group"/>) goes now.</summary>
    public static (string Tab, string Group) Map(string tab, string group) => (tab, group) switch
    {
        ("Laser", "Tests") => (Tests, "More tests"),
        ("Laser", "Colour on metal") => (Tests, "Colour on metal"),
        ("Laser", "Materials") => ("Materials & Tests", "Library"),
        ("Laser", "Make") => ("Placement", "From the photo"),
        ("Laser", "Machine") => ("Machine", "Settings"),
        ("Picture", "Add") => ("Import", "Files"),
        ("Picture", "Clean up") => ("Picture", "Edit"),
        ("Picture", "Turn it into" or "Relief" or "Wood") => ("Picture", "Effects"),
        ("Picture", "Shape") => ("Picture", "Mask"),
        ("Designer", "Minis & games") => ("Mini-Designer", "Accessories"),
        ("Designer", "Toys") => ("Mini-Designer", "Mechanical"),
        ("Camera", "Place") => ("Placement", "Onto pieces"),
        ("Camera", "Set up") => ("Machine", "Calibrate"),
        ("Home", "Bring in") => ("Import", "3D & maps"),
        ("Home", "Group") => ("Arrange", "Group"),
        ("Home", "Clipboard") => ("Home", "Select"),
        _ => (Tab(tab), group),
    };
}
