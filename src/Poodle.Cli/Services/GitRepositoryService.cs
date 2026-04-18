using LibGit2Sharp;
using Octokit;
using GitCommit = LibGit2Sharp.Commit;
using GitRepository = LibGit2Sharp.Repository;
using GitSignature = LibGit2Sharp.Signature;

namespace Poodle.Cli.Services;

internal sealed class GitRepositoryService
{
    public RepositoryHandle EnsureRepository(string workingDirectory)
    {
        var repositoryPath = GitRepository.Discover(workingDirectory);
        if (repositoryPath is null)
        {
            var initializedRepositoryPath = GitRepository.Init(workingDirectory);
            return new RepositoryHandle(new GitRepository(initializedRepositoryPath), true);
        }

        return new RepositoryHandle(new GitRepository(repositoryPath), false);
    }

    public string GetRepositoryRoot(GitRepository repository)
    {
        return repository.Info.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public void EnsureWorkingDirectoryIsInsideRepository(GitRepository repository, string workingDirectory)
    {
        var repositoryRoot = Path.GetFullPath(GetRepositoryRoot(repository)) + Path.DirectorySeparatorChar;
        var currentPath = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!currentPath.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The current working directory must be inside the git repository.");
        }
    }

    public string GetCurrentBranchName(GitRepository repository)
    {
        if (repository.Info.IsHeadDetached)
        {
            throw new InvalidOperationException("The current repository is in a detached HEAD state.");
        }

        return repository.Head.FriendlyName;
    }

    public void EnsureOrigin(GitRepository repository, string remoteUrl)
    {
        var origin = repository.Network.Remotes["origin"];
        if (origin is null)
        {
            repository.Network.Remotes.Add("origin", remoteUrl);
            return;
        }

        if (!string.Equals(origin.Url, remoteUrl, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.PushUrl, remoteUrl, StringComparison.OrdinalIgnoreCase))
        {
            repository.Network.Remotes.Update(
                "origin",
                updater => updater.Url = remoteUrl,
                updater => updater.PushUrl = remoteUrl);
        }
    }

    public IReadOnlyList<StatusEntry> GetWorktreeChanges(GitRepository repository)
    {
        var status = repository.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
            IncludeIgnored = false
        });

        return status.ToList();
    }

    public GitCommit? CommitAllChanges(GitRepository repository, User user, string message)
    {
        var status = GetWorktreeChanges(repository);
        if (status.Count == 0)
        {
            return null;
        }

        LibGit2Sharp.Commands.Stage(repository, "*");

        var signature = BuildSignature(repository, user);
        return repository.Commit(message, signature, signature);
    }

    public void Push(GitRepository repository, string branchName, string username, string accessToken)
    {
        var remote = repository.Network.Remotes["origin"]
            ?? throw new InvalidOperationException("Remote 'origin' is not configured.");

        var pushOptions = new PushOptions
        {
            CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials
            {
                Username = username,
                Password = accessToken
            }
        };

        repository.Network.Push(remote, $"refs/heads/{branchName}:refs/heads/{branchName}", pushOptions);
    }

    private static GitSignature BuildSignature(GitRepository repository, User user)
    {
        var configuredSignature = repository.Config.BuildSignature(DateTimeOffset.Now);
        if (configuredSignature is not null)
        {
            return configuredSignature;
        }

        var name = user.Name ?? user.Login;
        var email = user.Email ?? $"{user.Login}@users.noreply.github.com";
        return new GitSignature(name, email, DateTimeOffset.Now);
    }

    internal sealed record RepositoryHandle(GitRepository Repository, bool Initialized);
}
