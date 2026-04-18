using System.Net;
using Octokit;

namespace Poodle.Cli.Services;

internal sealed class GitHubRepositoryService
{
    private const string GitHubJsonMediaType = "application/vnd.github+json";
    private const string JsonContentType = "application/json";
    private const string WorkflowFileName = "deploy.yaml";
    private static readonly IDictionary<string, string> NoParameters =
        new Dictionary<string, string>();
    private readonly IGitHubClient _client;
    private readonly IConnection _connection;

    public GitHubRepositoryService(IGitHubClient client)
    {
        _client = client;
        _connection = client.Connection;
    }

    public async Task<(Repository Repository, bool Created)> EnsureRepositoryAsync(
        string owner,
        string repositoryName
    )
    {
        try
        {
            var existing = await _client.Repository.Get(owner, repositoryName);
            return (existing, false);
        }
        catch (NotFoundException)
        {
            var createRequest = new NewRepository(repositoryName)
            {
                AutoInit = false,
                Private = false,
            };

            var currentUser = await _client.User.Current();
            var created = string.Equals(
                owner,
                currentUser.Login,
                StringComparison.OrdinalIgnoreCase
            )
                ? await _client.Repository.Create(createRequest)
                : await _client.Repository.Create(owner, createRequest);

            return (created, true);
        }
    }

    public async Task ConfigurePagesForWorkflowAsync(
        string owner,
        string repositoryName,
        string branchName,
        CancellationToken cancellationToken
    )
    {
        var endpoint = new Uri($"repos/{owner}/{repositoryName}/pages", UriKind.Relative);
        var request = new Dictionary<string, object?>
        {
            ["build_type"] = "workflow",
            ["source"] = new Dictionary<string, string> { ["branch"] = branchName, ["path"] = "/" },
        };

        try
        {
            await _connection.Get<Page>(
                endpoint,
                NoParameters,
                GitHubJsonMediaType,
                cancellationToken
            );
            await _connection.Put<Page>(endpoint, request);
        }
        catch (NotFoundException)
        {
            await _connection.Post<Page>(
                endpoint,
                request,
                GitHubJsonMediaType,
                JsonContentType,
                NoParameters,
                cancellationToken
            );
        }
        catch (ApiException exception)
            when (exception.StatusCode
                    is HttpStatusCode.Conflict
                        or HttpStatusCode.UnprocessableEntity
            )
        {
            throw new PagesConfigurationDeferredException(
                "GitHub Pages could not be configured yet, likely because the branch does not exist remotely.",
                exception
            );
        }
    }

    public async Task TryEnableWorkflowAsync(string owner, string repositoryName)
    {
        try
        {
            var workflow = await _client.Actions.Workflows.Get(
                owner,
                repositoryName,
                WorkflowFileName
            );
            if (workflow.State.Value != WorkflowState.Active)
            {
                await _client.Actions.Workflows.Enable(owner, repositoryName, WorkflowFileName);
            }
        }
        catch (NotFoundException) { }
    }

    public async Task<WorkflowRun> WaitForWorkflowRunAsync(
        string owner,
        string repositoryName,
        string commitSha,
        CancellationToken cancellationToken
    )
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddMinutes(5);
        WorkflowRun? run = null;

        while (DateTimeOffset.UtcNow < timeoutAt && run is null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            run = await TryGetWorkflowRunAsync(owner, repositoryName, commitSha);
            if (run is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        if (run is null)
        {
            throw new InvalidOperationException(
                $"Could not find a workflow run for commit {commitSha[..Math.Min(7, commitSha.Length)]}."
            );
        }

        while (run.Status.Value != WorkflowRunStatus.Completed)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            run = await _client.Actions.Workflows.Runs.Get(owner, repositoryName, run.Id);
        }

        return run;
    }

    private async Task<WorkflowRun?> TryGetWorkflowRunAsync(
        string owner,
        string repositoryName,
        string commitSha
    )
    {
        var request = new WorkflowRunsRequest { HeadSha = commitSha };

        try
        {
            var response = await _client.Actions.Workflows.Runs.ListByWorkflow(
                owner,
                repositoryName,
                WorkflowFileName,
                request
            );
            return response.WorkflowRuns.OrderByDescending(run => run.CreatedAt).FirstOrDefault();
        }
        catch (NotFoundException)
        {
            var response = await _client.Actions.Workflows.Runs.List(
                owner,
                repositoryName,
                request
            );
            return response
                .WorkflowRuns.Where(run =>
                    string.Equals(run.HeadSha, commitSha, StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(run => run.CreatedAt)
                .FirstOrDefault();
        }
    }
}

internal sealed class PagesConfigurationDeferredException : Exception
{
    public PagesConfigurationDeferredException(string message, Exception innerException)
        : base(message, innerException) { }
}
