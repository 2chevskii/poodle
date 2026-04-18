# Poodle Agent Guide

This file explains the current state of the `poodle` project for coding agents working in this repository.

Read this before making changes. The project is still small, but it already has a few important behavioral and architectural constraints that are easy to break if you change things casually.

## Project Purpose

`poodle` is a .NET CLI utility for publishing static web content from the current working directory to GitHub Pages.

Right now the MVP exposes a single command:

- `publish`

The current user-facing workflow is:

1. Load local project config from `.poodle.json` in the working directory, if it exists.
2. Ensure the current directory is inside a git repository.
3. If there is no repository yet, initialize one automatically in the working directory.
4. Authenticate with GitHub using Octokit device flow.
5. Reuse a stored GitHub access token from `%LOCALAPPDATA%\poodle\auth.json` if it is still valid.
6. Ask for the target repository name only when it is not present in `.poodle.json`.
7. Persist the chosen repository name back into `.poodle.json`.
8. Ensure the remote GitHub repository exists.
9. Configure GitHub Pages to publish from GitHub Actions.
10. Generate or update `.github/workflows/deploy.yaml`.
11. Ensure HTML files have the expected `<base>` tag.
12. Show worktree changes.
13. Commit if there are changes.
14. Push to GitHub.
15. Wait for the matching GitHub Actions workflow run and report the result.

The goal of the codebase is not generic deployment infrastructure. It is a focused CLI for one publishing workflow.

## Technology Choices

The project intentionally uses these libraries:

- `System.CommandLine` for command parsing
- `Spectre.Console` for console output and prompts
- `Octokit` for GitHub API access
- `LibGit2Sharp` for local git access
- `YamlDotNet` for strongly typed workflow YAML generation
- `Microsoft.Extensions.DependencyInjection` for the small composition root

Do not replace these libraries unless the task explicitly asks for it.

## Repository Layout

Main source lives under `src/Poodle.Cli`.

Important files:

- `Program.cs`
  - Composition root
  - Registers services with DI
  - Builds the root command

- `Commands/PublishCommand.cs`
  - Thin command adapter
  - Delegates directly to `PublishService`

- `Services/PublishService.cs`
  - Main orchestration service for the `publish` flow
  - Coordinates local git, GitHub auth, remote repo setup, workflow generation, HTML rewriting, commit, push, and workflow polling

- `Services/GitHubAuthenticator.cs`
  - Handles GitHub authentication
  - Stores access token in `%LOCALAPPDATA%\poodle\auth.json`
  - Reuses stored access token if it is still valid
  - Falls back to device flow when token reuse fails

- `Services/GitHubRepositoryService.cs`
  - Wraps GitHub repository/pages/actions operations
  - Uses `IGitHubClient` and `IConnection`
  - Performs repository existence/create, Pages configuration, workflow enablement, and workflow run polling

- `Services/GitHubRepositoryServiceFactory.cs`
  - Small factory for creating `GitHubRepositoryService` after authentication
  - Exists because the GitHub service depends on an authenticated client produced at runtime

- `Services/GitRepositoryService.cs`
  - Wraps local git operations via `LibGit2Sharp`
  - Initializes repository if missing
  - Reads status
  - Configures `origin`
  - Creates commits
  - Pushes using HTTPS with the GitHub access token

- `Services/WorkflowFileService.cs`
  - Owns generation of `.github/workflows/deploy.yaml`
  - Uses typed DTOs plus `YamlDotNet`
  - Normalizes YAML and only writes when contents differ

- `Services/HtmlBaseTagService.cs`
  - Rewrites HTML files to ensure `<base href="...">`
  - Skips `.git`, `bin`, and `obj`

- `Models/PoodleConfig.cs`
  - Represents `.poodle.json`
  - Currently only persists the repository name

- `Models/RepositoryTarget.cs`
  - Parses repository identifiers
  - Computes clone URL
  - Computes GitHub Pages base path

## Current Architecture

The architecture is deliberately simple.

- `PublishCommand` is a thin adapter.
- `PublishService` is the application service for the end-to-end command.
- Lower-level services encapsulate one responsibility each.
- DI is minimal and manual via `ServiceCollection` in `Program.cs`.

This is not meant to become a large layered enterprise app. Prefer small, direct services over abstraction-heavy designs.

## Design Intent

There are a few important design choices already in place:

### 1. The CLI is stateful across runs

Two files are intentionally used for persistence:

- `.poodle.json` in the project working directory
  - project-local config
  - currently stores `repository`

- `%LOCALAPPDATA%\poodle\auth.json`
  - user-local auth cache
  - currently stores only the GitHub access token and its optional expiry

If you change auth or config behavior, preserve this distinction:

- project-specific settings belong in `.poodle.json`
- user credentials belong under `%LOCALAPPDATA%`

### 2. Repository initialization is part of the user experience

The tool must work in a plain folder. It should not require `git init` as a manual prerequisite.

That behavior lives in `GitRepositoryService.EnsureRepository`.

Do not reintroduce an assumption that the repository already exists.

### 3. Access token reuse is intentional

Right now the tool stores only an access token and attempts to reuse it by validating it against GitHub.

This is based on observed runtime behavior, where device flow was not yielding a useful refresh token for this app/client setup.

Do not bring refresh-token logic back unless there is a concrete product requirement and live verification that GitHub is actually issuing and accepting refresh tokens for this client.

### 4. Workflow generation is declarative

`WorkflowFileService` uses typed DTOs and `YamlDotNet` on purpose.

Do not replace workflow generation with large string literals or hand-built YAML if you can avoid it. Small string-based normalization is fine, but the structure should remain strongly typed.

### 5. Pages configuration sometimes needs a retry after first push

