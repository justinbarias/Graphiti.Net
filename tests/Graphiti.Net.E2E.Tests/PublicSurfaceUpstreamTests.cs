// The names and the defaults of the public surface against upstream (plan 0004, layer 9, packet P9d, fix round).
// port/tools/record_public_surface.py read upstream with inspect.signature and the pydantic field lists and wrote
// values/public_surface.json. This test reads the public types with reflection. Each upstream method, parameter, and
// field has its member under the name of rule R2, with the same default, or a difference that the design sets. An
// upstream name with no member and no listed difference fails the test.
namespace Graphiti.E2E.Tests;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Graphiti.Internal;

public sealed class PublicSurfaceUpstreamTests
{
    private const string NoDriver = "The facade: the per-call driver parameter of four upstream calls has no public form, because the driver contract is internal.";
    private const string TwoSagaProperties = "Notes on the calls: saga: str | SagaNode becomes two properties, Saga and SagaNode.";
    private const string AddTripletRow = "The facade table: AddTripletAsync(EntityNode source, EntityEdge edge, EntityNode target, AddTripletOptions? options).";
    private const string ResultNames = "Public records, row Results: AddEpisodeResult, AddEpisodeBulkResult, AddTripletResult.";
    private const string IngestionTable = "Options, the table of ConcurrentIngestionOptions: the option of each upstream field.";

    /// <summary>
    /// The differences that the design sets, by upstream path. <c>Type.method</c> is a method, <c>Type.method(p)</c> a
    /// parameter, <c>Model.field</c> a field, and a path that ends in <c>=</c> a default. The target is the public
    /// member (<c>Type.Property</c> or a parameter name), or null for no public form.
    /// </summary>
    private static readonly Dictionary<string, (string? Target, string Clause)> Differences = new(StringComparer.Ordinal)
    {
        ["Graphiti.close"] = (null, "The facade, Lifetime: close has no member on IGraphiti. The container disposes Graphiti."),
        ["Graphiti.search_"] = ("AdvancedSearchAsync", "Rule R2 and the facade table: search_ is AdvancedSearchAsync."),
        ["Graphiti.token_tracker"] = ("TokenUsage", "The facade table: token_tracker is ITokenUsageTracker TokenUsage { get; }."),
        ["Graphiti.add_episode(episode_body)"] = ("EpisodeInput.Content", "Notes on the calls: EpisodeInput holds Name, Content, SourceDescription, ReferenceTime, Source, and Uuid."),
        ["Graphiti.add_episode(saga)"] = ("AddEpisodeOptions.Saga", TwoSagaProperties),
        ["Graphiti.add_episode_bulk(bulk_episodes)"] = ("episodes", "The facade table: AddEpisodeBulkAsync(IReadOnlyList<EpisodeInput> episodes, AddEpisodeBulkOptions? options)."),
        ["Graphiti.add_episode_bulk(saga)"] = ("AddEpisodeBulkOptions.Saga", TwoSagaProperties),
        ["Graphiti.add_triplet(source_node)"] = ("source", AddTripletRow),
        ["Graphiti.add_triplet(target_node)"] = ("target", AddTripletRow),
        ["Graphiti.search(driver)"] = (null, NoDriver),
        ["Graphiti.search_(driver)"] = (null, NoDriver),
        ["Graphiti.retrieve_episodes(driver)"] = (null, NoDriver),
        ["Graphiti.build_communities(driver)"] = (null, NoDriver),
        ["Graphiti.search_(config)="] = (null, "Notes on the calls: AdvancedSearchAsync has no default for config. The host names a recipe from SearchRecipes."),
        ["Graphiti.__init__(uri)"] = ("Neo4jOptions.Uri", "Options: Neo4jOptions has Uri, User, Password, and Database."),
        ["Graphiti.__init__(user)"] = ("Neo4jOptions.User", "Options: Neo4jOptions has Uri, User, Password, and Database."),
        ["Graphiti.__init__(password)"] = ("Neo4jOptions.Password", "Options: Neo4jOptions has Uri, User, Password, and Database."),
        ["Graphiti.__init__(graph_driver)"] = (null, "Registration: AddNeo4j gives the driver. The driver contract is internal."),
        ["Graphiti.__init__(llm_client)"] = (null, "D28: the chat client comes from the container or from UseChatClient."),
        ["Graphiti.__init__(embedder)"] = (null, "D28: the embedding generator comes from the container or from UseEmbeddingGenerator."),
        ["Graphiti.__init__(cross_encoder)"] = (null, "D28 and D30: UseCrossEncoder, or the default reranker over the chat client."),
        ["Graphiti.__init__(tracer)"] = (null, "Lead choice L9: one ActivitySource named Graphiti.Net."),
        ["Graphiti.__init__(prompt_library)"] = (null, "Prompt overrides and model routing: Prompts.Overrides builds the prompt library."),
        ["Graphiti.__init__(llm_runtime)"] = (null, "Prompt overrides and model routing: Llm.Routes builds the runtime."),
        ["LLMConfig(api_key)"] = (null, "Options: the options hold no API key for a model, because the host builds the model clients."),
        ["LLMConfig(base_url)"] = (null, "Options: the options hold no API key for a model, because the host builds the model clients."),
        ["LLMConfig(temperature)="] = (null, "Options table and lead choice L4: Llm.Temperature is null, and null sends no temperature."),
        ["Namespace(tx)"] = (null, "Namespaces: the tx parameter has no public form, because the transaction type belongs to the internal driver contract."),
        ["Model:AddEpisodeResults"] = ("AddEpisodeResult", ResultNames),
        ["Model:AddBulkEpisodeResults"] = ("AddEpisodeBulkResult", ResultNames),
        ["Model:AddTripletResults"] = ("AddTripletResult", ResultNames),
        ["Model:RawEpisode"] = ("EpisodeInput", "Notes on the calls: EpisodeInput serves add_episode and the RawEpisode of add_episode_bulk."),
        ["RawEpisode.source="] = (null, "Notes on the calls: EpisodeInput holds Source with the default EpisodeType.Message, the default of add_episode."),
        ["Model:ConcurrentIngestionConfig"] = ("ConcurrentIngestionOptions", "Options: ConcurrentIngestion is ConcurrentIngestionOptions for ConcurrentIngestionConfig."),
        ["ConcurrentIngestionConfig.state_store"] = ("ConcurrentIngestionOptions.StateStorePath", IngestionTable),
        ["ConcurrentIngestionConfig.lease_seconds"] = ("ConcurrentIngestionOptions.Lease", IngestionTable),
        ["ConcurrentIngestionConfig.write_margin_seconds"] = ("ConcurrentIngestionOptions.WriteMargin", IngestionTable),
        ["ConcurrentIngestionConfig.poll_interval_seconds"] = ("ConcurrentIngestionOptions.PollInterval", IngestionTable),
        ["ConcurrentIngestionConfig.index_lag_seconds"] = ("ConcurrentIngestionOptions.IndexLag", IngestionTable),
        ["ConcurrentIngestionConfig.before_commit_write"] = (null, "Options: the table of ConcurrentIngestionOptions names eight options and no write hook."),
        ["ConcurrentIngestionConfig.before_enrichment_write"] = (null, "Options: the table of ConcurrentIngestionOptions names eight options and no write hook."),
    };

