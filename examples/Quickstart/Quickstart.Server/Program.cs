using Quickstart;

// The commands: split, ingest, reset, and serve (the default).
//   dotnet run --project examples/Quickstart/Quickstart.Server -- ingest --file <docx> [--limit N] [--sections 6,13,88] [--resume] [--window N]
//   dotnet run --project examples/Quickstart/Quickstart.Server -- serve
var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "serve";
var rest = args.Length > 0 && command == args[0] ? args[1..] : args;

string? Option(string name) =>
    Array.IndexOf(rest, name) is var i and >= 0 && i + 1 < rest.Length ? rest[i + 1] : null;

try
{
    if (command != "serve")
    {
        // An option that the command does not know stops the run. A typing error, for example --sections=6, must not
        // start a full ingestion.
        string[] known = ["--file", "--limit", "--sections", "--window", "--resume"];
        if (rest.FirstOrDefault(a => a.StartsWith("--", StringComparison.Ordinal) && !known.Contains(a, StringComparer.Ordinal)) is { } unknown)
        {
            throw new QuickstartException($"Unknown option '{unknown}'. Give the value of an option after a space, for example --sections 6,13.");
        }
    }

    switch (command)
    {
        case "split":
            Commands.Split(RequireFile());
            break;
        case "ingest":
            int? limit = Option("--limit") is { } text ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : null;
            var window = Option("--window") is { } w ? int.Parse(w, System.Globalization.CultureInfo.InvariantCulture) : 3;
            var sections = Option("--sections")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rest.Contains("--sections") && sections is not { Length: > 0 })
            {
                throw new QuickstartException("Give the section numbers after --sections, for example --sections 6,13.");
            }

            await Commands.IngestAsync(Settings.Load(), RequireFile(), limit, rest.Contains("--resume"), window, sections);
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
