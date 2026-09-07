namespace TripUpdates.Gtfs;

/// <summary>
/// Minimal streaming CSV reader for GTFS text files. stop_times.txt is ~45 MB, so rows are
/// read one at a time and never materialised as a list.
/// </summary>
internal sealed class CsvRows(TextReader reader) : IDisposable
{
    private readonly Dictionary<string, int> _columns = ReadHeader(reader);
    private string[] _fields = [];

    private static Dictionary<string, int> ReadHeader(TextReader reader)
    {
        var line = reader.ReadLine() ?? throw new InvalidDataException("CSV file is empty.");
        var header = SplitLine(line.TrimStart('﻿'));
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
            columns[header[i].Trim()] = i;
        return columns;
    }

    public bool HasColumn(string name) => _columns.ContainsKey(name);

    public bool Read()
    {
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            _fields = SplitLine(line);
            return true;
        }
        return false;
    }

    public string this[string column] =>
        _columns.TryGetValue(column, out var i) && i < _fields.Length ? _fields[i] : "";

    private static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c != '"') { value.Append(c); continue; }
                if (i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        fields.Add(value.ToString());
        return [.. fields];
    }

    public void Dispose() => reader.Dispose();
}
