using System.Text;

namespace Registrar.Core;

// Minimal RFC 4180 parser. Double-quoted fields may contain commas, newlines,
// and doubled quotes ("" -> "). Handles LF and CRLF row endings. A trailing
// newline does not produce an extra empty row.
public static class Csv
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true; i++; break;
                case ',':
                    row.Add(field.ToString()); field.Clear(); i++; break;
                case '\r':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                    i += (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
                    break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                    i++; break;
                default:
                    field.Append(c); i++; break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
