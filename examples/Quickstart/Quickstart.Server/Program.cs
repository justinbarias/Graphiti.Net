using Quickstart;

// The commands: split, ingest, reset, and serve (the default).
//   dotnet run --project examples/Quickstart/Quickstart.Server -- ingest --file <docx> [--limit N] [--resume] [--window N]
//   dotnet run --project examples/Quickstart/Quickstart.Server -- serve
var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "serve";
var rest = args.Length > 0 && command == args[0] ? args[1..] : args;

string? Option(string name) =>
    Array.IndexOf(rest, name) is var i and >= 0 && i + 1 < rest.Length ? rest[i + 1] : null;

try
{
    switch (command)
    {
        case "split":
            Commands.Split(RequireFile());
            break;
        case "ingest":
            int? limit = Option("--limit") is { } text ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : null;
            var window = Option("--window") is { } w ? int.Parse(w, System.Globalization.CultureInfo.InvariantCulture) : 3;
            await Commands.IngestAsync(Settings.Load(), RequireFile(), limit, rest.Contains("--resume"), window);
            break;
        case "reset":
            await Commands.ResetAsync(Settings.Load());
            break;
        case "serve":
            await Server.RunAsync(Settings.Load(), rest);
            break;
        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Use split, ingest, reset, or serve.");
            return 2;
    }

    return 0;
}
catch (QuickstartException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

string RequireFile() =>
    Option("--file") ?? throw new QuickstartException("Give the Act with --file <path to the .docx file>.");
