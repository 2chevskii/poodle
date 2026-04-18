namespace Poodle.Cli.Models;

internal sealed record RepositoryTarget(string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";

    public string CloneUrl => $"https://github.com/{Owner}/{Name}.git";

    public string BaseHref =>
        string.Equals(Name, $"{Owner}.github.io", StringComparison.OrdinalIgnoreCase)
            ? "/"
            : $"/{Name.Trim('/')}/";

    public static RepositoryTarget Parse(string rawValue, string defaultOwner)
    {
        var value = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Repository name cannot be empty.");
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => new RepositoryTarget(defaultOwner, parts[0]),
            2 => new RepositoryTarget(parts[0], parts[1]),
            _ => throw new InvalidOperationException("Repository name must be either 'repo' or 'owner/repo'.")
        };
    }
}
