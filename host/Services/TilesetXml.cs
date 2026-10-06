// Reads TSX and replaces selected property spans without reserializing unrelated XML.
// This helper owns XML syntax only; TileMetadataAuthoringService owns editing policy.
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MMO.ContentStudio.AuthoringHost.Services;

internal sealed class TilesetXml
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public byte[] Bytes { get; }
    public string Text { get; }
    public XElement Root { get; }
    private readonly bool _bom;
    private readonly int[] _lines;
    public TilesetXml(byte[] bytes)
    {
        Bytes = bytes;
        _bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
        Text = Utf8.GetString(bytes.AsSpan(_bom ? 3 : 0));
        using var reader = XmlReader.Create(new StringReader(Text), Settings());
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (document.Declaration?.Encoding is { } encoding && !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            throw new TileEditException("Only UTF-8 TSX files can be edited.");
        Root = document.Root ?? throw new TileEditException("Tileset XML has no root.");
        if (Root.Name != "tileset") throw new TileEditException("Expected an unnamespaced TSX tileset.");
        _lines = Lines(Text);
        var ids = new HashSet<int>();
        foreach (var tile in Root.Elements("tile"))
        {
            if (!ids.Add(Number(tile, "id"))) throw new TileEditException("Duplicate tile IDs are not supported.");
            if (tile.Elements("properties").Count() > 1) throw new TileEditException("A tile has duplicate properties blocks.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in tile.Element("properties")?.Elements("property") ?? [])
                if (property.Attribute("name") is not { } name || !names.Add(name.Value))
                    throw new TileEditException("A tile has missing or duplicate property names. Correct it in Tiled first.");
        }
        if (Root.Elements("image").Count() > 1 || (Root.Element("image") is not null && Root.Elements("tile").Any(t => t.Element("image") is not null)))
            throw new TileEditException("Mixed atlas and collection images are not supported.");
    }
    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
        MaxCharactersInDocument = 4 * 1024 * 1024, IgnoreWhitespace = false
    };
    public static int Number(XElement element, string name, int? fallback = null)
    {
        var value = (string?)element.Attribute(name);
        if (value is null && fallback is not null) return fallback.Value;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0)
            throw new TileEditException($"Invalid tileset {name}.");
        return number;
    }
    public XElement? Tile(int id) => Root.Elements("tile").FirstOrDefault(t => Number(t, "id") == id);
    public static string? Value(XElement? property) => property is null ? null : (string?)property.Attribute("value") ?? property.Value;
    public static bool SimpleProperty(XElement property, string type) =>
        !property.HasElements && !property.Nodes().Any(n => n is not XText) &&
        property.Attributes().All(a => a.Name == "name" || a.Name == "type" || a.Name == "value") &&
        ((string?)property.Attribute("type") ?? "string") == type &&
        (property.Attribute("value") is null || string.IsNullOrWhiteSpace(property.Value)) &&
        (type != "bool" || Value(property) is "true" or "false");

    // All spans refer to the original string. Apply right-to-left so offsets remain valid.
    public byte[] Patch(int tileId, IReadOnlyDictionary<string, string?> changes)
    {
        var tile = Tile(tileId);
        var patches = new List<(int Start, int Length, string Replacement)>();
        var additions = new List<string>();
        var properties = tile?.Element("properties");
        foreach (var (name, value) in changes)
        {
            var existing = properties?.Elements("property").SingleOrDefault(p => (string?)p.Attribute("name") == name);
            if (Value(existing) == value) continue;
            var replacement = value is null ? "" : new XElement("property", new XAttribute("name", name),
                new XAttribute("type", name is "ignitable" or "slick" ? "bool" : "string"), new XAttribute("value", value)).ToString(SaveOptions.DisableFormatting);
            if (existing is not null)
            {
                var span = Span(existing);
                patches.Add((span.Start, span.Length, replacement));
            }
            else if (value is not null) additions.Add(replacement);
        }
        if (additions.Count > 0)
        {
            var newline = Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var body = string.Join(newline + "   ", additions);
            if (tile is null)
            {
                var rootSpan = Span(Root);
                patches.Add((ClosingStart(rootSpan), 0, $" <tile id=\"{tileId}\">{newline}  <properties>{newline}   {body}{newline}  </properties>{newline} </tile>{newline}"));
            }
            else if (properties is null)
            {
                var span = Span(tile);
                if (tile.IsEmpty)
                    patches.Add((span.Start, span.Length, Text.Substring(span.Start, span.Length - 2) + $">{newline}  <properties>{newline}   {body}{newline}  </properties>{newline} </tile>"));
                else
                    patches.Add((OpeningEnd(span.Start), 0, $"{newline}  <properties>{newline}   {body}{newline}  </properties>"));
            }
            else
            {
                var span = Span(properties);
                if (properties.IsEmpty)
                    patches.Add((span.Start, span.Length, Text.Substring(span.Start, span.Length - 2) + $">{newline}   {body}{newline}  </properties>"));
                else
                    patches.Add((ClosingStart(span), 0, $"{newline}   {body}{newline}  "));
            }
        }
        var result = Text;
        foreach (var patch in patches.OrderByDescending(p => p.Start))
            result = result.Remove(patch.Start, patch.Length).Insert(patch.Start, patch.Replacement);
        var output = Utf8.GetBytes(result);
        if (_bom) output = [239, 187, 191, .. output];
        _ = new TilesetXml(output); // Reject invalid output before any file operation.
        return output;
    }
    private (int Start, int Length) Span(XElement element)
    {
        var location = (IXmlLineInfo)element;
        var start = _lines[location.LineNumber - 1] + location.LinePosition - 2;
        if (start < 0 || Text[start] != '<') throw new TileEditException("Unsupported XML source position.");
        if (element.IsEmpty) return (start, OpeningEnd(start) - start);
        var fragment = Text[start..];
        var lines = Lines(fragment);
        var settings = Settings(); settings.ConformanceLevel = ConformanceLevel.Fragment;
        using var reader = XmlReader.Create(new StringReader(fragment), settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != 0) continue;
            var end = (IXmlLineInfo)reader;
            var position = lines[end.LineNumber - 1] + end.LinePosition - 1;
            return (start, fragment.IndexOf('>', position) + 1);
        }
        throw new TileEditException("Cannot locate the XML element boundary.");
    }
    private int ClosingStart((int Start, int Length) span) => Text.LastIndexOf("</", span.Start + span.Length - 1, span.Length, StringComparison.Ordinal);
    private int OpeningEnd(int start)
    {
        char quote = '\0';
        for (var i = start; i < Text.Length; i++)
        {
            var c = Text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '\'' or '"') quote = c;
            else if (c == '>') return i + 1;
        }
        throw new TileEditException("Incomplete XML opening tag.");
    }
    private static int[] Lines(string text) => new[] { 0 }.Concat(text.Select((c, i) => (c, i)).Where(x => x.c == '\n').Select(x => x.i + 1)).ToArray();
}

public sealed class TileEditException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
