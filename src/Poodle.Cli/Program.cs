using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Poodle.Cli.Commands;
using Poodle.Cli.Services;
using Spectre.Console;

var services = new ServiceCollection()
    .AddSingleton<IAnsiConsole>(AnsiConsole.Console)
    .AddSingleton<GitHubAuthenticator>()
    .AddSingleton<GitHubRepositoryServiceFactory>()
    .AddSingleton<GitRepositoryService>()
    .AddSingleton<HtmlBaseTagService>()
    .AddSingleton<WorkflowFileService>()
    .AddTransient<PublishService>()
    .AddTransient<PublishCommand>()
    .BuildServiceProvider();

var rootCommand = new RootCommand("Publish static files in the current git repository to GitHub Pages.");
rootCommand.Subcommands.Add(services.GetRequiredService<PublishCommand>().Create());

return await rootCommand.Parse(args).InvokeAsync();
