using System.Buffers.Binary;
using System.Text;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Import;
using PhotonCanvas.Doc.Materials;
using Xunit;

namespace PhotonCanvas.Tests;

public class XToolPresetsTests
{
    static byte[] Varint(ulong v)
    {
        var o = new List<byte>();
        while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; }
        o.Add((byte)v);
        return [.. o];
    }

    [Fact]
    public void SnappyLiteralsAndCopies()
    {
        // 12 bytes: "abc" as a literal, then 9 copied from 3 back.
        byte[] packed = [12, 0x08, (byte)'a', (byte)'b', (byte)'c', 0x15, 3];
        Assert.Equal("abcabcabcabc", Encoding.ASCII.GetString(ChromiumIndexedDb.Snappy(packed)));
    }

    /// <summary>An IndexedDB value as Chromium stores it: its version, Blink's envelope (with the trailer marker), then V8's.</summary>
    static byte[] Stored(params byte[] v8) => [1, 0xFF, 0x15, 0xFE, .. new byte[12], 0xFF, 0x0F, .. v8];

    static byte[] Str(string s) => [(byte)'"', .. Varint((ulong)s.Length), .. Encoding.Latin1.GetBytes(s)];

    [Fact]
    public void V8ValuesComeBackAsPlainData()
    {
        // {name: "Bas", n: 3, ok: true, list: [1, "x"], w: "ä€"}
        byte[] two = [(byte)'c', 4, .. Encoding.Unicode.GetBytes("ä€")];
        var v = (Dictionary<string, object?>)ChromiumIndexedDb.Value(Stored(
            [(byte)'o', .. Str("name"), .. Str("Bas"), .. Str("n"), (byte)'I', 6, .. Str("ok"), (byte)'T',
             .. Str("list"), (byte)'A', 2, (byte)'I', 2, .. Str("x"), (byte)'$', 0, 2, .. Str("w"), .. two, (byte)'{', 5]))!;
        Assert.Equal("Bas", v["name"]);
        Assert.Equal(3.0, v["n"]);
        Assert.Equal(true, v["ok"]);
        Assert.Equal([1.0, "x"], (List<object?>)v["list"]!);
        Assert.Equal("ä€", v["w"]);
    }

    /// <summary>A LevelDB table with one data block (uncompressed) holding these entries.</summary>
    static byte[] Table(IEnumerable<(byte[] Key, ulong Seq, byte[] Value)> entries)
    {
        var blk = new List<byte>();
        foreach (var (key, seq, value) in entries.OrderBy(e => Convert.ToHexString(e.Key)))
        {
            var ik = new byte[key.Length + 8];
            key.CopyTo(ik, 0);
            BinaryPrimitives.WriteUInt64LittleEndian(ik.AsSpan(key.Length), seq << 8 | 1);
            blk.AddRange([.. Varint(0), .. Varint((ulong)ik.Length), .. Varint((ulong)value.Length), .. ik, .. value]);
        }
        blk.AddRange(BitConverter.GetBytes(0u)); // one restart, at 0
        blk.AddRange(BitConverter.GetBytes(1u));
        var file = new List<byte>(blk) { 0, 0, 0, 0, 0 }; // trailer: not compressed, checksum
        var index = new List<byte>();
        var handle = Varint(0).Concat(Varint((ulong)blk.Count)).ToArray();
        index.AddRange([.. Varint(0), .. Varint(1), .. Varint((ulong)handle.Length), (byte)'z', .. handle]);
        index.AddRange(BitConverter.GetBytes(0u));
        index.AddRange(BitConverter.GetBytes(1u));
        int indexAt = file.Count;
        file.AddRange(index);
        file.AddRange([0, 0, 0, 0, 0]);
        var footer = new List<byte>([.. Varint(0), .. Varint(0), .. Varint((ulong)indexAt), .. Varint((ulong)index.Count)]);
        while (footer.Count < 40) footer.Add(0);
        footer.AddRange(BitConverter.GetBytes(0xdb4775248b80fb57UL));
        file.AddRange(footer);
        return [.. file];
    }

    /// <summary>A LevelDB log with one write batch.</summary>
    static byte[] Log(ulong seq, IEnumerable<(byte[] Key, byte[] Value)> puts)
    {
        var batch = new List<byte>(BitConverter.GetBytes(seq));
        var list = puts.ToList();
        batch.AddRange(BitConverter.GetBytes((uint)list.Count));
        foreach (var (k, v) in list) batch.AddRange([1, .. Varint((ulong)k.Length), .. k, .. Varint((ulong)v.Length), .. v]);
        return [0, 0, 0, 0, .. BitConverter.GetBytes((ushort)batch.Count), 1, .. batch];
    }

    /// <summary>A record's key: database, store, index (1 = the records), then the record's own key.</summary>
    static byte[] Key(byte db, byte store, string id) => [0, db, store, 1, .. Encoding.ASCII.GetBytes(id)];

    [Fact]
    public void ReadsTablesAndTheLogNewestFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pc-idb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "000005.ldb"), Table([
                (Key(2, 1, "a"), 5, Stored([.. Str("old")])),
                (Key(2, 1, "b"), 6, Stored([.. Str("kept")])),
                (Key(2, 2, "c"), 7, Stored([.. Str("other store")])),
            ]));
            // The log is newer: it replaces "a".
            File.WriteAllBytes(Path.Combine(dir, "000006.log"), Log(10, [(Key(2, 1, "a"), Stored([.. Str("new")]))]));
            var stores = ChromiumIndexedDb.Read(dir);
            Assert.Equal(["kept", "new"], stores[(2, 1)].Cast<string>().Order());
            Assert.Equal(["other store"], stores[(2, 2)].Cast<string>());
        }
        finally { Directory.Delete(dir, true); }
    }

    static Dictionary<string, object?> D(params (string K, object? V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);
    static Dictionary<string, object?> C(string code) => D(("code", code));

    [Fact]
    public void XToolSettingsBecomeRecipes()
    {
        var materials = new List<Dictionary<string, object?>>
        {
            D(("id", 1.0), ("name", D(("de", "3mm Lindensperrholz"), ("en", "1/8\"(3mm) Basswood Plywood"))), ("thickness", 3.0), ("categoryCode", "Wood"), ("secondCategoryCode", "PLYWOOD")),
            D(("id", 2.0), ("name", D(("en", "Brown to Gold PU Leather"))), ("thickness", 0.5), ("categoryCode", "Leather")),
        };
        var p = new Dictionary<string, object?>[]
        {
            D(("processingMode", C("LASER_PLANE")), ("processingType", C("VECTOR_CUTTING")), ("laserSource", C("blue")), ("power", 70.0), ("speed", 40.0), ("repeat", 4.0)),
            D(("processingMode", C("LASER_PLANE")), ("processingType", C("FILL_VECTOR_ENGRAVING")), ("laserSource", C("blue")), ("power", 90.0), ("speed", 1250.0), ("density", D(("code", "100")))),
            D(("processingMode", C("LASER_PLANE")), ("processingType", C("BITMAP_ENGRAVING")), ("laserSource", C("blue")), ("power", 90.0), ("dpi", 180.0), ("dotTime", 200.0), ("bitmapMode", C("Jarvis"))),
            D(("processingMode", C("LASER_PLANE")), ("processingType", C("VECTOR_ENGRAVING")), ("laserSource", C("red")), ("power", 80.0), ("speed", 3600.0), ("pulseWidth", 500.0), ("mopaFrequency", 60.0)),
            D(("processingMode", C("CONVEYOR_EXTENSION")), ("processingType", C("FILL_VECTOR_ENGRAVING")), ("laserSource", C("blue")), ("power", 90.0), ("speed", 1250.0)),
        };
        var catalogue = new List<Dictionary<string, object?>>
        {
            D(("materialId", 1.0), ("deviceCode", "GS004-CLASS-4"), ("machineParas", p.Cast<object?>().ToList())),
            D(("materialId", 2.0), ("deviceCode", "MLM"), ("machineParas", new List<object?> { p[0] })), // another machine
        };
        // The user's own, made in xTool Studio: a local material with a full setting.
        var local = new List<Dictionary<string, object?>> { D(("id", "local_1"), ("name", "Brass Dark Mark"), ("thickness", 1.0), ("precautionCodes", D(("0", "HIGH_REFLECT")))) };
        var localParams = new List<Dictionary<string, object?>>
        {
            D(("materialId", "local_1"), ("deviceCode", "GS004-CLASS-4"), ("processingTypeCode", "FILL_VECTOR_ENGRAVING"), ("processingModeCode", "LASER_PLANE"), ("laserSourceCode", "red"), ("name", "Brass Dark Mark"),
              ("rawData", D(("power", 90.0), ("speed", 250.0), ("repeat", 1.0), ("density", 300.0), ("pulseWidth", 500.0), ("mopaFrequency", 30.0), ("scanAngle", 45.0), ("crossAngle", true), ("defocus", true), ("defocus_distance", 2.0)))),
        };
        var r = XToolPresets.From(materials, catalogue, [], local, localParams, "GS004-CLASS-4");
        Assert.Equal(1, r.LeftOut); // the conveyor one
        var bass = r.Materials.Single(m => m.Name.Contains("Basswood"));
        Assert.Equal((3.0, "Plywood & MDF", false), (bass.ThicknessMm!.Value, bass.Category, bass.Yours));
        Assert.Equal(4, bass.Settings.Count);
        var cut = bass.Settings[0].Settings;
        Assert.Equal((LayerMode.Cut, 70.0, 40.0, 4), (cut.Mode, cut.Laser.Power, cut.Laser.Speed, cut.Laser.Passes));
        Assert.Equal(100, bass.Settings[1].Settings.LinesPerCm);
        var pic = bass.Settings[2].Settings;
        Assert.Equal((LayerMode.Image, 180.0, 200), (pic.Mode, pic.Dpi, pic.DotUs));
        var mopa = bass.Settings[3].Settings;
        Assert.Equal((LightSource.Red, 500, 60), (mopa.Laser.Light, mopa.Laser.PulseWidthNs, mopa.Laser.FrequencyKhz));

        var mine = r.Materials.Single(m => m.Yours);
        Assert.True(mine.Reflective);
        var mark = mine.Settings.Single().Settings;
        Assert.Equal((45.0, true, -2.0, 300.0), (mark.FillAngleDeg, mark.Crosshatch, mark.FocusOffsetMm, mark.LinesPerCm));

        // Into the library: xTool's marked as theirs; a second import updates rather than repeats.
        var lib = new MaterialLibrary(Path.Combine(Path.GetTempPath(), $"pc-xt-{Guid.NewGuid():N}"));
        var (mats, added, updated) = XToolPresets.Into(lib, r, "GS004-CLASS-4");
        Assert.Equal((2, 5, 0), (mats, added, updated));
        var recipe = lib.Materials.Single(m => m.Name.Contains("Basswood")).Recipes.First();
        Assert.Equal(("xTool", true), (recipe.TestedBy, recipe.Tested));
        Assert.EndsWith("(xTool)", recipe.Title);
        Assert.Equal("", lib.Materials.Single(m => m.Name == "Brass Dark Mark").Recipes.Single().TestedBy);
        Assert.Equal((0, 0, 0), XToolPresets.Into(lib, r, "GS004-CLASS-4"));
    }
}
