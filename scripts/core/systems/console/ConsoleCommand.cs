using System;
using System.Threading.Tasks;

namespace Waterjam.Core.Systems.Console;

public class ConsoleCommand
{
    public string Name { get; }
    public string Description { get; }
    public string Usage { get; }
    private readonly Func<string[], Task<bool>> executor;

    public ConsoleCommand(string name, string description, string usage, Func<string[], Task<bool>> executor)
    {
        Name = name;
        Description = description;
        Usage = usage;
        this.executor = executor;
    }

    public async Task<bool> Execute(string[] args)
    {
        return await executor(args);
    }
}