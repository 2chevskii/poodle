# .NET Project Template

This repository is a minimal starting point for future .NET projects.

Its purpose is to provide a clean baseline with shared solution-level configuration already in place, so new projects can start from a consistent structure instead of rebuilding the same setup each time.

## What This Template Includes

- `Solution.slnx` as the solution entry point
- `src/Project/Project.csproj` as the initial SDK-style project
- `Directory.Build.props` for shared MSBuild settings
- `Directory.Packages.props` for centralized NuGet package version management
- `global.json` to pin the .NET SDK version
- Standard repository files such as `.editorconfig`, `.gitignore`, and `LICENSE`

## Intended Use

Use this template when creating a new .NET repository and you want:

- a small, predictable starting structure
- centralized build and package configuration
- nullable reference types and implicit usings enabled by default
- a repository that can be expanded without carrying unnecessary boilerplate

## Current Baseline

The template is intentionally minimal. It does not assume any specific application type such as web API, worker service, library, or desktop app. The expectation is that you copy or generate from this repository, then rename `Project`, add the required projects, and shape the solution around the new project's needs.
