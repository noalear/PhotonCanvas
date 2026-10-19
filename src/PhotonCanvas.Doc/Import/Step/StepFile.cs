using System.Globalization;
using System.Text;

namespace PhotonCanvas.Doc.Import.Step;

/// <summary>A reference to another entity in the file (<c>#123</c>).</summary>
public readonly record struct StepRef(int Id);

/// <summary>An enumeration value (<c>.T.</c>, <c>.UNSPECIFIED.</c>).</summary>
public readonly record struct StepEnum(string Name);

/// <summary>A value written with its type (<c>LENGTH_MEASURE(25.4)</c>).</summary>
public sealed record StepTyped(string Type, IReadOnlyList<object?> Args);

/// <summary>
/// One entity of a STEP file: a type and its arguments, or (a "complex" entity) several
/// types with their own arguments each, like a rational B-spline or a unit.
/// Arguments are double, string, <see cref="StepRef"/>, <see cref="StepEnum"/>, <see cref="StepTyped"/>,
/// a list of those, or null (<c>$</c> or <c>*</c>).
/// </summary>
public sealed class StepEntity(int id, string type, IReadOnlyList<object?> args, IReadOnlyDictionary<string, IReadOnlyList<object?>>? parts = null)
{
    public int Id { get; } = id;
    /// <summary>The type, or "" for a complex entity.</summary>
    public string Type { get; } = type;
    public IReadOnlyList<object?> Args { get; } = args;
    public IReadOnlyDictionary<string, IReadOnlyList<object?>>? Parts { get; } = parts;

    /// <summary>Is it this type (or, complex, does it have this part)?</summary>
    public bool Is(string type) => Type == type || Parts?.ContainsKey(type) == true;

    /// <summary>The arguments of one of its types.</summary>
    public IReadOnlyList<object?>? Of(string type) => Type == type ? Args : Parts?.GetValueOrDefault(type);

    public override string ToString() => $"#{Id}={(Type.Length > 0 ? Type : string.Join("+", Parts!.Keys))}";
}

/// <summary>
/// Reads the data section of a STEP file (ISO 10303-21, "Part 21"): every entity by its number.
/// Nothing is interpreted here; see <see cref="StepMesh"/>.
/// </summary>
public sealed class StepFile
{
    readonly Dictionary<int, StepEntity> _entities;
    public string Schema { get; }

    StepFile(Dictionary<int, StepEntity> entities, string schema) { _entities = entities; Schema = schema; }

    public IEnumerable<StepEntity> All => _entities.Values;
    public int Count => _entities.Count;

    public StepEntity? Get(int id) => _entities.GetValueOrDefault(id);
    public StepEntity? Get(object? arg) => arg is StepRef r ? Get(r.Id) : null;

    public static StepFile Load(string path) => Parse(File.ReadAllText(path, Encoding.Latin1));

    /// <exception cref="FormatException">Not a STEP file.</exception>
    public static StepFile Parse(string text)
    {
        if (!text.AsSpan(0, Math.Min(text.Length, 200)).TrimStart().StartsWith("ISO-10303-21", StringComparison.Ordinal))
            throw new FormatException("This isn't a STEP file (no ISO-10303-21 header).");
        string schema = "";
        int fs = text.IndexOf("FILE_SCHEMA", StringComparison.Ordinal);
        if (fs >= 0)
        {
            int q1 = text.IndexOf('\'', fs), q2 = q1 >= 0 ? text.IndexOf('\'', q1 + 1) : -1;
            if (q2 > q1) schema = text[(q1 + 1)..q2];
        }
        int data = text.IndexOf("DATA;", StringComparison.Ordinal);
        if (data < 0) throw new FormatException("This STEP file has no DATA section.");
        var r = new Reader(text, data + 5);
        var entities = new Dictionary<int, StepEntity>();
        while (r.NextEntity() is { } e) entities[e.Id] = e;
        return new StepFile(entities, schema);
    }

    /// <summary>A hand-written scanner: fast enough for files of hundreds of megabytes.</summary>
    sealed class Reader(string s, int at)
    {
        int _i = at;

        void Skip()
        {
            while (_i < s.Length)
            {
                char c = s[_i];
                if (c is ' ' or '\r' or '\n' or '\t') { _i++; continue; }
                if (c == '/' && _i + 1 < s.Length && s[_i + 1] == '*')
                {
                    int end = s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    _i = end < 0 ? s.Length : end + 2;
                    continue;
                }
                break;
            }
        }

