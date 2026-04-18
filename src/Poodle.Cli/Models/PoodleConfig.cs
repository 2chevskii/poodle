using System.Text.Json;

namespace Poodle.Cli.Models;

internal sealed class PoodleConfig
{
    private const string FileName = ".poodle.json";

    public string? Repository { get; set; }

    public static PoodleConfig? Load(string workingDirectory)
    {
        var path = GetPath(workingDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PoodleConfig>(json, JsonOptions);
    }

    public void Save(string workingDirectory)
    {
        var path = GetPath(workingDirectory);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static string GetPath(string workingDirectory)
    {
        return Path.Combine(workingDirectory, FileName);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
