using System.CommandLine;
using Poodle.Cli.Services;

namespace Poodle.Cli.Commands;

internal sealed class PublishCommand
{
    private readonly PublishService _publishService;

    public PublishCommand(PublishService publishService)
    {
        _publishService = publishService;
    }

    public Command Create()
    {
        var command = new Command("publish", "Publish the current working directory to GitHub Pages.");

        command.SetAction((_, cancellationToken) =>
            _publishService.RunAsync(Environment.CurrentDirectory, cancellationToken));

        return command;
    }
}
