using System.Globalization;
using System.Text;

namespace PhotonCanvas.Addon.Automata;

/// <summary>A note to play: when (beats from the start) and which (MIDI number: 60 = middle C, C4).</summary>
public readonly record struct NoteEvent(double Beat, int Pitch);

/// <summary>Note names and numbers.</summary>
public static class Notes
{
    static readonly string[] Sharp = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>"C4", "F#5" for a MIDI number (60 = C4).</summary>
    public static string Name(int pitch) => $"{Sharp[((pitch % 12) + 12) % 12]}{pitch / 12 - 1}";

    /// <summary>A note name ("C4", "F#5", "Bb3", "c#4") as a MIDI number, or null.</summary>
    public static int? Parse(string s)
    {
        s = s.Trim();
        if (s.Length < 2) return null;
        int step = char.ToUpperInvariant(s[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => -1 };
        if (step < 0) return null;
        int i = 1;
        while (i < s.Length && (s[i] == '#' || s[i] == 'b' || s[i] == '♯' || s[i] == '♭'))
        {
            step += s[i] is '#' or '♯' ? 1 : -1;
            i++;
        }
        if (!int.TryParse(s[i..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int octave) || octave < -1 || octave > 9) return null;
        int p = (octave + 1) * 12 + step;
        return p is >= 0 and <= 127 ? p : null;
    }
}

/// <summary>
/// A tune written as text: notes separated by spaces, each a name and octave (<c>C4</c>, <c>F#5</c>, <c>Bb4</c>),
/// with <c>:2</c> for two beats (<c>:0.5</c> half a beat; one beat when not given); notes joined with <c>+</c>
/// play together (<c>C4+E4+G4</c>); <c>-</c> or <c>r</c> is a rest; <c>|</c> bar lines and anything after
/// <c>#</c> at the start of a line are ignored.
/// </summary>
public static class Melody
{
    public sealed record Result(List<NoteEvent> Notes, double Beats, List<string> Problems);

    public static Result Parse(string text)
    {
        var notes = new List<NoteEvent>();
        var problems = new List<string>();
        double beat = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            foreach (var tok in line.Split([' ', '\t', ',', '|'], StringSplitOptions.RemoveEmptyEntries))
            {
                string body = tok;
                double len = 1;
                int colon = tok.IndexOf(':');
                if (colon >= 0)
                {
                    body = tok[..colon];
                    var lenText = tok[(colon + 1)..];
                    if (lenText.Contains('/'))
                    {
                        var ab = lenText.Split('/');
                        if (ab.Length == 2 && double.TryParse(ab[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && double.TryParse(ab[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b > 0) len = a / b;
                        else { problems.Add($"\"{tok}\": the length after : isn't a number."); continue; }
                    }
                    else if (!double.TryParse(lenText, NumberStyles.Float, CultureInfo.InvariantCulture, out len) || len <= 0)
                    {
                        problems.Add($"\"{tok}\": the length after : isn't a number.");
                        continue;
                    }
                }
                if (body is "-" or "r" or "R" or "rest")
                {
                    beat += len;
                    continue;
                }
                bool ok = true;
                var chord = new List<int>();
                foreach (var part in body.Split('+', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Notes.Parse(part) is { } p) chord.Add(p);
                    else { ok = false; problems.Add($"\"{part}\" isn't a note (write a name and octave, like C4, F#5 or Bb3)."); }
                }
                if (ok) notes.AddRange(chord.Select(p => new NoteEvent(beat, p)));
                beat += len;
            }
        }
        return new Result(notes, beat, problems);
    }

    /// <summary>Notes back to text, a bar of <paramref name="perBar"/> beats on each line.</summary>
    public static string Write(IReadOnlyList<NoteEvent> notes, int perBar = 4)
    {
        var sb = new StringBuilder();
        var groups = notes.GroupBy(n => Math.Round(n.Beat, 4)).OrderBy(g => g.Key).ToList();
        double at = 0;
        int bar = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            double when = groups[i].Key;
            if (when > at + 1e-6)
            {
                Rest(sb, when - at);
                at = when;
            }
            double next = i + 1 < groups.Count ? groups[i + 1].Key : when + 1;
            double len = next - when;
            sb.Append(string.Join("+", groups[i].Select(n => n.Pitch).Distinct().Order().Select(Notes.Name)));
            if (Math.Abs(len - 1) > 1e-6) sb.Append(':').Append(Len(len));
            sb.Append(' ');
            at = next;
            int b = (int)Math.Floor(at / perBar + 1e-9);
            if (b > bar) { sb.Append('\n'); bar = b; }
        }
        return sb.ToString().TrimEnd();
    }

    static void Rest(StringBuilder sb, double len)
    {
        sb.Append('-');
        if (Math.Abs(len - 1) > 1e-6) sb.Append(':').Append(Len(len));
        sb.Append(' ');
    }

    static string Len(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Tunes to start from (all long out of copyright).</summary>
    public static readonly IReadOnlyList<(string Name, string Text)> Tunes =
    [
        ("Twinkle, twinkle, little star", """
            C5 C5 G5 G5 | A5 A5 G5:2 | F5 F5 E5 E5 | D5 D5 C5:2
            G5 G5 F5 F5 | E5 E5 D5:2 | G5 G5 F5 F5 | E5 E5 D5:2
            C5 C5 G5 G5 | A5 A5 G5:2 | F5 F5 E5 E5 | D5 D5 C5+C4:2
            """),
        ("Ode to Joy (Beethoven)", """
            E5 E5 F5 G5 | G5 F5 E5 D5 | C5 C5 D5 E5 | E5:1.5 D5:0.5 D5:2
            E5 E5 F5 G5 | G5 F5 E5 D5 | C5 C5 D5 E5 | D5:1.5 C5:0.5 C5+C4:2
            """),
        ("Frère Jacques", """
            C5 D5 E5 C5 | C5 D5 E5 C5 | E5 F5 G5:2 | E5 F5 G5:2
            G5:0.5 A5:0.5 G5:0.5 F5:0.5 E5 C5 | G5:0.5 A5:0.5 G5:0.5 F5:0.5 E5 C5
            C5 G4 C5:2 | C5 G4 C5+C4:2
            """),
        ("Greensleeves (opening)", """
            A4 | C5:2 D5 | E5:1.5 F5:0.5 E5 | D5:2 B4 | G4:1.5 A4:0.5 B4
            C5:2 A4 | A4:1.5 G#4:0.5 A4 | B4:2 G#4 | E4:2 A4
            C5:2 D5 | E5:1.5 F5:0.5 E5 | D5:2 B4 | G4:1.5 A4:0.5 B4
            C5:1.5 B4:0.5 A4 | G#4:1.5 F#4:0.5 G#4 | A4+A3:3
            """),
    ];

    /// <summary>A test strip: every note the box has, lowest first, a beat apart (so you can hear each one and check the holes line up).</summary>
    public static string EveryNote(IReadOnlyList<int> pitches) => string.Join(" ", pitches.Select(Notes.Name));
}

/// <summary>
/// Reads a standard MIDI file into notes, timed in beats (quarter notes). Every track and channel is read
/// except channel 10 (drums); notes are put on the nearest step of <paramref name="grid"/> beats.
/// </summary>
public static class MidiFile
{
    public static List<NoteEvent> Read(byte[] data, double grid = 0.25)
    {
        int pos = 0;
        string Tag()
        {
            if (pos + 4 > data.Length) throw new InvalidDataException("The file ends too soon.");
            var s = Encoding.ASCII.GetString(data, pos, 4);
            pos += 4;
            return s;
        }
        int U32() { if (pos + 4 > data.Length) throw new InvalidDataException("The file ends too soon."); int v = data[pos] << 24 | data[pos + 1] << 16 | data[pos + 2] << 8 | data[pos + 3]; pos += 4; return v; }
        int U16() { if (pos + 2 > data.Length) throw new InvalidDataException("The file ends too soon."); int v = data[pos] << 8 | data[pos + 1]; pos += 2; return v; }

        if (Tag() != "MThd") throw new InvalidDataException("That isn't a MIDI file (it should start with MThd).");
        int headerLen = U32();
        int headerEnd = pos + headerLen;
        U16(); // format
        int tracks = U16();
        int division = U16();
        pos = headerEnd;
        // Ticks per beat; a file timed in frames per second (rare) is taken as 120 beats a minute.
        double perBeat = (division & 0x8000) == 0 ? Math.Max(1, division) : Math.Max(1, (256 - (division >> 8)) * (division & 0xFF) / 2.0);

        var notes = new List<NoteEvent>();
        for (int tr = 0; tr < tracks && pos + 8 <= data.Length; tr++)
        {
            string tag = Tag();
            int len = U32();
            int end = Math.Min(data.Length, pos + Math.Max(0, len));
            if (tag != "MTrk") { pos = end; continue; }
            long tick = 0;
            int status = 0;
            while (pos < end)
            {
                tick += Vlq(data, ref pos, end);
                if (pos >= end) break;
                int b = data[pos];
                if (b >= 0x80) { status = b; pos++; }
                else if (status == 0) throw new InvalidDataException("A MIDI event has no status byte.");
                int kind = status & 0xF0, channel = status & 0x0F;
                if (status == 0xFF)
                {
                    if (pos >= end) break;
                    pos++; // meta type
                    int l = (int)Vlq(data, ref pos, end);
                    pos += l;
                    status = 0;
                }
                else if (status is 0xF0 or 0xF7)
                {
                    int l = (int)Vlq(data, ref pos, end);
                    pos += l;
                    status = 0;
                }
                else if (kind is 0xC0 or 0xD0) pos += 1;
                else
                {
                    if (pos + 2 > end) break;
                    int a = data[pos], v = data[pos + 1];
                    pos += 2;
                    if (kind == 0x90 && v > 0 && channel != 9)
                    {
                        double beat = tick / perBeat;
                        if (grid > 0) beat = Math.Round(beat / grid) * grid;
                        notes.Add(new NoteEvent(beat, a));
                    }
                }
            }
            pos = end;
        }
        if (notes.Count == 0) throw new InvalidDataException("There are no notes in that MIDI file (drums on channel 10 are left out).");
        // Start at the first note.
        double first = notes.Min(n => n.Beat);
        return [.. notes.Select(n => n with { Beat = n.Beat - first }).Distinct().OrderBy(n => n.Beat).ThenBy(n => n.Pitch)];
    }

    static long Vlq(byte[] d, ref int pos, int end)
    {
        long v = 0;
        for (int i = 0; i < 4 && pos < end; i++)
        {
            byte b = d[pos++];
            v = (v << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return v;
    }
}
