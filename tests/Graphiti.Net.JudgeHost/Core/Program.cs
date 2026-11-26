using System.Text;
using Graphiti.JudgeHost.Core;

// The .NET judge host. It reads one case as a JSON line from standard input and prints one
// JSON result line. port/judge/README.md defines the protocol, and HostLoop answers each line.
// Nothing but result lines may reach standard output.

if (args is ["--selfcheck", var vectorsPath])
{
    return JudgeEmbedding.SelfCheck(vectorsPath);
}

var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
Console.SetOut(Console.Error);
using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);

while (await stdin.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    await stdout.WriteLineAsync(await HostLoop.AnswerAsync(line));
}

return 0;
