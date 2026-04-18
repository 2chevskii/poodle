using Octokit;

namespace Poodle.Cli.Services;

internal sealed class GitHubRepositoryServiceFactory
{
    public GitHubRepositoryService Create(IGitHubClient client)
    {
        return new GitHubRepositoryService(client);
    }
}
