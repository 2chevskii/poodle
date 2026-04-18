using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Poodle.Cli.Services;

internal sealed class WorkflowFileService
{
    public const string WorkflowRelativePath = ".github/workflows/deploy.yaml";

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public WorkflowUpdateResult EnsureWorkflowFile(string repositoryRoot, string branchName, string artifactPath)
    {
        var workflowPath = Path.Combine(repositoryRoot, WorkflowRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var workflowDirectory = Path.GetDirectoryName(workflowPath);
        if (workflowDirectory is null)
        {
            throw new InvalidOperationException("Could not determine the workflow directory.");
        }

        Directory.CreateDirectory(workflowDirectory);

        var expectedYaml = SerializeWorkflow(branchName, artifactPath);
        var currentYaml = File.Exists(workflowPath) ? Normalize(File.ReadAllText(workflowPath)) : null;

        if (string.Equals(currentYaml, expectedYaml, StringComparison.Ordinal))
        {
            return new WorkflowUpdateResult(workflowPath, false);
        }

        File.WriteAllText(workflowPath, expectedYaml);
        return new WorkflowUpdateResult(workflowPath, true);
    }

    private string SerializeWorkflow(string branchName, string artifactPath)
    {
        var workflow = new DeployWorkflow
        {
            Name = "Deploy Pages",
            On = new WorkflowTriggers
            {
                Push = new PushTrigger
                {
                    Branches = new[] { branchName }
                },
                WorkflowDispatch = new Dictionary<string, string>()
            },
            Permissions = new WorkflowPermissions
            {
                Contents = "read",
                Pages = "write",
                IdToken = "write"
            },
            Concurrency = new WorkflowConcurrency
            {
                Group = "pages",
                CancelInProgress = true
            },
            Jobs = new Dictionary<string, WorkflowJob>
            {
                ["deploy"] = new()
                {
                    RunsOn = "ubuntu-latest",
                    Environment = new WorkflowEnvironment
                    {
                        Name = "github-pages",
                        Url = "${{ steps.deployment.outputs.page_url }}"
                    },
                    Steps = new[]
                    {
                        new WorkflowStep
                        {
                            Name = "Checkout",
                            Uses = "actions/checkout@v4"
                        },
                        new WorkflowStep
                        {
                            Name = "Configure Pages",
                            Uses = "actions/configure-pages@v5"
                        },
                        new WorkflowStep
                        {
                            Name = "Upload Pages artifact",
                            Uses = "actions/upload-pages-artifact@v3",
                            With = new Dictionary<string, string>
                            {
                                ["path"] = artifactPath
                            }
                        },
                        new WorkflowStep
                        {
                            Name = "Deploy",
                            Id = "deployment",
                            Uses = "actions/deploy-pages@v4"
                        }
                    }
                }
            }
        };

        return Normalize(_serializer.Serialize(workflow));
    }

    private static string Normalize(string content)
    {
        return content.Replace("\r\n", "\n").TrimEnd() + "\n";
    }
}

internal sealed record WorkflowUpdateResult(string Path, bool Updated);

internal sealed class DeployWorkflow
{
    [YamlMember(Alias = "name", Order = 1)]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "on", Order = 2)]
    public WorkflowTriggers On { get; init; } = new();

    [YamlMember(Alias = "permissions", Order = 3)]
    public WorkflowPermissions Permissions { get; init; } = new();

    [YamlMember(Alias = "concurrency", Order = 4)]
    public WorkflowConcurrency Concurrency { get; init; } = new();

    [YamlMember(Alias = "jobs", Order = 5)]
    public Dictionary<string, WorkflowJob> Jobs { get; init; } = new();
}

internal sealed class WorkflowTriggers
{
    [YamlMember(Alias = "push", Order = 1)]
    public PushTrigger Push { get; init; } = new();

    [YamlMember(Alias = "workflow_dispatch", Order = 2)]
    public Dictionary<string, string> WorkflowDispatch { get; init; } = new();
}

internal sealed class PushTrigger
{
    [YamlMember(Alias = "branches", Order = 1)]
    public IReadOnlyList<string> Branches { get; init; } = Array.Empty<string>();
}

internal sealed class WorkflowPermissions
{
    [YamlMember(Alias = "contents", Order = 1)]
    public string Contents { get; init; } = string.Empty;

    [YamlMember(Alias = "pages", Order = 2)]
    public string Pages { get; init; } = string.Empty;

    [YamlMember(Alias = "id-token", Order = 3)]
    public string IdToken { get; init; } = string.Empty;
}

internal sealed class WorkflowConcurrency
{
    [YamlMember(Alias = "group", Order = 1)]
    public string Group { get; init; } = string.Empty;

    [YamlMember(Alias = "cancel-in-progress", Order = 2)]
    public bool CancelInProgress { get; init; }
}

internal sealed class WorkflowJob
{
    [YamlMember(Alias = "runs-on", Order = 1)]
    public string RunsOn { get; init; } = string.Empty;

    [YamlMember(Alias = "environment", Order = 2)]
    public WorkflowEnvironment Environment { get; init; } = new();

    [YamlMember(Alias = "steps", Order = 3)]
    public IReadOnlyList<WorkflowStep> Steps { get; init; } = Array.Empty<WorkflowStep>();
}

internal sealed class WorkflowEnvironment
{
    [YamlMember(Alias = "name", Order = 1)]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "url", Order = 2)]
    public string Url { get; init; } = string.Empty;
}

internal sealed class WorkflowStep
{
    [YamlMember(Alias = "name", Order = 1)]
    public string? Name { get; init; }

    [YamlMember(Alias = "id", Order = 2)]
    public string? Id { get; init; }

    [YamlMember(Alias = "uses", Order = 3)]
    public string Uses { get; init; } = string.Empty;

    [YamlMember(Alias = "with", Order = 4)]
    public Dictionary<string, string>? With { get; init; }
}
