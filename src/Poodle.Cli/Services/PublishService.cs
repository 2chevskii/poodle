using LibGit2Sharp;
using Octokit;
using Poodle.Cli.Models;
using Spectre.Console;
using GitCommit = LibGit2Sharp.Commit;
using GitRepository = LibGit2Sharp.Repository;

namespace Poodle.Cli.Services;

internal sealed class PublishService
{
    private readonly IAnsiConsole _console;
    private readonly GitHubAuthenticator _gitHubAuthenticator;
    private readonly GitHubRepositoryServiceFactory _gitHubRepositoryServiceFactory;
    private readonly GitRepositoryService _gitRepositoryService;
    private readonly HtmlBaseTagService _htmlBaseTagService;
    private readonly WorkflowFileService _workflowFileService;

    public PublishService(
        IAnsiConsole console,
        GitHubAuthenticator gitHubAuthenticator,
        GitHubRepositoryServiceFactory gitHubRepositoryServiceFactory,
        GitRepositoryService gitRepositoryService,
        HtmlBaseTagService htmlBaseTagService,
        WorkflowFileService workflowFileService)
    {
        _console = console;
        _gitHubAuthenticator = gitHubAuthenticator;
        _gitHubRepositoryServiceFactory = gitHubRepositoryServiceFactory;
        _gitRepositoryService = gitRepositoryService;
        _htmlBaseTagService = htmlBaseTagService;
        _workflowFileService = workflowFileService;
    }