        public StepEntity? NextEntity()
        {
            while (true)
            {
                Skip();
                if (_i >= s.Length) return null;
                if (s[_i] == '#')
                {
                    _i++;
                    int id = Int();
                    Skip();
                    if (_i >= s.Length || s[_i] != '=') { SkipStatement(); continue; }
                    _i++;
                    Skip();
                    try
                    {
                        StepEntity e;
                        if (s[_i] == '(')
                        {
                            // Complex: (A(...) B(...) ...)
                            _i++;
                            var parts = new Dictionary<string, IReadOnlyList<object?>>();
                            while (true)
                            {
                                Skip();
                                if (s[_i] == ')') { _i++; break; }
                                var name = Name();
                                Skip();
                                parts[name] = List();
                            }
                            e = new StepEntity(id, "", [], parts);
                        }
                        else
                        {
                            var name = Name();
                            Skip();
                            e = new StepEntity(id, name, List());
                        }
                        SkipStatement();
                        return e;
                    }
                    catch (IndexOutOfRangeException) { return null; }
                }
                // ENDSEC; or anything else: past the next ';'.
                if (string.CompareOrdinal(s, _i, "ENDSEC", 0, 6) == 0) return null;
                SkipStatement();
            }
        }

        void SkipStatement()
        {
            // To the ';' that ends this statement (strings may hold ';').
            while (_i < s.Length && s[_i] != ';')
            {
                if (s[_i] == '\'') String(); else _i++;
            }
            _i++;
        }

        int Int()
        {
            int v = 0;
            while (_i < s.Length && char.IsAsciiDigit(s[_i])) v = v * 10 + (s[_i++] - '0');
            return v;
        }

        string Name()
        {
            int start = _i;
            while (_i < s.Length && (char.IsAsciiLetterOrDigit(s[_i]) || s[_i] == '_' || s[_i] == '-')) _i++;
            return s[start.._i].ToUpperInvariant();
        }

        IReadOnlyList<object?> List()
        {
            if (s[_i] != '(') throw new FormatException($"Expected '(' at {_i}.");
            _i++;
            var items = new List<object?>();
            Skip();
            if (s[_i] == ')') { _i++; return items; }
            while (true)
            {
                Skip();
                items.Add(Value());
                Skip();
                if (s[_i] == ',') { _i++; continue; }
                if (s[_i] == ')') { _i++; return items; }
                throw new FormatException($"Unexpected '{s[_i]}' at {_i}.");
            }
        }

        object? Value()
        {
            char c = s[_i];
            switch (c)
            {
                case '#': _i++; return new StepRef(Int());
                case '$': _i++; return null;
                case '*': _i++; return null;
                case '\'': return String();
                case '(': return List();
                case '.':
                    {
                        int end = s.IndexOf('.', _i + 1);
                        var name = s[(_i + 1)..end];
                        _i = end + 1;
                        return new StepEnum(name);
                    }
                case '"':
                    {
                        // Binary: "0ABC…" (not needed for geometry).
                        int end = s.IndexOf('"', _i + 1);
                        _i = end + 1;
                        return null;
                    }
            }
            if (c is '-' or '+' || char.IsAsciiDigit(c)) return Number();
            if (char.IsAsciiLetter(c))
            {
                var name = Name();
                Skip();
                return new StepTyped(name, List());
            }
            throw new FormatException($"Unexpected '{c}' at {_i}.");
        }

        double Number()
        {
            int start = _i;
            if (s[_i] is '-' or '+') _i++;
            while (_i < s.Length && (char.IsAsciiDigit(s[_i]) || s[_i] is '.' or 'E' or 'e' || ((s[_i] is '-' or '+') && s[_i - 1] is 'E' or 'e'))) _i++;
            var span = s.AsSpan(start, _i - start);
            // "1." and "1.E-05" are fine for double.Parse; ".5" too.
            return double.Parse(span, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        string String()
        {
            var sb = new StringBuilder();
            _i++;
            while (_i < s.Length)
            {
                char c = s[_i++];
                if (c == '\'')
                {
                    if (_i < s.Length && s[_i] == '\'') { sb.Append('\''); _i++; continue; }
                    break;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
