using System.Text.Json;

namespace Poodle.Cli.Models;

internal sealed class PoodleConfig
{
    public string? Repository { get; set; }

    public static PoodleConfig? Load(string workingDirectory)
    {
        var path = Path.Combine(workingDirectory, ".poodle.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PoodleConfig>(json, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
