using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WRApperture.Host.Cli;

internal static class Output
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void WriteJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));

    /// <summary>Prints rows as a left-aligned table.</summary>
    public static void WriteTable(string[] headers, IEnumerable<string[]> rows)
    {
        var all = rows.ToList();
        var widths = headers.Select((h, i) => Math.Max(h.Length, all.Count == 0 ? 0 : all.Max(r => r[i].Length))).ToArray();
        Console.WriteLine(string.Join("  ", headers.Select((h, i) => h.PadRight(widths[i]))).TrimEnd());
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in all)
        {
            Console.WriteLine(string.Join("  ", row.Select((c, i) => c.PadRight(widths[i]))).TrimEnd());
        }
    }

    public static void Error(string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = previous;
    }

    public static void Warn(string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