    public async Task<int> RunAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(workingDirectory, cancellationToken);
        }
        catch (Exception exception)
        {
            _console.MarkupLine($"[red]Publish failed:[/] {Markup.Escape(exception.Message)}");
            return 1;
        }
    }

    private async Task<int> RunCoreAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        RenderHeader();

        var workspace = LoadWorkspace(workingDirectory);
        using var repository = workspace.Repository;

        var authenticatedClient = await _gitHubAuthenticator.AuthenticateAsync(cancellationToken);
        var repositoryTarget = ResolveRepositoryTarget(workspace.Config, authenticatedClient.User.Login);
        var remoteContext = await EnsureRemoteRepositoryAsync(
            repository,
            repositoryTarget,
            workspace.BranchName,
            authenticatedClient,
            cancellationToken);

        ApplyLocalPublishingChanges(workspace, repositoryTarget);

        var commitSha = CommitAndPushChanges(
            repository,
            workspace.BranchName,
            repositoryTarget,
            authenticatedClient);

        return await FinalizePublishAsync(
            remoteContext,
            repositoryTarget,
            workspace.BranchName,
            commitSha,
            cancellationToken);
    }

    private void RenderHeader()
    {
        _console.Write(new Rule("[yellow]Poodle Publish[/]").LeftJustified());
    }

    private PublishWorkspace LoadWorkspace(string workingDirectory)
    {
        var config = PoodleConfig.Load(workingDirectory);
        if (config is null)
        {
            _console.MarkupLine("[grey].poodle.json was not found. Repository name will be requested interactively.[/]");
        }
        else
        {
            _console.MarkupLine("[green]Loaded configuration from[/] [grey].poodle.json[/].");
        }

        var repositoryHandle = _gitRepositoryService.EnsureRepository(workingDirectory);
        var repository = repositoryHandle.Repository;

        if (repositoryHandle.Initialized)
        {
            _console.MarkupLine("[green]Initialized a new git repository in the current working directory.[/]");
        }

        _gitRepositoryService.EnsureWorkingDirectoryIsInsideRepository(repository, workingDirectory);

        var repositoryRoot = _gitRepositoryService.GetRepositoryRoot(repository);
        var branchName = _gitRepositoryService.GetCurrentBranchName(repository);
        var contentRoot = Path.GetFullPath(workingDirectory);
        var artifactPath = GetArtifactPath(repositoryRoot, contentRoot);

        _console.MarkupLine($"[blue]Using git repository:[/] [grey]{Markup.Escape(repositoryRoot)}[/]");
        _console.MarkupLine($"[blue]Publishing branch:[/] [bold]{Markup.Escape(branchName)}[/]");
        _console.MarkupLine($"[blue]Publishing content root:[/] [grey]{Markup.Escape(contentRoot)}[/]");

        return new PublishWorkspace(config, repository, repositoryRoot, branchName, contentRoot, artifactPath);
    }

    private RepositoryTarget ResolveRepositoryTarget(PoodleConfig? config, string defaultOwner)
    {
        var repositoryInput = config?.Repository ?? PromptForRepositoryName();
        var repositoryTarget = RepositoryTarget.Parse(repositoryInput, defaultOwner);
        _console.MarkupLine($"[blue]Target repository:[/] [bold]{Markup.Escape(repositoryTarget.FullName)}[/]");
        return repositoryTarget;
    }

    private async Task<RemotePublishContext> EnsureRemoteRepositoryAsync(
        GitRepository repository,
        RepositoryTarget repositoryTarget,
        string branchName,
        GitHubAuthenticator.AuthenticatedGitHubClient authenticatedClient,
        CancellationToken cancellationToken)
    {
        var gitHubRepositoryService = _gitHubRepositoryServiceFactory.Create(authenticatedClient.Client);
        var (remoteRepository, created) = await gitHubRepositoryService
            .EnsureRepositoryAsync(repositoryTarget.Owner, repositoryTarget.Name);

        _console.MarkupLine(
            created
                ? $"[green]Created remote repository[/] [bold]{Markup.Escape(repositoryTarget.FullName)}[/]."
                : $"[green]Remote repository exists:[/] [bold]{Markup.Escape(repositoryTarget.FullName)}[/].");

        _gitRepositoryService.EnsureOrigin(repository, remoteRepository.CloneUrl);
        _console.MarkupLine($"[green]Configured local[/] [bold]origin[/] [green]remote to[/] [grey]{Markup.Escape(remoteRepository.CloneUrl)}[/].");

        var pagesNeedRetry = await TryConfigurePagesAsync(
            gitHubRepositoryService,
            repositoryTarget,
            branchName,
            cancellationToken);

        return new RemotePublishContext(gitHubRepositoryService, pagesNeedRetry);
    }

    private async Task<bool> TryConfigurePagesAsync(
        GitHubRepositoryService gitHubRepositoryService,
        RepositoryTarget repositoryTarget,
        string branchName,
        CancellationToken cancellationToken)
    {
        try
        {
            await gitHubRepositoryService.ConfigurePagesForWorkflowAsync(
                repositoryTarget.Owner,
                repositoryTarget.Name,
                branchName,
                cancellationToken);
            _console.MarkupLine("[green]Configured GitHub Pages to publish via GitHub Actions.[/]");
            return false;
        }
        catch (PagesConfigurationDeferredException)
        {
            _console.MarkupLine("[yellow]GitHub Pages configuration will be retried after the first push.[/]");
            return true;
        }
    }

    private void ApplyLocalPublishingChanges(PublishWorkspace workspace, RepositoryTarget repositoryTarget)
    {
        ReportWorkflowUpdate(_workflowFileService.EnsureWorkflowFile(
            workspace.RepositoryRoot,
            workspace.BranchName,
            workspace.ArtifactPath));

        ReportHtmlBaseUpdates(_htmlBaseTagService.EnsureBaseTags(workspace.ContentRoot, repositoryTarget.BaseHref));

        RenderChangeTable(_gitRepositoryService.GetWorktreeChanges(workspace.Repository));
    }

    private string CommitAndPushChanges(
        GitRepository repository,
        string branchName,
        RepositoryTarget repositoryTarget,
        GitHubAuthenticator.AuthenticatedGitHubClient authenticatedClient)
    {
        var commit = _gitRepositoryService.CommitAllChanges(
            repository,
            authenticatedClient.User,
            "Publish with Poodle");

        ReportCommit(commit);

        _gitRepositoryService.Push(
            repository,
            branchName,
            authenticatedClient.User.Login,
            authenticatedClient.AccessToken);
        _console.MarkupLine($"[green]Pushed[/] [bold]{Markup.Escape(branchName)}[/] [green]to[/] [bold]{Markup.Escape(repositoryTarget.FullName)}[/].");

        var commitSha = commit?.Sha ?? repository.Head.Tip?.Sha;
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            throw new InvalidOperationException("Could not determine the commit SHA to inspect.");
        }

        return commitSha;
    }

    private async Task<int> FinalizePublishAsync(
        RemotePublishContext remoteContext,
        RepositoryTarget repositoryTarget,
        string branchName,
        string commitSha,
        CancellationToken cancellationToken)
    {
        if (remoteContext.PagesNeedRetry)
        {
            await remoteContext.GitHubRepositoryService.ConfigurePagesForWorkflowAsync(
                repositoryTarget.Owner,
                repositoryTarget.Name,
                branchName,
                cancellationToken);
            _console.MarkupLine("[green]Configured GitHub Pages after the initial push.[/]");
        }

        await remoteContext.GitHubRepositoryService.TryEnableWorkflowAsync(repositoryTarget.Owner, repositoryTarget.Name);

        _console.MarkupLine($"[blue]Waiting for GitHub Actions workflow run for[/] [bold]{Markup.Escape(commitSha[..7])}[/]...");
        var run = await remoteContext.GitHubRepositoryService.WaitForWorkflowRunAsync(
            repositoryTarget.Owner,
            repositoryTarget.Name,
            commitSha,
            cancellationToken);

        _console.MarkupLine(
            $"[green]Workflow finished with status[/] [bold]{Markup.Escape(run.Status.StringValue)}[/] [green]and conclusion[/] [bold]{Markup.Escape(run.Conclusion?.ToString() ?? "none")}[/].");
        _console.MarkupLine($"[green]Workflow URL:[/] [link]{Markup.Escape(run.HtmlUrl)}[/]");

        return run.Conclusion == WorkflowRunConclusion.Success ? 0 : 1;
    }

    private void ReportWorkflowUpdate(WorkflowUpdateResult workflowUpdate)
    {
        _console.MarkupLine(
            workflowUpdate.Updated
                ? $"[green]Generated or updated workflow:[/] [grey]{Markup.Escape(workflowUpdate.Path)}[/]"
                : $"[grey]Workflow already matches expected contents:[/] [grey]{Markup.Escape(workflowUpdate.Path)}[/]");
    }

    private void ReportHtmlBaseUpdates(IReadOnlyList<string> htmlChanges)
    {
        _console.MarkupLine(
            htmlChanges.Count > 0
                ? $"[green]Updated <base> tags in[/] [bold]{htmlChanges.Count}[/] [green]HTML file(s).[/]"
                : "[grey]All HTML files already have the expected <base> tag.[/]");
    }

    private void ReportCommit(GitCommit? commit)
    {
        if (commit is null)
        {
            _console.MarkupLine("[grey]No local changes needed to be committed.[/]");
            return;
        }

        _console.MarkupLine($"[green]Created commit[/] [bold]{Markup.Escape(commit.Sha[..7])}[/]: {Markup.Escape(commit.MessageShort)}");
    }

    private string PromptForRepositoryName()
    {
        return _console.Prompt(
            new TextPrompt<string>("GitHub repository name:")
                .PromptStyle("green")
                .Validate(value =>
                    !string.IsNullOrWhiteSpace(value)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Repository name is required.[/]")));
    }

    private static string GetArtifactPath(string repositoryRoot, string contentRoot)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, contentRoot)
            .Replace('\\', '/');

        return string.Equals(relativePath, ".", StringComparison.Ordinal) ? "." : relativePath;
    }

    private void RenderChangeTable(IReadOnlyList<StatusEntry> changes)
    {
        if (changes.Count == 0)
        {
            _console.MarkupLine("[grey]No worktree changes to show.[/]");
            return;
        }

        var table = new Table()
            .RoundedBorder()
            .AddColumn("State")
            .AddColumn("Path");

        foreach (var change in changes.OrderBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(
                Markup.Escape(change.State.ToString()),
                Markup.Escape(change.FilePath));
        }

        _console.Write(table);
    }

    private sealed record PublishWorkspace(
        PoodleConfig? Config,
        GitRepository Repository,
        string RepositoryRoot,
        string BranchName,
        string ContentRoot,
        string ArtifactPath);

    private sealed record RemotePublishContext(
        GitHubRepositoryService GitHubRepositoryService,
        bool PagesNeedRetry);
}