    private static readonly Assembly[] Public = [typeof(IGraphiti).Assembly, typeof(Neo4jOptions).Assembly];

    private static readonly JsonObject Upstream = JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "public_surface.json"), Encoding.UTF8))!.AsObject();

    [Fact]
    public void Each_upstream_name_has_its_member_and_its_default()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();

        CheckGraphiti(used, failures);
        CheckNamespaces(used, failures);
        CheckSignature("LLMConfig", Upstream["llm_config"]!.AsArray(), [], [typeof(LlmOptions)], used, failures);
        CheckModels(used, failures);

        Assert.True(failures.Count == 0, string.Join('\n', failures));
        var unused = Differences.Keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(unused.Count == 0, "Listed differences that no upstream name uses: " + string.Join(", ", unused));
    }

    [Fact]
    public void Each_recipe_equals_the_recipe_of_Python()
    {
        var failures = new List<string>();
        var recipes = Upstream["recipes"]!.AsObject();
        Assert.Equal(16, recipes.Count);
        foreach (var (name, value) in recipes)
        {
            var property = typeof(SearchRecipes).GetProperty(Pascal(name.ToLowerInvariant()), BindingFlags.Public | BindingFlags.Static);
            if (property is null)
            {
                failures.Add($"recipe {name}: no property");
                continue;
            }

            Compare($"recipe {name}", value, property.GetValue(null), failures);
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void The_ingestion_options_map_to_the_config_of_Python()
    {
        var example = Upstream["ingestion_example"]!.AsObject();
        var options = new ConcurrentIngestionOptions { Window = 0 };
        foreach (var (field, value) in example)
        {
            var target = Differences.TryGetValue($"ConcurrentIngestionConfig.{field}", out var d) ? d.Target!.Split('.')[1] : Pascal(field);
            var property = typeof(ConcurrentIngestionOptions).GetProperty(target)!;
            property.SetValue(options, property.PropertyType == typeof(TimeSpan)
                ? TimeSpan.FromSeconds(value!.GetValue<double>())
                : Convert.ChangeType(value!.GetValue<double>(), property.PropertyType, System.Globalization.CultureInfo.InvariantCulture));
        }

        var config = GraphitiOptionsValidator.ToConfig(options);

        foreach (var (field, value) in example)
        {
            var ported = config.GetType().GetProperty(Pascal(field), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(config);
            Assert.True(Convert.ToDouble(ported, System.Globalization.CultureInfo.InvariantCulture) == value!.GetValue<double>(), $"{field}: {ported} is not {value}");
        }
    }

    [Fact]
    public void A_default_created_at_is_utc_now_of_the_port_clock_cut_to_microseconds()
    {
        // utc_now() is datetime.now(timezone.utc), which has microseconds. The clock gives 100 ns ticks.
        var at = new DateTimeOffset(2031, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1_234_567);
        using (PortServices.Use(time: new FixedClock(at)))
        {
            Node[] nodes =
            [
                new EntityNode { Name = "n", GroupId = "g" },
                new CommunityNode { Name = "n", GroupId = "g" },
                new SagaNode { Name = "n", GroupId = "g" },
                new EpisodicNode { Name = "n", GroupId = "g", Source = EpisodeType.Text, SourceDescription = "s", Content = "c", ValidAt = at },
            ];
            Assert.All(nodes, n => Assert.Equal((at.AddTicks(-7), TimeSpan.Zero), (n.CreatedAt, n.CreatedAt.Offset)));
        }
    }

    private static void CheckGraphiti(HashSet<string> used, List<string> failures)
    {
        foreach (var (name, signature) in Upstream["graphiti"]!.AsObject())
        {
            var path = $"Graphiti.{name}";
            if (name == "__init__")
            {
                CheckSignature(path, signature!["parameters"]!.AsArray(), [], [typeof(GraphitiOptions), typeof(Neo4jOptions)], used, failures);
                continue;
            }

            var method = FindMethod(typeof(IGraphiti), path, name, signature!["async"]!.GetValue<bool>(), used, failures);
            if (method is not null)
            {
                var carriers = method.GetParameters().Select(p => Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType)
                    .Where(t => t == typeof(EpisodeInput) || (t.Namespace == "Graphiti" && t.Name.EndsWith("Options", StringComparison.Ordinal)))
                    .ToList();
                CheckSignature(path, signature["parameters"]!.AsArray(), method.GetParameters(), carriers, used, failures);
            }
        }

        foreach (var name in Upstream["graphiti_properties"]!.AsArray().Select(n => n!.GetValue<string>()))
        {
            var path = $"Graphiti.{name}";
            var target = Differences.TryGetValue(path, out var d) ? Use(path, used, d.Target) : Pascal(name);
            if (target is not null && typeof(IGraphiti).GetProperty(target) is null)
            {
                failures.Add($"{path}: no property {target}");
            }
        }
    }

    private static void CheckNamespaces(HashSet<string> used, List<string> failures)
    {
        foreach (var (name, upstreamMethods) in Upstream["namespaces"]!.AsObject())
        {
            // EntityNodeNamespace is Nodes.Entity, HasEpisodeEdgeNamespace is Edges.HasEpisode (Namespaces: the names mirror upstream).
            var stem = name[..^"Namespace".Length];
            var (holder, member) = stem.EndsWith("Node", StringComparison.Ordinal)
                ? (typeof(INodeNamespaces), stem[..^"Node".Length])
                : (typeof(IEdgeNamespaces), stem[..^"Edge".Length]);
            var type = holder.GetProperty(member)?.PropertyType;
            if (type is null)
            {
                failures.Add($"{name}: no property {holder.Name}.{member}");
                continue;
            }

            foreach (var (methodName, signature) in upstreamMethods!.AsObject())
            {
                var path = $"{name}.{methodName}";
                var method = FindMethod(type, path, methodName, signature!["async"]!.GetValue<bool>(), used, failures);
                if (method is not null)
                {
                    CheckSignature(path, signature["parameters"]!.AsArray(), method.GetParameters(), [], used, failures);
                }
            }
        }
    }

    private static void CheckModels(HashSet<string> used, List<string> failures)
    {
        foreach (var (name, upstreamFields) in Upstream["models"]!.AsObject())
        {
            var typeName = Differences.TryGetValue($"Model:{name}", out var d) ? Use($"Model:{name}", used, d.Target)! : name;
            var type = PublicType(typeName);
            if (type is null || !type.IsPublic)
            {
                failures.Add($"model {name}: no public type {typeName}");
                continue;
            }

            CheckSignature(name, upstreamFields!.AsArray(), [], [type], used, failures);
        }
    }

    /// <summary>Checks each upstream parameter or field: its member by rule R2 or by a listed difference, then its default.</summary>
    private static void CheckSignature(
        string owner, JsonArray upstreamParameters, ParameterInfo[] parameters, List<Type> carriers, HashSet<string> used, List<string> failures)
    {
        var separator = parameters.Length > 0 || owner.StartsWith("Graphiti.", StringComparison.Ordinal) || owner == "LLMConfig" ? "(" : ".";
        foreach (var upstream in upstreamParameters)
        {
            var name = upstream!["name"]!.GetValue<string>();
            var path = separator == "(" ? $"{owner}({name})" : $"{owner}.{name}";
            var namespaceTx = owner.Contains("Namespace.", StringComparison.Ordinal) && name == "tx";
            var key = namespaceTx ? "Namespace(tx)" : path;
            var target = Differences.TryGetValue(key, out var difference) ? Use(key, used, difference.Target) : name;
            if (target is null)
            {
                continue;
            }

            var defaultKey = path + "=";
            var compareDefault = !Differences.ContainsKey(defaultKey) || Use(defaultKey, used, null) is not null;
            var location = Locate(target, parameters, carriers);
            if (location is null)
            {
                failures.Add($"{path}: no member and no listed difference");
                continue;
            }

            if (compareDefault)
            {
                CompareDefault(path, upstream["default"]!.AsObject(), location.Value, failures);
            }
        }
    }

    private static (ParameterInfo? Parameter, PropertyInfo? Property)? Locate(string target, ParameterInfo[] parameters, List<Type> carriers)
    {
        if (target.Contains('.', StringComparison.Ordinal))
        {
            var parts = target.Split('.');
            var property = PublicType(parts[0])?.GetProperty(parts[1]);
            return property is null ? null : (null, property);
        }

        if (parameters.FirstOrDefault(p => p.Name == Camel(target)) is { } parameter)
        {
            return (parameter, null);
        }

        foreach (var carrier in carriers)
        {
            if (carrier.GetProperty(Pascal(target)) is { } property)
            {
                return (null, property);
            }
        }

        return null;
    }

    private static void CompareDefault(string path, JsonObject expected, (ParameterInfo? Parameter, PropertyInfo? Property) location, List<string> failures)
    {
        var required = expected.ContainsKey("required");
        if (location.Parameter is { } parameter)
        {
            if (required != !parameter.HasDefaultValue)
            {
                failures.Add($"{path}: required is {required} upstream, and the parameter {parameter.Name} has a default: {parameter.HasDefaultValue}");
            }
            else if (!required)
            {
                Compare(path, expected["value"], parameter.DefaultValue, failures);
            }

            return;
        }

        var property = location.Property!;
        var isRequired = property.GetCustomAttribute<RequiredMemberAttribute>() is not null;
        if (required != isRequired)
        {
            failures.Add($"{path}: required is {required} upstream and {isRequired} for {property.DeclaringType!.Name}.{property.Name}");
            return;
        }

        var owner = property.ReflectedType!;
        if (required || owner.IsAbstract)
        {
            return;
        }

        if (expected["factory"] is { } factory)
        {
            var values = Enumerable.Range(0, 20).Select(_ => property.GetValue(Activator.CreateInstance(owner))).ToList();
            var ok = factory.GetValue<string>() switch
            {
                "uuid4" => values.All(v => v is string s && Guid.TryParse(s, out var g) && g.Version == 4) && values.Distinct().Count() == values.Count,
                "utc_now" => values.All(v => v is DateTimeOffset t && t.Offset == TimeSpan.Zero && t.Ticks % 10 == 0
                    && (DateTimeOffset.UtcNow - t).Duration() < TimeSpan.FromMinutes(1)),
                _ => false,
            };
            if (!ok)
            {
                failures.Add($"{path}: the default of {owner.Name}.{property.Name} is not the factory {factory}");
            }

            return;
        }

        Compare(path, expected["value"], property.GetValue(Activator.CreateInstance(owner)), failures);
    }

    /// <summary>Compares a value that Python recorded with a public value.</summary>
    private static void Compare(string path, JsonNode? expected, object? actual, List<string> failures)
    {
        switch (expected)
        {
            case null:
                if (actual is not null)
                {
                    failures.Add($"{path}: {actual} is not None");
                }

                break;
            case JsonObject e when e.ContainsKey("enum"):
                var member = Pascal(e["member"]!.GetValue<string>());
                if (actual is not Enum enumValue || enumValue.GetType().Name != e["enum"]!.GetValue<string>() || enumValue.ToString() != member)
                {
                    failures.Add($"{path}: {actual} is not {e["enum"]}.{member}");
                }

                break;
            case JsonObject e when actual is IDictionary or IEnumerable<KeyValuePair<string, object?>>:
                var entries = ((IEnumerable)actual).Cast<object>().Count();
                if (e.Count != 0 || entries != 0)
                {
                    failures.Add($"{path}: the dictionary does not equal {e.ToJsonString()}");
                }

                break;
            case JsonObject e:
                if (actual is null)
                {
                    failures.Add($"{path}: null is not {e.ToJsonString()}");
                    break;
                }

                foreach (var (key, value) in e)
                {
                    var property = actual.GetType().GetProperty(Pascal(key));
                    if (property is null)
                    {
                        failures.Add($"{path}.{key}: no property {actual.GetType().Name}.{Pascal(key)}");
                        continue;
                    }

                    Compare($"{path}.{key}", value, property.GetValue(actual), failures);
                }

                break;
            case JsonArray e:
                if (actual is not IEnumerable items || actual is string)
                {
                    failures.Add($"{path}: {actual} is not a list");
                    break;
                }

                var list = items.Cast<object?>().ToList();
                if (list.Count != e.Count)
                {
                    failures.Add($"{path}: {list.Count} items, upstream {e.Count}");
                    break;
                }

                for (var i = 0; i < list.Count; i++)
                {
                    Compare($"{path}[{i}]", e[i], list[i], failures);
                }

                break;
            case JsonValue e when e.TryGetValue<bool>(out var flag):
                if (actual is not bool b || b != flag)
                {
                    failures.Add($"{path}: {actual} is not {flag}");
                }

                break;
            case JsonValue e when e.TryGetValue<string>(out var text):
                if (actual is not string s || s != text)
                {
                    failures.Add($"{path}: {actual} is not '{text}'");
                }

                break;
            case JsonValue e:
                var number = e.GetValue<double>();
                double? got = actual switch
                {
                    TimeSpan t => t.TotalSeconds,
                    int or long or float or double => Convert.ToDouble(actual, System.Globalization.CultureInfo.InvariantCulture),
                    _ => null,
                };
                if (got != number)
                {
                    failures.Add($"{path}: {actual} is not {number}");
                }

                break;
        }
    }

    private static MethodInfo? FindMethod(Type type, string path, string name, bool isAsync, HashSet<string> used, List<string> failures)
    {
        var target = Differences.TryGetValue(path, out var d) ? Use(path, used, d.Target) : Pascal(name) + (isAsync ? "Async" : string.Empty);
        if (target is null)
        {
            return null;
        }

        var methods = type.GetMethods().Where(m => m.Name == target).ToList();
        if (methods.Count != 1)
        {
            failures.Add($"{path}: {methods.Count} methods {type.Name}.{target}");
            return null;
        }

        if (isAsync && !typeof(Task).IsAssignableFrom(methods[0].ReturnType))
        {
            failures.Add($"{path}: {target} does not return a task");
        }

        return methods[0];
    }

    private static Type? PublicType(string name) =>
        Public.Select(a => a.GetType($"Graphiti.{name}")).FirstOrDefault(t => t is not null);

    private static string? Use(string key, HashSet<string> used, string? target)
    {
        used.Add(key);
        return target;
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private static string Pascal(string snake) =>
        string.Concat(snake.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Camel(string snake)
    {
        var pascal = Pascal(snake);
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }
}
