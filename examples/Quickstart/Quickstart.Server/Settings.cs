namespace Quickstart;

/// <summary>
/// The settings of the example. They come from the environment variables, and from the file <c>.env</c> at the
/// repository root if it exists. An environment variable wins over the file. The code never prints a key or a
/// password, and never writes one to a file.
/// </summary>
internal sealed record Settings(
    string OpenAiApiKey,
    string Neo4jUri,
    string Neo4jUser,
    string Neo4jPassword,
    string ChatModel,
    string EmbeddingModel,
    string GroupId,
    DateOnly BillAssentDate,
    bool BillAssentIsPlaceholder,
    string RepositoryRoot)
{
    /// <summary>Gets the path of the folder for the downloaded Act and for sections.json.</summary>
    public string DataDirectory => Path.Combine(RepositoryRoot, "examples", "Quickstart", "data");

    /// <summary>Gets the path of the sections file of this group: one file for each group id.</summary>
    public string SectionsFile => Path.Combine(DataDirectory, $"sections.{SafeGroupName(GroupId)}.json");

    /// <summary>Shows only the settings that are not secret. The generated text of a record would show the key and the password.</summary>
    /// <param name="builder">The builder of the text.</param>
    /// <returns>True.</returns>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"Neo4jUri = {Neo4jUri}, ChatModel = {ChatModel}, EmbeddingModel = {EmbeddingModel}, GroupId = {GroupId}");
        return true;
    }

    private static string SafeGroupName(string groupId) =>
        new([.. groupId.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);

    /// <summary>Loads the settings. It throws one clear sentence if the OpenAI key is missing.</summary>
    /// <returns>The settings.</returns>
    public static Settings Load()
    {
        var root = FindRepositoryRoot();
        var file = ReadEnvFile(Path.Combine(root, ".env"));
        string Get(string name, string fallback) => Read(file, name, fallback);

        var key = Get("OPENAI_API_KEY", string.Empty);
        if (key.Length == 0)
        {
            throw new QuickstartException(
                "The variable OPENAI_API_KEY is not set. Set it in the environment or in the file .env at the repository root.");
        }

        return new Settings(
            key,
            Get("NEO4J_URI", "bolt://localhost:7687"),
            Get("NEO4J_USER", "neo4j"),
            Get("NEO4J_PASSWORD", "password"),
            Get("QUICKSTART_CHAT_MODEL", "gpt-6-luna"),
            Get("QUICKSTART_EMBEDDING_MODEL", "text-embedding-3-small"),
            Get("QUICKSTART_GROUP_ID", "online-safety-act-2021"),
            ParseDate(BillAssentVariable, Get(BillAssentVariable, BillAssentPlaceholder)),
            Get(BillAssentVariable, string.Empty).Length == 0,
            root);
    }

    /// <summary>Loads only the day of the Royal Assent of the Bill. The command split needs no key and no database.</summary>
    /// <returns>The day.</returns>
    public static DateOnly LoadBillAssentDate() =>
        ParseDate(BillAssentVariable, Read(ReadEnvFile(Path.Combine(FindRepositoryRoot(), ".env")), BillAssentVariable, BillAssentPlaceholder));

    private const string BillAssentVariable = "QUICKSTART_BILL_ASSENT_DATE";

    private static string Read(Dictionary<string, string> file, string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
        : file.TryGetValue(name, out var fromFile) && fromFile.Length > 0 ? fromFile
        : fallback;

    /// <summary>
    /// The placeholder for the day of the Royal Assent of the Digital Duty of Care Bill 2026. Both Houses passed the
    /// Bill on this day, and the Bill had no assent when this example was written. Set the real day with the variable
    /// QUICKSTART_BILL_ASSENT_DATE.
    /// </summary>
    public const string BillAssentPlaceholder = "2026-11-25";

    private static DateOnly ParseDate(string name, string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
        {
            throw new QuickstartException($"The variable {name} is not a date of the form 2026-11-25.");
        }

        return date;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphiti.Net.slnx")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new QuickstartException("Run the example from inside the repository: no Graphiti.Net.slnx file was found.");
    }

    private static Dictionary<string, string> ReadEnvFile(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            var name = line[..equals].Trim();
            if (name.StartsWith("export ", StringComparison.Ordinal))
            {
                name = name[7..].Trim();
            }

            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            values[name] = value;
        }

        return values;
    }
}

/// <summary>An error with a message that is safe to show to the person who runs the example.</summary>
internal sealed class QuickstartException(string message) : Exception(message);