The tool intentionally treats some GitHub Pages configuration errors as temporary and retries after the branch exists remotely.

Do not remove this without replacing it with an equally reliable approach.

## How The Publish Flow Is Split

`PublishService` currently divides the flow into these steps:

- `LoadWorkspace`
  - load config
  - ensure/init git repo
  - calculate repository root, branch, content root, artifact path

- `ResolveRepositoryTarget`
  - use config if repository is already present
  - otherwise prompt
  - persist `.poodle.json`

- `EnsureRemoteRepositoryAsync`
  - ensure remote repo exists
  - configure `origin`
  - try GitHub Pages configuration

- `ApplyLocalPublishingChanges`
  - update workflow file
  - update HTML base tags
  - show worktree changes

- `CommitAndPushChanges`
  - commit if needed
  - push branch
  - return commit SHA

- `FinalizePublishAsync`
  - retry Pages configuration if needed
  - enable workflow if disabled
  - wait for matching Actions run

If you refactor this area again, keep the flow readable and chronological.

## Build And Verification

Useful commands:

- `dotnet build Poodle.slnx`
- `dotnet run --project src\\Poodle.Cli -- publish --help`

If you make behavior changes, at minimum:

1. Build the solution.
2. Run the help command.
3. If practical, run a local publish scenario against a throwaway folder or repo.

If you cannot do an end-to-end GitHub verification because it requires interactive login or external state, say so explicitly.

## Files Agents Should Usually Ignore

Unless the task is specifically about packaging or build artifacts, ignore:

- `bin/`
- `obj/`
- generated publish outputs

Agent work should focus on source files, not generated artifacts.

## Coding Guidance For This Repo

### Keep changes surgical

This repo follows a “small code that does one thing” style.

Good changes:

- clear orchestration
- small helpers with real names
- direct use of the chosen libraries
- minimal DTOs for external formats

Bad changes:

- abstract factories for everything
- speculative interfaces for code with one implementation
- broad refactors unrelated to the request
- adding configuration knobs that nobody asked for

### Prefer behavior-preserving refactors

If the task is “clean up” or “refactor”, preserve:

- user-visible console output where reasonable
- config file location and meaning
- auth storage location
- workflow path
- repository initialization behavior
- current GitHub Pages deployment mode through GitHub Actions

### Preserve current DI style

DI exists now, but it is intentionally lightweight.

Use constructor injection for runtime services. Avoid over-engineering the service graph.

If a service depends on runtime-authenticated GitHub state, a small factory is acceptable. That is why `GitHubRepositoryServiceFactory` exists.

### Keep GitHub usage inside services

`PublishService` should orchestrate, not know Octokit details. Push GitHub-specific logic into `GitHubRepositoryService` or `GitHubAuthenticator`.

### Keep git usage inside `GitRepositoryService`

Avoid sprinkling `LibGit2Sharp` calls around the codebase unless you are intentionally moving git behavior into a better home.

## Known Behavioral Details

These are easy to miss:

- Repository input supports either:
  - `repo`
  - `owner/repo`

- If only `repo` is provided, owner defaults to the authenticated GitHub user.

- GitHub Pages base path logic:
  - repository named `{owner}.github.io` gets `/`
  - everything else gets `/{repo}/`

- HTML rewriting only targets `*.html`.

- Workflow generation points the Pages artifact at the current working directory relative to the repository root.

- The push path uses HTTPS plus the stored GitHub access token.

- Commit author defaults to git config signature if available, otherwise falls back to GitHub user info / noreply email.

- If nothing changed locally, commit creation is skipped.

- The tool still pushes even when there is no new local commit, because remote branch state may still need to exist for Pages/workflow behavior.

## When Modifying Authentication

Be careful here. This area has already had a few iterations.

Current intended behavior:

1. Read `%LOCALAPPDATA%\poodle\auth.json`.
2. If `AccessToken` exists, try it.
3. If it is valid, keep using it.
4. If it is invalid, run device flow.
5. Save the new access token.

If you change this flow:

- verify what the actual saved file contains
- prefer observed runtime behavior over assumptions from docs
- do not add raw `HttpClient` GitHub OAuth code unless there is a strong reason and the existing Octokit path cannot handle the scenario

## When Modifying GitHub Pages Or Workflow Behavior

Be careful not to accidentally change the deployment model.

Current model:

- repository Pages is configured for GitHub Actions-based publishing
- workflow file is `.github/workflows/deploy.yaml`
- workflow uploads the current content root as a Pages artifact
- workflow deploys through `actions/deploy-pages`

If you need to change workflow behavior:

- preserve typed DTO generation
- preserve normalization to avoid unnecessary rewrites
- preserve the workflow filename unless asked otherwise

## When Modifying Config Behavior

`.poodle.json` is intentionally tiny right now.

Current shape:

```json
{
  "repository": "owner/repo"
}
```

If you add new fields:

- keep them project-local
- keep the file human-editable
- avoid turning it into a generic dumping ground

## Recommended Workflow For Agents

When working on this repo:

1. Read the relevant service first.
2. Identify whether the change belongs in:
   - command wiring
   - orchestration
   - GitHub auth/API logic
   - local git logic
   - workflow generation
   - HTML rewriting
   - config/model logic
3. Change the narrowest layer that owns the behavior.
4. Build the solution.
5. Mention any end-to-end limitations if GitHub interaction was not exercised.

## Current Gaps

These are not necessarily bugs, just boundaries of the current MVP:

- only one command exists
- no automated test project yet
- README still describes the old template repository, not the real CLI
- auth persistence is pragmatic rather than deeply sophisticated
- end-to-end publish success still depends on live GitHub state and permissions

If you work on these areas, keep the project focused. Avoid turning “MVP cleanup” into a full platform redesign.
