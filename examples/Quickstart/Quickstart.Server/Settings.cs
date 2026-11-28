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
    string RepositoryRoot)
{
    /// <summary>Gets the path of the folder for the downloaded Act and for sections.json.</summary>
    public string DataDirectory => Path.Combine(RepositoryRoot, "examples", "Quickstart", "data");

    /// <summary>Gets the path of sections.json.</summary>
    public string SectionsFile => Path.Combine(DataDirectory, "sections.json");

    /// <summary>Loads the settings. It throws one clear sentence if the OpenAI key is missing.</summary>
    /// <returns>The settings.</returns>
    public static Settings Load()
    {
        var root = FindRepositoryRoot();
        var file = ReadEnvFile(Path.Combine(root, ".env"));

        string Get(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
            : file.TryGetValue(name, out var fromFile) && fromFile.Length > 0 ? fromFile
            : fallback;

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
            root);
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
