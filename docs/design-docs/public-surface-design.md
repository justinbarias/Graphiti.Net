# Public surface design

Status: approved by the maintainer on 2026-11-25. Plan 0003, task 2. The maintainer decided D24 to D35 on that day, each with a question that listed the options, and accepted 12 lead choices.
One adversarial review ran after the approval. On 2026-11-26 the maintainer decided D36, changed the lifetime part of D24, and accepted lead choice L13.
This page designs the public .NET surface of Graphiti.Net: the `AddGraphiti` registration, the options, the `Microsoft.Extensions.AI` adapter, and the cross-encoder over `IChatClient`.
It holds no product code. The signatures here are the contract for the code that plan 0004 writes.

## Requirements

| Requirement | Source |
| --- | --- |
| A .NET host registers Graphiti with dependency injection, sets options, and passes a `CancellationToken` to each async call. | [The product spec](../product-specs/spec.md), M1 |
| Internal modules stay a structural port. Only the public surface is designed for .NET. | Decision D4 |
| The core references no provider package and no `Neo4j.Driver` type. | [ARCHITECTURE.md](../../ARCHITECTURE.md), boundaries |
| The judge reaches model calls through a judge `IChatClient` under the product adapter. | [The judge README](../../port/judge/README.md), capture seams |
| The LLM contract is upstream's base client path. The schema text stays at the end of the prompt. | Decision D17 |
| Ported code runs on one exclusive scheduler and reads the logger, the clock, and the random source from `PortServices`. | Decisions D22 and D23 |
| A public type does not carry Python semantics that surprise a .NET caller. | Decision D25 below |

## Layers

```text
host application
  |  IGraphiti, public records, options           public, designed for .NET
  v
GraphitiFacade (Graphiti.Internal)                 lead code: maps types, enters the scheduler
  |  ported types, Python signatures
  v
GraphitiCore and the ported modules                internal, structural port, on the exclusive scheduler
  |                    |                     |                    |
  v                    v                     v                    v
ChatClientLlmClient  EmbeddingGenerator-   OpenAIRerankerClient driver contract (internal)
                     Embedder              on LogProbabilityChat  |
  |  thread pool       |  thread pool        |  thread pool       v
  v                    v                     v                  Graphiti.Net.Neo4j
IChatClient          IEmbeddingGenerator   IChatClient and      Neo4j.Driver
                                           IChatLogProbabilities
```

The facade and the adapters are lead code in `src/Graphiti.Net/Internal/`. They have no upstream module.
There are four: the chat adapter, the embedding adapter, the chat seam of the reranker, and a bridge from a host `ICrossEncoder` to the ported `CrossEncoderClient`.
The reranker itself is the ported unit `cross_encoder.openai_reranker_client`. It stays in the queue.
The judge host binds to `GraphitiCore` and the ported modules. It does not go through `IGraphiti`.

## Decisions

Each table gives the options that the maintainer saw. The first row is the choice.

### D24. The facade is the interface `IGraphiti`

| Option | For | Against |
| --- | --- | --- |
| Public interface `IGraphiti`, internal implementation, resolved from DI only. Chosen. | Smallest public commitment. A host can fake it in tests. | No construction without a service collection. |
| Public `sealed class GraphitiClient` with a static factory | Construction without DI, as in the Python quickstart. | A host cannot fake it. Each constructor is a commitment. |
| Both | Most flexible. | Largest surface to keep stable from the first preview. |

The ported class of `graphiti.py` is `internal sealed class GraphitiCore`. Upstream names it `Graphiti`.
Inside a namespace that starts with `Graphiti`, a type named `Graphiti` takes the name from the namespace.
Each qualified name such as `Graphiti.Search.X` then fails to compile.
The module-level functions of `graphiti.py` go to the static class `GraphitiModule`, by rule R2.

### D25. Public data types are separate records

| Option | For | Against |
| --- | --- | --- |
| New immutable records with `DateTimeOffset` and read-only collections. The facade maps them to and from the ported classes. Chosen. | Normal .NET equality. Port phase 9 can change internals with no public break. | Mapping code for each type, which the lead writes and tests. |
| Make the ported classes public | No mapping code. | `PyDateTime` becomes public. An edge does not equal itself (GAPS U12), so `List.Contains` fails. Each cleanup is a breaking change. |
| Read-only interfaces on the ported classes | No copy on output. | Inputs still need public types. Each ported class needs a second date property. |

This decision has one consequence, which is D35 below.

### D26. A custom type is a descriptor with a builder

Upstream takes `dict[str, type[BaseModel]]` and reads four things from each class: the docstring, the field list, the JSON schema text, and validation of the model reply.

| Option | For | Against |
| --- | --- | --- |
| A public `GraphTypeDefinition` built in code from a closed set of field kinds. Chosen. | No reflection. The schema text equals pydantic's for each field kind. Safe for trimming. | A host writes the fields by hand. |
| A C# record with `[Description]` attributes, read by reflection | Familiar to .NET users. | Each C# type needs a rule that gives pydantic's schema text. Trimming needs annotations. |
| Both in M1 | Best for hosts. | More design and test work before the first preview. |

A typed helper `GraphTypeDefinition.From<T>()` can come after M1 on top of the descriptor.

### D27. The M1 public surface

The maintainer chose all four extra surfaces. The M1 public surface is:

- The 13 public members of the upstream class: 11 graph calls, `close`, and `token_tracker`.
- The node and edge namespaces: 75 calls (37 on nodes, 38 on edges).
- Prompt overrides and model routing.
- The options of concurrent ingestion.

The driver contract stays internal in M1. `Graphiti.Net.Neo4j` reaches it through `InternalsVisibleTo` (rule R3). It becomes public when a driver outside this repository needs it.
The two write hooks of concurrent ingestion (`before_commit_write`, `before_enrichment_write`) stay internal, because they expose `ChangeSet` and `AdmittedUnit`.

Correction: the question to the maintainer said "13 facade calls" and "about 60" namespace methods. The counts above are from the source.

### D28. Clients come from DI, and the builder can replace them

| Option | For | Against |
| --- | --- | --- |
| Resolve the unkeyed `IChatClient` and `IEmbeddingGenerator` that the host registered. The builder can replace each one. Chosen. | Zero wiring for the common host. A host can still give Graphiti its own client. | A host with two chat clients must state which one Graphiti gets. |
| Explicit only | Nothing is taken from DI without a statement. | More lines in each host. |
| A fixed service key `"graphiti"` | No builder methods. | A host with one client registers it a second time. |

### D29. One chat client and two model ids

| Option | For | Against |
| --- | --- | --- |
| One `IChatClient`. `Llm.Model` and `Llm.SmallModel` go into `ChatOptions.ModelId`. Chosen. | Closest to upstream `LLMConfig`. | A client that ignores `ChatOptions.ModelId` uses one model for both sizes. |
| Two clients, medium and small | Works for each provider. | The host wires two clients. |
| Both | Covers each provider. | Two ways to do one thing. |

Model routing (D27) covers the provider that needs a second client: a route can name a keyed `IChatClient`.

### D30. The reranker reads log probabilities through a public hook

Upstream's `OpenAIRerankerClient` asks for `logprobs` and `top_logprobs` and ranks by the probability of the token `True`.
`Microsoft.Extensions.AI.Abstractions` has no member for token log probabilities. They are reachable only through a provider type.

| Option | For | Against |
| --- | --- | --- |
| The ported logic runs on `IChatClient`. A public interface, `IChatLogProbabilities`, sets the request and reads the response. The documents give the OpenAI implementation. Chosen. | The core keeps no provider reference. Packages stay at two. | A host copies about 20 lines to use a cross-encoder recipe with OpenAI. |
| The hook and a `Graphiti.Net.OpenAI` package | Works out of the box for OpenAI. | A third package to version and publish. |
| Port the logic of upstream's Gemini reranker now: a score prompt for each passage | Works with each provider with no hook. | It moves a module that decision D10 defers to M5. |

This amends the last sentence of lead choice 2 in [DECISIONS.md](../../port/DECISIONS.md). With no hook, the first rerank call fails with a clear error for each provider.

### D31. A delegate classifies provider errors

Upstream retries a model call for `RateLimitError`, `EmptyResponseError`, `JSONDecodeError`, and an `httpx.HTTPStatusError` with a 5xx status (`llm_client/client.py:61`).

| Option | For | Against |
| --- | --- | --- |
| A public option `Func<Exception, ModelCallFailure>`. The default knows `HttpRequestException` with status 429 or 5xx. Chosen. | No new dependency. Each provider SDK is reachable with one host function. | Without the function, Graphiti does not retry an OpenAI 429. The OpenAI SDK retries it on its own. |
| The core references `System.ClientModel` and reads `ClientResultException.Status` | OpenAI and Azure work with no host code. | One more dependency in the core. |
| No classification | Simplest. | It drops upstream's retry for rate limits and server errors. |

### D32. The decisions client is ported onto `HttpClient` and stays internal in M1

Lead choice 3 said that the OpenAI decisions client is ported onto `IChatClient`. That is not possible.
The client posts its own request body to `{base_url}/decisions` with `httpx` and never calls chat completions (`llm_client/openai_decisions_client.py:191`).

| Option | For | Against |
| --- | --- | --- |
| Port the module as it is onto `HttpClient`. Internal in M1. A public route target with the SDK's `DecisionsClient` can come after M1. Chosen. | The request body, the retry loop, and the error tests stay equal to Python. The 14 pytest tests port onto a stub `HttpMessageHandler`. | A host cannot use the client in M1. |
| A public hook `IDecisionsTransport`, with the SDK code in the documents | No provider reference in the core. | The request function differs from the Python structure. |
| A `Graphiti.Net.OpenAI` package on `DecisionsClient` | OpenAI works with one call. | A third package, on an API that the SDK marks experimental. |
| The core references the `OpenAI` package | Least code. | It breaks the boundary rule of ARCHITECTURE.md and decision D3. |

The judge has no scenario for this client (GAPS J13, debt T12), so its unit stops at `tests_pass`.

### D33. Host services run on the thread pool

Rulebook open item O3 asks what host code can expect from the exclusive scheduler of D22.

| Option | For | Against |
| --- | --- | --- |
| The adapters call each host service on the thread pool and return to the scheduler with the result. Short delegates run in line on the scheduler. Chosen. | Host services get normal .NET rules. A host client that blocks cannot stall or deadlock Graphiti. | One thread-pool hop for each model call. |
| All host code on the scheduler | No hop. | A host client that blocks on a task can deadlock all Graphiti work in the process. |
| All host code on the thread pool | Host code never sees the scheduler. | The ported call sites of the delegates need a wrapper, which differs from the Python structure. |

The section "Host code and the scheduler" lists each service and each delegate.

### D34. Options for two settings, environment variables for ten

Upstream reads 11 settings from the environment one time at import. Rule R17 ports them as static read-only fields.

| Option | For | Against |
| --- | --- | --- |
| `GraphitiOptions.MaxCoroutines` and `Embedding.Dimensions` are options. The other values stay environment variables in M1. Chosen. | No rule changes. | A host sets process environment variables for chunk sizes and index names. |
| Options for all 11, set one time through `PortServices` | Idiomatic. | It changes rule R17 and decision D23. The values stay process-wide. |
| Environment only | Closest to the source. | A host cannot set the embedding size in code. |

This amends the answer of GAPS G10, which says that the public surface offers options for each value.
Ten of the 11 values stay environment variables only: `SEMAPHORE_LIMIT`, `USE_PARALLEL_RUNTIME`, `CHUNK_TOKEN_SIZE`, `CHUNK_OVERLAP_TOKENS`, `CHUNK_MIN_TOKENS`, `CHUNK_DENSITY_THRESHOLD`, and the four index names `ENTITY_INDEX_NAME`, `EPISODE_INDEX_NAME`, `COMMUNITY_INDEX_NAME`, and `ENTITY_EDGE_INDEX_NAME`.
The eleventh, `EMBEDDING_DIM`, is the default of `Embedding.Dimensions`. `MaxCoroutines` is a constructor argument upstream, not an environment read. It replaces `SEMAPHORE_LIMIT` for one instance.
The question to the maintainer said "nine". The count from the source is ten.
An empty search query uses a zero vector with the length of `EMBEDDING_DIM`, not of the option (`search/search.py:153`). Upstream has the same split. A host that changes the size sets both values.

### D35. Ported code moves to the namespace `Graphiti.Core`

Rule R1 puts `graphiti_core/nodes.py` in the namespace `Graphiti`. D25 puts a public record `EntityNode` in the root namespace too.
One assembly cannot hold two types with one full name, so one side must move.

| Option | For | Against |
| --- | --- | --- |
| Ported code uses `Graphiti.Core` and `Graphiti.Core.<Pkg>`. Public types use `Graphiti`. Chosen. | The host sees clean names in the root namespace. `graphiti_core` maps to `Graphiti.Core` by name. | Rule R1 changes before the pilot. No ported code exists, so no unit is ported again. Each file that names a ported type must place its `using` directives inside the namespace. The facade needs an alias for each pair of a public and a ported type. |
| Public records use a sub-namespace such as `Graphiti.Graph` | Rule R1 stays. | The internal types keep the best names. Facade code needs an alias for each pair. |
| Public records get a suffix, for example `EntityNodeRecord` | No namespace change. | Each public name is longer for no reason that a host sees. |

The ported driver code keeps `Graphiti.Neo4j`. That namespace is also a child of `Graphiti`, so its files follow the same placement rule.
The exception classes are the one family of ported types that are public as they are. They use the namespace `Graphiti`.

Two details came after the maintainer's answer. The lead added them to rule R1, and they are named here so that the maintainer can reject them:

- The ported files move to the folder `src/Graphiti.Net/Core/`, so that each folder equals its namespace. The public types take the root folder.
- C# searches the parent namespace `Graphiti` before a `using` directive at the top of a file. A file in `Graphiti.Core` with `using Graphiti.Core.Search;` at the top binds `SearchConfig` to the public record. With the directive after the file-scoped namespace line, the name binds to the ported type. Test projects with the .NET 10 SDK showed both results on 2026-11-25.
- A wrong placement does not always fail the build. A constructor call gave `error CS1503`. `typeof(SearchConfig)` and `value is SearchFilters` compiled and bound to the public record.
- The same holds in each namespace below `Graphiti`: the Neo4j project, the judge host, and the test projects. Rule R1 states the placement for all of them, and rulebook open item O8 asks for a build check.
- Inside `namespace Graphiti.Neo4j.<Sub>;` the name `Neo4j` is `Graphiti.Neo4j`, so `using Neo4j.Driver;` fails with `error CS0234`. Rule R1 tells a driver file to write `using global::Neo4j.Driver;`.

### D36. A naive date is read as UTC and saved as a zoned value

Decided on 2026-11-26, after the adversarial review. It is an ask-first item, because it changes a stored value shape.

Python can write a node or an edge with a naive date, and Neo4j stores it as a local value. A .NET host cannot create a naive date, because the public records use `DateTimeOffset`.

| Option | For | Against |
| --- | --- | --- |
| Read a naive date as UTC. A save through a public call writes a zoned UTC value. Chosen. | The usual .NET date type. Upstream's `ensure_utc` gives a naive date the same meaning. | The stored shape changes for a record that Python wrote naive and that .NET saves again. |
| A marker for a naive value on each public date | A read and a save change nothing. | Each record with a date gets a less common shape. |
| Public dates as `DateTime` | Naive values round-trip. | An aware value with another offset is written back as UTC. |

The documents state the change. The end-to-end test for data that Python wrote (GAPS J11) covers the read.
Inside ported code nothing changes: a `PyDateTime` keeps a naive value naive, and the judge cases pin that.

## Registration

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class GraphitiServiceCollectionExtensions
{
    public static IGraphitiBuilder AddGraphiti(
        this IServiceCollection services,
        Action<GraphitiOptions>? configure = null);
}

public interface IGraphitiBuilder
{
    IServiceCollection Services { get; }
    IGraphitiBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory);
    IGraphitiBuilder UseEmbeddingGenerator(
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>> factory);
    IGraphitiBuilder UseEmbeddingGenerator(
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<double>>> factory);
    IGraphitiBuilder UseCrossEncoder(Func<IServiceProvider, ICrossEncoder> factory);
    IGraphitiBuilder UseKeyedChatClient(object serviceKey);
    IGraphitiBuilder UseChatLogProbabilities(Func<IServiceProvider, IChatLogProbabilities> factory);
}
```

`Graphiti.Net.Neo4j` adds the driver:

```csharp
public static IGraphitiBuilder AddNeo4j(this IGraphitiBuilder builder, Action<Neo4jOptions> configure);
public static IGraphitiBuilder AddNeo4j(this IGraphitiBuilder builder, Func<IServiceProvider, IDriver> factory);
```

A host with OpenAI and Neo4j:

```csharp
services.AddChatClient(openAi.GetChatClient("gpt-4.1").AsIChatClient());
services.AddEmbeddingGenerator(
    openAi.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator());
services.AddGraphiti(o => o.Llm.SmallModel = "gpt-4.1-mini")
        .AddNeo4j(o => { o.Uri = "bolt://localhost:7687"; o.User = "neo4j"; o.Password = password; });
```

Rules of the registration:

- `IGraphiti` is a singleton. One container holds one Graphiti. Upstream allows more than one instance in a process. The scheduler and `PortServices` are process-wide, so a second container shares them.
- The first resolution of `IGraphiti` gives `PortServices` the `ILoggerFactory` and the `TimeProvider` of the container. With no registered `TimeProvider`, the value is `TimeProvider.System`.
- Options are read through `IOptions<GraphitiOptions>`. Under the generic host they are validated at startup. In each host the first resolution of `IGraphiti` validates them.
- A missing driver, chat client, or embedding generator is not an option value. The first resolution of `IGraphiti` fails with a message that names the missing call.
- The embedding generator with `Embedding<float>` is resolved first. With none, the one with `Embedding<double>` is resolved.
- Graphiti closes the driver when the container disposes the implementation, with options and with a factory. Upstream's `close` always closes the driver that it holds (`graphiti.py:530`). A host that needs the `IDriver` after that gives Graphiti its own driver object.
- The library builds no index at start (GAPS G20). The host calls `BuildIndicesAndConstraintsAsync`.
- The extension methods use the namespace `Microsoft.Extensions.DependencyInjection`, as `AddChatClient` does.

## Options

| Option | Type and default | Upstream source |
| --- | --- | --- |
| `StoreRawEpisodeContent` | `bool`, `true` | `Graphiti.__init__` |
| `MaxCoroutines` | `int?`, null. Null uses `SEMAPHORE_LIMIT`, which defaults to 20. | `Graphiti.__init__`, `helpers.py:47` |
| `TraceSpanPrefix` | `string`, `"graphiti"` | `Graphiti.__init__` |
| `Llm.Model`, `Llm.SmallModel` | `string?`, null. Null sets no model id. | `LLMConfig` |
| `Llm.Temperature` | `float?`, null. Null sends no temperature. | `LLMConfig`, default 1. See lead choice L4. |
| `Llm.MaxTokens` | `int`, 16384 | `DEFAULT_MAX_TOKENS` |
| `Llm.ClassifyFailure` | `Func<Exception, ModelCallFailure>?`, null. Values: `None`, `RateLimit`, `ServerError`. | D31 |
| `Llm.Routes` | `IDictionary<string, ModelRoute>`, empty. With one or more routes, `Llm.Model` must have a value. | `LLMRuntime`, `PromptRoutes` |
| `Embedding.Dimensions` | `int`, the value of `EMBEDDING_DIM`, which defaults to 1024 | `embedder/client.py:23` |
| `Embedding.Model` | `string?`, null | `OpenAIEmbedderConfig.embedding_model` |
| `Reranker.Model` | `string?`, null. Null sets no model id, and the chat client uses its own default. This differs from upstream, which sends `gpt-4.1-mini`. | `OpenAIRerankerClient`, default `gpt-4.1-mini` |
| `Prompts.Overrides` | `IDictionary<string, PromptBuilder>`, empty | `create_prompt_library(overrides)` |
| `ConcurrentIngestion` | `ConcurrentIngestionOptions?`, null. Null gives serial ingestion. | `ConcurrentIngestionConfig` |

`ConcurrentIngestionOptions`:

| Option | Type and default | Upstream field |
| --- | --- | --- |
| `Window` | `int`, required, 1 or more | `window` |
| `Lease` | `TimeSpan`, 60 seconds | `lease_seconds` |
| `WriteMargin` | `TimeSpan`, 5 seconds. It must be less than a third of `Lease`. | `write_margin_seconds` |
| `PollInterval` | `TimeSpan`, 0.25 seconds | `poll_interval_seconds` |
| `IndexLag` | `TimeSpan`, 300 seconds | `index_lag_seconds` |
| `MaxApplyAttempts` | `int`, 5 | `max_apply_attempts` |
| `EnabledForGroup` | `Func<string, bool>?`, null | `enabled_for_group` |
| `StateStorePath` | `string?`, null. Null uses the in-memory store. A path uses the SQLite store in that file. | `state_store` |

The nested option classes are `LlmOptions`, `EmbeddingOptions`, `RerankerOptions`, and `PromptOptions`.
`Neo4jOptions` has `Uri`, `User`, `Password`, and `Database` with the default `"neo4j"` (`driver/neo4j_driver.py:65`).
The library reads no `.env` file. The options hold no API key for a model, because the host builds the model clients.

## The facade

Each async member ends in `Async` and takes a `CancellationToken` as its last parameter.
A Python parameter list with many optional values becomes one input record and one options record.
The per-call `driver` parameter of four upstream calls (`retrieve_episodes`, `build_communities`, `search`, `search_`) has no public form, because the driver contract is internal.

| Upstream | `IGraphiti` member | Result |
| --- | --- | --- |
| `add_episode` | `AddEpisodeAsync(EpisodeInput episode, AddEpisodeOptions? options)` | `AddEpisodeResult` |
| `add_episode_bulk` | `AddEpisodeBulkAsync(IReadOnlyList<EpisodeInput> episodes, AddEpisodeBulkOptions? options)` | `AddEpisodeBulkResult` |
| `add_triplet` | `AddTripletAsync(EntityNode source, EntityEdge edge, EntityNode target, AddTripletOptions? options)` | `AddTripletResult` |
| `search` | `SearchAsync(string query, SearchOptions? options)` | `IReadOnlyList<EntityEdge>` |
| `search_` | `AdvancedSearchAsync(string query, SearchConfig config, AdvancedSearchOptions? options)` | `SearchResults` |
| `retrieve_episodes` | `RetrieveEpisodesAsync(DateTimeOffset referenceTime, RetrieveEpisodesOptions? options)` | `IReadOnlyList<EpisodicNode>` |
| `get_nodes_and_edges_by_episode` | `GetNodesAndEdgesByEpisodeAsync(IReadOnlyList<string> episodeUuids, string? groupId)` | `SearchResults` |
| `remove_episode` | `RemoveEpisodeAsync(string episodeUuid, string? groupId)` | none |
| `build_communities` | `BuildCommunitiesAsync(IReadOnlyList<string>? groupIds)` | `BuildCommunitiesResult` |
| `summarize_saga` | `SummarizeSagaAsync(string sagaId, string? groupId)` | `SagaNode` |
| `build_indices_and_constraints` | `BuildIndicesAndConstraintsAsync(bool deleteExisting = false)` | none |
| `close` | No member on `IGraphiti`. See "Lifetime". | none |
| `token_tracker` | `ITokenUsageTracker TokenUsage { get; }` | |
| `nodes`, `edges` | `INodeNamespaces Nodes { get; }`, `IEdgeNamespaces Edges { get; }` | |

Notes on the calls:

- `EpisodeInput` holds `Name`, `Content`, `SourceDescription`, `ReferenceTime`, `Source` with the default `EpisodeType.Message`, and `Uuid`. It serves `add_episode` and the `RawEpisode` of `add_episode_bulk`.
- `AddEpisodeOptions` holds the other 11 parameters of `add_episode` with the upstream defaults. `saga: str | SagaNode` becomes two properties, `Saga` and `SagaNode`. A call that sets both throws `ArgumentException`.
- `entity_types` and `edge_types` are lists of `GraphTypeDefinition`. The list order is the dict order of Python, which reaches prompt text.
- `edge_type_map` is `IReadOnlyList<EdgeTypeRule>`, where `EdgeTypeRule` holds `SourceType`, `TargetType`, and `EdgeTypes`. A list keeps the order, which reaches prompt text (`edge_operations.py:176`).
- `AdvancedSearchAsync` has no default for `config`. Upstream's default is the cross-encoder recipe, which needs the hook of D30. The host names a recipe from `SearchRecipes`.
- Upstream changes the nodes and the edge that a caller passes to `add_triplet`. The public records are immutable, so the changed values come back in `AddTripletResult`.
- Upstream also changes a `SagaNode` that a caller passes to `add_episode` (`graphiti.py:1214`). The public call returns no saga. The host reads the saga again.
- Each call maps its arguments, starts the `GraphitiCore` method of the same name on the scheduler with `Asyncio.Run`, and maps the result. `Asyncio.Run` returns a task and does not block.
- The facade awaits with `ConfigureAwait(false)`, and the task of a public call completes on the thread pool. A continuation of the host never runs on the Graphiti scheduler.
- The facade reproduces upstream defects. `AddEpisodeAsync` with `UpdateCommunities = true` fails for a node count other than two (GAPS U14).

### Lifetime

- The container owns the one instance. `IGraphiti` has no dispose member, so a consumer cannot close the shared instance.
- The internal implementation has `IAsyncDisposable` and `IDisposable`. A container that is disposed with the sync `Dispose` throws for a service that has only the async form.
- Both forms call `GraphitiCore.close` one time. A second call does nothing.
- The preview that the maintainer saw for D24 showed `IGraphiti : IAsyncDisposable`. The adversarial review found the two problems above, and the maintainer chose this form on 2026-11-26.

## Public records

The public types of the root namespace, 59 in all:

| Group | Types |
| --- | --- |
| Nodes | `Node` (abstract), `EpisodicNode`, `EntityNode`, `CommunityNode`, `SagaNode`, `EpisodeType` |
| Edges | `Edge` (abstract), `EntityEdge`, `EpisodicEdge`, `CommunityEdge`, `HasEpisodeEdge`, `NextEpisodeEdge` |
| Inputs | `EpisodeInput`, `AddEpisodeOptions`, `AddEpisodeBulkOptions`, `AddTripletOptions`, `SearchOptions`, `AdvancedSearchOptions`, `RetrieveEpisodesOptions`, `EdgeTypeRule` |
| Results | `AddEpisodeResult`, `AddEpisodeBulkResult`, `AddTripletResult`, `BuildCommunitiesResult`, `SearchResults` |
| Search configuration | `SearchConfig`, `EdgeSearchConfig`, `NodeSearchConfig`, `EpisodeSearchConfig`, `CommunitySearchConfig`, the 4 search method enums, the 4 reranker enums, `SearchRecipes` |
| Search filters | `SearchFilters`, `DateFilter`, `PropertyFilter`, `ComparisonOperator` |
| Custom types | `GraphTypeDefinition`, `GraphFieldDefinition`, `GraphFieldKind` |
| Prompts and routing | `PromptBuilder`, `PromptContext`, `ChatPrompt`, `ModelRoute` |
| Usage | `ITokenUsageTracker`, `TokenUsage`, `PromptTokenUsage` |
| Model boundary | `ICrossEncoder`, `IChatLogProbabilities`, `TokenLogProbability`, `ModelCallFailure`, `GraphitiModelCall`, `ModelCallInfo` |

The options classes, the builder, the namespace interfaces, and the exceptions are listed in their own sections.

This page does not list each property. A record or an options record has one property for each upstream field or parameter, by the mapping rules below, with the upstream default.
`GraphFieldKind` has eight members: `String`, `Integer`, `Number`, `Boolean`, and the four list forms.
A namespace interface has one method for each upstream method, by the rules of the section "Namespaces".

Mapping rules:

| Ported form | Public form |
| --- | --- |
| A field | A property with the PascalCase name and `{ get; init; }`. A required Python field is `required`. |
| `PyDateTime` | `DateTimeOffset`. Input keeps its offset and is cut to microseconds. A naive value on output is read as UTC, as `ensure_utc` does. A save then writes a zoned value (D36). |
| `list[T]` | `IReadOnlyList<T>` |
| `list[float]` for an embedding | `ReadOnlyMemory<double>?`. Neo4j stores a vector with 32-bit values (`db.create.setNodeVectorProperty`), so the first save rounds a 64-bit value to 32 bits. A value that was read changes no more in a later save and read. |
| `dict[str, Any]` (`attributes`, `episode_metadata`) | `IReadOnlyDictionary<string, object?>`. A value is null, `bool`, `long`, `double`, `string`, `DateTimeOffset`, `DateOnly`, a read-only list, or a read-only dictionary. A tuple becomes a read-only list. Another kind of value throws `InvalidCastException` at the facade. A null `episode_metadata` stays null and does not become an empty dictionary. |
| An enum | A C# enum with PascalCase members. The facade maps by member, never by text. |
| `SearchResults` score lists | `IReadOnlyList<double>` next to each item list, in the same order |
| The 16 recipes of `search_config_recipes.py` | Static properties of `SearchRecipes`. Each read gives a new `SearchConfig`. This differs from Python on purpose: in Python a `search` call changes the shared recipe object that a later `search_` call receives (GAPS U8). Inside `GraphitiCore.search` the defect stays as it is. |

A record has value equality on its properties, and a collection property compares by reference, as in each C# record.
Validation stays in the ported classes. The facade builds the ported object and calls `Validate()`, so a bad label throws the same `ValidationException` as in Python.
`ValidationException` is internal to `PyCompat` and derives from `ArgumentException`. A host catches `ArgumentException`.

## Custom entity types and edge types

```csharp
var person = GraphTypeDefinition.Create("Person", "A human being who is named in the text.")
    .AddString("occupation", "The job or profession of the person")
    .AddString("home_city", "The city where the person lives");
```

| Member | Meaning |
| --- | --- |
| `Create(name, description = null)` | The name is the dict key and the class name of Python. The description is the docstring. Null is a class with no docstring: the schema then has no `description` key, and upstream's text `Default Entity Type` applies. |
| `AddString`, `AddInteger`, `AddNumber`, `AddBoolean` | A scalar field. Parameters: the name, the description, `required` with the default `false`, and for a string `maxLength`. |
| `AddStringList`, `AddIntegerList`, `AddNumberList`, `AddBooleanList` | A list field. It has no length limit in M1. |

What the port reads from a definition, and where upstream reads it:

| Upstream read | Site | From the definition |
| --- | --- | --- |
| `type_model.__doc__` | `node_operations.py:192`, `:234`, `:1266`, `edge_operations.py:189`, `combined_extraction.py:136` | `Description` |
| `model_fields` keys and count | `entity_types_utils.py:32`, `:59`, `node_operations.py:1026`, `:1117`, `edge_operations.py:1085`, `:1139` | `Fields` in order |
| `max_length` and `is_required` of a field | `attribute_utils.py:84` to `:98` | `GraphFieldDefinition.MaxLength`, `Required` |
| The type as the response model of the attribute prompt, then `model_json_schema()` | `node_operations.py:1140`, `edge_operations.py:1097`, `:1152`, `llm_client/client.py:227` | A `PydanticSchema` that the definition builds |
| `entity_type(**merged)` | `node_operations.py:1161` | Lax validation of each field by its kind |

- A field name is the Python name, for example `home_city`. It is a wire name: it reaches the prompt and the stored node property.
- An optional field has the default `None`, and its schema is `anyOf` of the kind and `null`, as pydantic writes it for `str | None = Field(default=None, ...)`.
- The five judge fixtures (`Person`, `Product`, `Company`, `Likes`, `WorksAt`) use only optional strings with a description. The judge pins their schema text through the flow cases.
- The `title` of a field is pydantic's: the name with each `_` as a space, then Python `str.title()`. `home_city` gives `Home City`.
- Pydantic writes `inspect.cleandoc` of the docstring into the schema, and `__doc__` holds the raw text. One `Description` serves both: the descriptor keeps the raw text for `__doc__`, and it writes `PyText.Cleandoc` of that text into the schema, as pydantic does.
- A field kind outside the closed set is not possible in M1. The documents say so.
- Inside ported code, a `type[BaseModel]` parameter for a custom type becomes the internal `GraphTypeDescriptor` that the definition builds. This answers the `type[BaseModel]` row of rulebook Part 3 and the three `attribute_utils` sites of rule R15.

Layer 7 of plan 0004 built `GraphTypeDefinition`. This section gave no form for six points, and the lead chose one for each. The maintainer accepts or rejects each one at gate 4.

| Point | Form that the lead chose |
| --- | --- |
| The names of the four list kinds of `GraphFieldKind` | `StringList`, `IntegerList`, `NumberList`, and `BooleanList` |
| The type of `maxLength` | `int?` on `AddString` and on `GraphFieldDefinition.MaxLength` |
| The default of `description` | `null` on `Create` and on each `Add*` |
| A second `Add*` call | It returns a new definition. A definition never changes (D25). |
| A field name that pydantic refuses at class creation | `Add*` throws `ArgumentException`: a name that starts with `_`, `model_config`, and the `model_dump` and `model_validate` members of `BaseModel` |
| Equality of two definitions | The name and the description compare by value, and the field list by reference, as "Public records" says. The internal descriptor is not part of it. |

## The chat adapter

`ChatClientLlmClient` derives from the ported `LLMClient` and implements `_generate_response`, by decision D17.
The base class does the shared work: the attribute preamble, the schema text, the language instruction, input cleaning, the cache, and the retry.

`_generate_response` does not receive the prompt name. So the adapter also overrides `generate_response`, as seven upstream clients do.
The override sets the prompt name in the ambient call value and calls the base method. It adds nothing to the messages.

Request mapping:

| `_generate_response` input | `IChatClient.GetResponseAsync` |
| --- | --- |
| `messages` | One `ChatMessage` for each message, in order. `system`, `user`, and `assistant` map to the `ChatRole` of the same name. Another role text becomes `new ChatRole(role)`. |
| `max_tokens` | `ChatOptions.MaxOutputTokens` |
| `model`, `model_size` | `ChatOptions.ModelId` is `model` if it is set. If not, it is `Llm.SmallModel` for the small size, with `Llm.Model` as the fallback, and `Llm.Model` for the medium size. Null stays unset. |
| `LLMConfig.temperature` | `ChatOptions.Temperature`, only if `Llm.Temperature` has a value |
| A JSON object reply | `ChatOptions.ResponseFormat = ChatResponseFormat.Json` for each call. The schema is in the prompt text, so no schema goes into the format. |

Reply mapping:

| Reply | Result |
| --- | --- |
| `ChatResponse.Text` is empty | `EmptyResponseException`. The base class retries it. |
| `ChatResponse.FinishReason` is `ContentFilter` | `RefusalException`. Not retried. |
| Text | `PyJson.Loads(text)`. Text that is not JSON throws `JsonException`, which the base class retries, as Python retries `JSONDecodeError`. |
| JSON that is not an object | `JsonException` with a message that names the kind of the value |
| `ChatResponse.Usage` | One `token_tracker.record(promptName, inputTokens, outputTokens)` for each reply that parses, as `openai_base_client.py:312` does. A reply with no usage records zeros. |
| An exception from the client | `Llm.ClassifyFailure`, then the default classifier. `RateLimit` throws `RateLimitException` with the provider exception inside. `ServerError` throws `HttpRequestException` with the status 500 and the provider exception inside, because the ported retry test knows a server error only as that type (rule R4). Other exceptions go to the caller as they are. |

The retry is upstream's: 4 attempts, with the wait of rule R16. A provider SDK can add its own retries below the adapter.
After the fourth attempt the caller gets the last exception: `RateLimitException`, `EmptyResponseException`, `JsonException`, or the `HttpRequestException` that wraps the provider exception.

The adapter does not strip a markdown code fence from the reply. Upstream's generic client does that, and the base path does not. See open question 8.

### Call information for host middleware and for the judge

The adapter publishes the facts of the current call through an ambient value:

```csharp
public static class GraphitiModelCall
{
    public static ModelCallInfo? Current { get; }
}

public sealed record ModelCallInfo(
    string Operation, string? PromptName, string? ResponseModel, string? ModelSize, string? Model, string? ResponseSchema);
```

`Current` is set while an adapter awaits a host service, and it flows to the thread pool with the execution context.
`Operation` is `chat`, `embed`, `embed_batch`, or `rerank`. `Model` is the `model` argument of `_generate_response`, which is null with no route.
The judge chat client reads `ResponseModel`, `ModelSize`, and `Model` from it to write the `llm` capture. It does not read `ChatOptions.ModelId`, which also holds the sized option.
A host can read it in a `DelegatingChatClient`, for example to send `ResponseSchema` as native structured output. Decision D17 allows that as a .NET extension outside the judge.

The other option was to put these values in `ChatOptions.AdditionalProperties`.
An ambient value cannot reach the provider request. A provider adapter is free to forward additional properties to the service, and this design does not depend on what each adapter does with them.

The span attribute `llm.provider` takes `ChatClientMetadata.ProviderName` from `IChatClient.GetService`, or `unknown`. Upstream derives it from the Python class name, which has no .NET form.

## The embedding adapter

`EmbeddingGeneratorEmbedder` derives from the ported `EmbedderClient`.

| Upstream | Adapter |
| --- | --- |
| `create(input_data)` | `GenerateAsync(inputData, options)` with the caller's list, then the first vector. `Operation` is `embed`. |
| `create_batch(input_data_list)` | One `GenerateAsync(texts, options)` call, results in input order. `Operation` is `embed_batch`. |
| `[: embedding_dim]` | The vector is cut to `Embedding.Dimensions`. The adapter does not send `EmbeddingGenerationOptions.Dimensions`, because upstream's OpenAI embedder cuts on the client and sends no size. |
| `embedding_model` | `EmbeddingGenerationOptions.ModelId` if `Embedding.Model` has a value |
| `list[float]` | `List<double>`. An `Embedding<float>` value is widened. An `Embedding<double>` value is copied as it is, which the judge embedder needs. |

Each of the 6 MVP call sites of `create` passes a list with one string, for example `nodes.py:509`. The ported parameter is `List<string>`.
`create` in Python also accepts a plain string and token id lists. No MVP module passes one.
A generator sees the same `GenerateAsync` call for `create` and for `create_batch`. The Python judge records them in two forms, so the judge generator reads `Operation` from the ambient call value.

## The cross-encoder

```csharp
public interface ICrossEncoder
{
    Task<IReadOnlyList<(string Passage, double Score)>> RankAsync(
        string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default);
}

public interface IChatLogProbabilities
{
    void Request(ChatOptions options, int topLogProbabilityCount, IReadOnlyDictionary<int, int> logitBias);
    IReadOnlyList<TokenLogProbability>? ReadFirstToken(ChatResponse response);
}

public readonly record struct TokenLogProbability(string Token, double LogProbability);
```

- `ICrossEncoder` is the public form of `CrossEncoderClient.rank`. A host can register its own with `UseCrossEncoder`. A bridge in `Internal/` gives it to ported code as a `CrossEncoderClient`. The result is in descending order of the score.
- The default is the ported class `OpenAIRerankerClient` of the queue unit `cross_encoder.openai_reranker_client`. The unit keeps the upstream prompt, the score rule, the sort, and the error handling.
- Upstream calls `self.client.chat.completions.create(...)`. The port calls the lead seam `LogProbabilityChat.CreateAsync` with the same seven arguments. The constructor takes the seam and an `LLMConfig`. Rule R19 gives the details.
- The seam builds `ChatOptions` with `MaxOutputTokens = 1`, `Temperature = 0`, and `ModelId` from `Reranker.Model`. It calls `Request(options, 2, { 6432: 1, 7983: 1 })` on the hook, calls the chat client of D28 on the thread pool, and returns `ReadFirstToken(response)`.
- The score rule is upstream's. The first token text is trimmed, cut at its first space, and set to lower case. If it is `true`, the score is `exp(logprob)`. If not, the score is `1 - exp(logprob)`.
- A null or empty read gives a passage with no score. Upstream then fails in `zip(..., strict=True)`, and the port keeps that.
- The passages run through `semaphore_gather` with no limit argument, so the limit is `SEMAPHORE_LIMIT` and not `MaxCoroutines`, as upstream.
- An exception that the classifier of D31 names a rate limit becomes `RateLimitException`, as upstream maps `openai.RateLimitError`.
- With no registered `IChatLogProbabilities`, the seam throws `InvalidOperationException` before the first model call. The message names `UseChatLogProbabilities` and the page of the documents.
- The token ids `6432` and `7983` belong to one OpenAI tokenizer. A hook for another provider ignores the bias or maps it.

Limits that the documents state:

- The OpenAI SDK gives a log probability as a 32-bit value. A score differs from Python's in the eighth digit.
- A cache in front of the chat client returns a reply with no `RawRepresentation`. The hook then reads nothing, and the rerank call fails. A host puts no cache on the client that serves the reranker.
- Upstream's comment says that reasoning models reject `max_tokens` and `logit_bias`. A host sets `Reranker.Model` to a model that accepts them.

The OpenAI implementation in the documents sets `IncludeLogProbabilities`, `TopLogProbabilityCount`, and `LogitBiases` on a `ChatCompletionOptions` that it returns from `ChatOptions.RawRepresentationFactory`.
It reads `ContentTokenLogProbabilities[0].TopLogProbabilities` from `ChatResponse.RawRepresentation`, which the OpenAI adapter sets to the `ChatCompletion`.

The judge uses its own cross-encoder at the `rank` seam, so no judge case runs `OpenAIRerankerClient`. Upstream has no test for the class.
One end-to-end test with a scripted chat client and a scripted hook covers the score rule and the order of the result.

## Prompt overrides and model routing

```csharp
public delegate ChatPrompt PromptBuilder(PromptContext context);

public sealed record ChatPrompt(string System, string User);

public sealed class PromptContext
{
    public string Name { get; }
    public IReadOnlyDictionary<string, object?> Values { get; }
    public ChatPrompt BuildDefault();
}

public sealed class ModelRoute
{
    public required string ModelId { get; init; }
    public object? ChatClientKey { get; init; }
    public int? MaxTokens { get; init; }
    public IDictionary<string, PromptBuilder> PromptOverrides { get; }
}
```

- A key of `Prompts.Overrides` is one of the 25 upstream prompt names, for example `extract_nodes.extract_message`. An unknown name fails at startup.
- `PromptContext.Values` is a read-only view of the upstream context dict, with the value kinds of the public records. The keys are upstream's and can change when the pin moves.
- `BuildDefault()` gives the built-in prompt for the same context, so a host can add text to it.
- `ChatPrompt` maps to upstream's `ChatPrompt`. Upstream adds the Unicode note to the system text after the builder returns.
- A key of `Llm.Routes` is a prompt name or one of the 8 group names. Upstream resolves the name first, then the group, then the default model (`llm_runtime.py:147`).
- `ChatClientKey` names a keyed `IChatClient` of the container. Null uses the chat client of D28.
- With no route and no override, the facade passes no prompt library and no runtime. `GraphitiCore` then uses the module-level prompt functions, which is the path that the judge pins. A library object, also the default one, moves each call to another code path (`graphiti_types.py:95`).
- With one or more overrides and no route, the facade passes `create_prompt_library(overrides)` as `prompt_library`.
- With one or more routes, it builds an `LLMRuntime` with the routes and the overrides. The default model of the runtime is `Llm.Model` on the chat client of D28. Upstream rejects an empty model id (`prompt_config.py:188`), so `Llm.Model` must have a value.
- Each route with a `ChatClientKey` gets its own `ChatClientLlmClient` on the keyed chat client. A route with no key uses the main one.
- On the route path, `model_size` does not select `Llm.SmallModel`. That is upstream's rule. A host routes a light prompt to a small model by name.
- Upstream rejects `llm_runtime` together with `llm_client` or `prompt_library`. The options cannot express that conflict, so the facade never passes both.

The judge has no scenario for an override or a route (GAPS J13, debt T12). The end-to-end layer covers both in M1.

## Namespaces

`Nodes` has `Entity`, `Episode`, `Community`, and `Saga`. `Edges` has `Entity`, `Episodic`, `Community`, `HasEpisode`, and `NextEpisode`. The names mirror upstream.

- Each upstream method becomes one `Async` method with the same parameters and defaults, for example `Nodes.Entity.GetByGroupIdsAsync(groupIds, limit, uuidCursor)`.
- The `tx` parameter has no public form, because the transaction type belongs to the internal driver contract.
- A method that changes its argument upstream returns the changed record. `Nodes.Entity.LoadEmbeddingsAsync(node)` returns an `EntityNode`.
- The methods reproduce upstream defects. The batched deletes, the saga save, and a save with no embedding fail as upstream fails (GAPS U1, U2, U7). The documents list them.
- A namespace call applies no group filter beyond what upstream applies. A multi-tenant host makes sure that the UUIDs belong to its tenant.

## Token usage

```csharp
public interface ITokenUsageTracker
{
    IReadOnlyDictionary<string, PromptTokenUsage> GetUsage();
    TokenUsage GetTotalUsage();
    void Reset();
}
```

`PromptTokenUsage` has `PromptName`, `CallCount`, `TotalInputTokens`, `TotalOutputTokens`, `TotalTokens`, `AvgInputTokens`, and `AvgOutputTokens`. `TokenUsage` has `InputTokens`, `OutputTokens`, and `TotalTokens`.
Upstream's `print_summary` writes to standard output and has no public form.
A reply with no usage values records one call with zero tokens.
With routes, each `ChatClientLlmClient` of one Graphiti shares one tracker. Upstream shows only the tracker of the default client (lead choice L13).

## Host code and the scheduler

A public call works from any thread and with any `SynchronizationContext`. The task that it returns completes off the Graphiti scheduler.

| Host code | Where it runs | Rule for the host |
| --- | --- | --- |
| `IChatClient`, also a keyed route client | Thread pool | Normal .NET rules. Calls run in parallel, up to the limit of each gather. |
| `IEmbeddingGenerator` | Thread pool | The same |
| A custom `ICrossEncoder` | Thread pool | The same |
| `IChatLogProbabilities` | Thread pool, with the chat call | No I/O |
| `Llm.ClassifyFailure` | In line on the scheduler | No blocking call, no I/O, no call to `IGraphiti` |
| A `PromptBuilder` | In line on the scheduler | The same |
| `ConcurrentIngestion.EnabledForGroup` | In line on the scheduler | The same |
| Host parts that .NET calls from ported code: an `ILoggerProvider`, the `TimeProvider`, an `ActivityListener` | In line on the scheduler | No blocking call |
| The `IDriver` of a factory, and the handlers of an `HttpClient` | The call starts on the scheduler. The I/O completes on the thread pool. | No blocking call before the first `await` |

- The adapters make the hop with one internal helper, `Asyncio.CallHostAsync`. It starts the host call on `TaskScheduler.Default` and resumes on the scheduler. It is a new `PyCompat` helper, which the adapter work of plan 0004 needs.
- The review measured the hop: the ambient call value reaches the thread pool, two calls at the same time keep their own values, and the caller resumes on the scheduler.
- A host service is called from more than one thread at a time. `IChatClient` and `IEmbeddingGenerator` require thread safety by their own contract.
- An exception from a delegate in line goes to the caller of the public call as it is.
- A `CancellationToken` of a public call reaches each host service call.
- Rule R8 keeps `Asyncio.OnLoopAsync` for the other direction: a lambda that a library calls back into ported code.

## Errors

| Public exception | When |
| --- | --- |
| `GraphitiException` and the 14 classes of rule R4 that derive from it | As upstream raises them |
| `RateLimitException`, `RefusalException`, `EmptyResponseException` | From the chat adapter and the reranker |
| `ArgumentException` | A value that a ported validator rejects, and each upstream `ValueError`. One exception: a node label that is not safe gives `NodeLabelValidationException` when the check runs outside a model (`nodes.py:685`, `search_filters.py:101` and `:151`, `node_db_queries.py:26`). That class derives from `GraphitiException` only (rule R4). |
| `InvalidOperationException` | A rerank call with no log probability hook, and each upstream `RuntimeError` |
| A `Neo4j.Driver` exception | As it arrives. The core never wraps it. |
| A provider exception | As it arrives, unless the classifier names it a rate limit |
| `OperationCanceledException` | The token of the call was canceled |
| `JsonException` | The model reply was not a JSON object in 4 attempts |
| `HttpRequestException` with a provider exception inside | The classifier named a server error in 4 attempts |
| `KeyNotFoundException`, `InvalidCastException`, `NotImplementedException`, `TimeoutException`, `MissingMemberException` | Each upstream `KeyError`, `TypeError`, `NotImplementedError`, timeout, and `AttributeError`, by the table of rule R4 |
| `DivideByZeroException` | Each upstream `ZeroDivisionError`, by the table of rule R4 |
| `OverflowException` | Each upstream `OverflowError`, by the table of rule R4. Also an integer that a 64-bit `long` cannot hold: see the limit below. |

The two decisions client exceptions stay internal with their module.

One limit: `Py.Int` and `Py.FloorDiv` throw `OverflowException` for an integer that a 64-bit `long` cannot hold, where Python returns a value.
`OverflowException` does not derive from `ArgumentException`, so a host's `catch (ArgumentException)` does not catch it.
Example: `attribute_utils.py:67` calls `int(raw)` inside `except ValueError`, and the environment value `99999999999999999999` is an int in Python.
Rule R4 of [the rulebook](../../port/RULES.md) states the same limit for ported code.

This page also gives the rule for the provider errors that rule R4 leaves open:

| Python | C# |
| --- | --- |
| `httpx.HTTPStatusError`, `error.response.status_code` | `HttpRequestException` with a `StatusCode` that is not null |
| `httpx.TransportError` | `HttpRequestException` with a null `StatusCode`, or an `IOException` from a body read |
| `httpx.TimeoutException` | `TimeoutException`, or an `OperationCanceledException` for which `Asyncio.IsCancellation` is false |
| `openai.RateLimitError` | An exception that the classifier of D31 names `RateLimit` |

## Lead choices

The lead made these choices. The maintainer accepts or changes them with this page.

| # | Choice | Reason |
| --- | --- | --- |
| L1 | `AddGraphiti` returns a builder, and `IGraphiti` is a singleton with one instance for each container. | The scheduler and `PortServices` are process-wide (D22, D23). Named instances wait for port phase 9. |
| L2 | Public dates are `DateTimeOffset`. A naive value on output is read as UTC. | A host cannot build a naive value, and `ensure_utc` gives a naive value the same meaning. |
| L3 | Embeddings in public records are `ReadOnlyMemory<double>`. | The ported classes hold an embedding as `list[float]`, which is 64-bit, and each ranking function computes with 64-bit values. The public type keeps that type, so the facade changes no value. The first reason of this row was false: it said that the database holds 64-bit values. Neo4j stores 32-bit values, so the first save rounds each value (corrected on 2026-11-28 after the first review of the surface). |
| L4 | `Llm.Temperature` defaults to null, and the adapter then sends none. Upstream sends 1. | 1 is the provider default for OpenAI. Some reasoning models reject the parameter, and upstream's OpenAI client leaves it out for them. |
| L5 | Each chat call asks for `ChatResponseFormat.Json`. | The base path always parses the reply as a JSON object. Upstream's generic client does the same in its `json_object` mode. |
| L6 | The call facts go through the ambient `GraphitiModelCall.Current`, not through `ChatOptions.AdditionalProperties`. | An ambient value cannot reach a provider request. |
| L7 | `AdvancedSearchAsync` has no default recipe. | Upstream's default needs the log probability hook, and a call with no hook fails. |
| L8 | The response cache of `LLMClient` has no option. It stays off. | A host adds a cache as middleware on its `IChatClient`. The ported cache stays for the evaluation tests (GAPS J4). |
| L9 | Tracing uses one `ActivitySource` named `Graphiti.Net`. A host turns it on with `AddSource("Graphiti.Net")`. | With no listener an `ActivitySource` starts no activity. One difference from upstream's no-op tracer stays below the facade: the ported tracer keeps upstream defect U19 and wraps an error of a span body, also with no listener. The facade gives the host the error of the call, so a host sees the behavior of the no-op tracer (corrected on 2026-11-28 after the adversarial pass of the surface). |
| L10 | The name `MaxCoroutines` stays, and `search_` becomes `AdvancedSearchAsync`. | The first name ties the option to the upstream documents. A trailing underscore is not a .NET name. |
| L11 | `GraphTypeDefinition.Name` is both the dict key and the class name. | They are equal in the judge fixtures and in the upstream examples. |
| L12 | The public namespaces mirror upstream, without the `tx` parameter. | A host reads the upstream documents for these calls. |
| L13 | Added after the review and accepted on 2026-11-26: with routes, each chat adapter of one Graphiti shares one usage tracker. | Upstream's `token_tracker` shows the default client only, so routed calls are not visible to a host. |

## Effects on other documents

The same change that approved this page made these changes:

| Document | Change |
| --- | --- |
| [port/DECISIONS.md](../../port/DECISIONS.md) | Rows D24 to D35. Lead choice 2 and lead choice 3 point to D30 and D32. Two library rows: `Microsoft.Extensions.DependencyInjection.Abstractions` 11.0.0 and `Microsoft.Extensions.Options` 11.0.0. |
| [port/RULES.md](../../port/RULES.md) | The rule changes below. Open item O3 is closed. The maintainer chose to make them in the same branch. |
| [port/GAPS.md](../../port/GAPS.md) | G6 and G10 point to D26 and D34. |
| [ARCHITECTURE.md](../../ARCHITECTURE.md) | The planned module map names the facade layer, the adapters, and the `Core` folder. |

Rule changes:

| Rule | Change |
| --- | --- |
| R1 | Ported files move to `src/Graphiti.Net/Core/`, and ported namespaces start with `Graphiti.Core`. The two error files use `Graphiti`. `using` directives go inside the namespace in each project. A branch that builds a default object throws. Public exception members get XML comments. |
| R2, R3 | The class `Graphiti` of `graphiti.py` is `GraphitiCore`. The exception classes of the two error files are `public`. Nothing else ported is public. |
| R4, R17 | The four provider error rows of the section "Errors", and the inner-exception constructor. `PortServices.ClassifyModelFailure` gives ported code the classifier of D31. |
| R7, R15, Part 3 | `type[BaseModel]` for a custom type is `GraphTypeDescriptor`. A response model parameter is the internal interface `IResponseModel`. `Pydantic.Wrap<TException>` for a core exception type. |
| R8, Part 2 | `Asyncio.CallHostAsync` for a call from an adapter to a host service. `Asyncio.Run` returns a task. |
| R19 | The decisions client uses `HttpClient` (D32), with rules for the body bytes, the headers, and the timeout. The reranker unit calls the lead seam `LogProbabilityChat` (D30). |
| R22 | The five judge fixtures are `GraphTypeDescriptor` objects in the judge host. |

One Opus 5.5 adversarial review ran on this page and on the rule changes on 2026-11-25. [Plan 0003](../exec-plans/active/0003-rulebook-and-pilot.md) lists its 14 important findings and the answer to each one.

## Checked facts

Checked on 2026-11-25. Upstream paths are under `.upstream/graphiti/graphiti_core` at commit `a61333e`.

| Claim | Source |
| --- | --- |
| The upstream class has 11 public async graph calls, `close`, and the `token_tracker` property. | `graphiti.py`, read with `ast` |
| `add_episode` has 17 parameters. `RawEpisode` has 6 fields. | `graphiti.py:1630`, `utils/bulk_utils.py:105` |
| The namespaces have 37 node calls and 38 edge calls. | `namespaces/nodes.py`, `namespaces/edges.py`, read with `ast` |
| The base `generate_response` adds the schema text, and `_generate_response` takes messages, the response model, `max_tokens`, `model_size`, and `model`. | `llm_client/client.py:152`, `:207` |
| The retry covers `RateLimitError`, `EmptyResponseError`, `JSONDecodeError`, and a 5xx `HTTPStatusError`. | `llm_client/client.py:61` |
| The reranker sends `logprobs=True`, `top_logprobs=2`, `max_tokens=1`, `temperature=0`, and `logit_bias` for `6432` and `7983`. | `cross_encoder/openai_reranker_client.py` |
| The decisions client posts to `{base_url}/decisions` with `httpx` and has its own retry loop. | `llm_client/openai_decisions_client.py:191` |
| The OpenAI embedder cuts the vector to `embedding_dim` and sends no size. | `embedder/openai.py` |
| 11 import-time environment reads and 3 call-time reads | `grep` for `os.getenv` and `os.environ` in the MVP modules |
| 25 prompt names in 8 groups | `prompts/names.py` |
| The judge captures `messages`, `response_model`, `max_tokens`, `model_size`, and `model` for each model call. | `port/judge/host-python/judge_clients.py:238` |
| `Microsoft.Extensions.AI.Abstractions` and `Microsoft.Extensions.AI.OpenAI` are at 11.0.1. `Microsoft.Extensions.Options` and `Microsoft.Extensions.DependencyInjection.Abstractions` are at 11.0.0. | The version index of nuget.org for each package |
| `ChatOptions` has `ModelId`, `MaxOutputTokens`, `Temperature`, `ResponseFormat`, `AdditionalProperties`, and `RawRepresentationFactory`, and no member for log probabilities. | The Microsoft Learn page of `ChatOptions`. The page shows package version 10.11.0. |
| Each `Microsoft.Extensions.AI` member that this page names exists in the 10.8.3 assemblies: `RawRepresentationFactory`, `RawRepresentation`, `ChatResponseFormat.Json`, `ChatFinishReason.ContentFilter`, `ChatClientMetadata.ProviderName`, `GetService`, `EmbeddingGenerationOptions.Dimensions`, `Embedding<double>`, and `new ChatRole(string)`. | The adversarial review, by reflection on the local package cache |
| The ambient call value reaches a call that starts on `TaskScheduler.Default`, and the caller resumes on an exclusive scheduler. | The adversarial review, with a test project |
| `ChatResponse` has `Text`, `Usage`, `FinishReason`, and `RawRepresentation`. `ChatResponseFormat.Json` asks for JSON with no schema. | The Microsoft Learn pages of both types, version 10.11.0 |
| `EmbeddingGenerationOptions` has `Dimensions` and `ModelId`. `IEmbeddingGenerator` implementations are thread-safe by contract. | The Microsoft Learn pages of both types, version 10.11.0 |
| `AddChatClient` and `AddKeyedChatClient` are in the package `Microsoft.Extensions.AI`, namespace `Microsoft.Extensions.DependencyInjection`. | The Microsoft Learn page of `ChatClientBuilderServiceCollectionExtensions`, version 11.0.0 |
| The OpenAI adapter reads `RawRepresentationFactory` as a `ChatCompletionOptions`, sets `ChatResponse.RawRepresentation` to the `ChatCompletion`, and holds no code for log probabilities. | `OpenAIChatClient.cs` on the `main` branch of `dotnet/extensions` |
| The OpenAI SDK has `IncludeLogProbabilities`, `TopLogProbabilityCount`, `LogitBiases`, and `ContentTokenLogProbabilities`. `ClientResultException` has `Status`. | The XML documents of `OpenAI` 2.12.0 and `System.ClientModel` 1.14.0 in the local package cache |
| The OpenAI SDK has an experimental `DecisionsClient` from version 2.15.0 of 2026-11-04. | The change log of `openai/openai-dotnet` on `main` |

## Open questions

| # | Question | Owner | Needed before |
| --- | --- | --- | --- |
| 1 | The Learn pages of `Microsoft.Extensions.AI.Abstractions` show version 10.11.0. Nobody inspected the 11.0.1 assembly. Make sure that each member of the table above exists when the package first enters `Directory.Packages.props`. | Lead | The adapter unit |
| 2 | `PyCompat` is a third assembly. Does it ship inside the `Graphiti.Net` package or as its own package? `ValidationException` lives in it. | Maintainer | Plan 0005 |
| 3 | Does the port need `OpenTelemetry.Api`? Lead choice L9 needs only `System.Diagnostics`. | Lead, then the maintainer for the library table | The `tracer` unit |
| 4 | `IHttpClientFactory` is in the package `Microsoft.Extensions.Http`, not in the box as the library table says. The decisions client needs a row for it, or it takes an `HttpClient` directly. | Maintainer | The decisions client unit |
| 5 | Prompt overrides, routing, and the decisions client have no judge scenario (GAPS J13, debt T12). They are public in M1 with end-to-end tests only, until a scenario exists. | Lead | Gate 4 |
| 6 | The driver session member for a write with a timeout (rulebook O6) is still open. This page does not decide it. | Maintainer | Layer 7 of plan 0004 |
| 7 | Does a second `AddGraphiti` in one process with other options fail or share? L1 says that the containers share the process-wide parts. | Lead | The facade unit |
| 8 | The chat adapter does not strip a markdown code fence. A local model that wraps its JSON in a fence then fails after 4 attempts. A strip step is a .NET extension below the judge seam. | Maintainer | The adapter unit |
| 9 | The typed helper `GraphTypeDefinition.From<T>()` and more field kinds. | Maintainer | After M1 |
| 10 | Plan 0004 gives layer 3, with "the new `Microsoft.Extensions.AI` adapter", to a Sonnet 5.5 workhorse. The adapters, the facade, and the seams are lead code by rule R1. The plan needs a row for them. | Lead | Layer 3 of plan 0004 |
| 11 | A value in `attributes` that the value model cannot hold, for example a Neo4j point, throws at the facade. Nobody listed which values the Neo4j project can return. | Lead | The facade unit |
| 12 | Two ported namespaces can hold one class name. Two `using` directives inside a namespace are then ambiguous. Nobody searched upstream for such pairs. | Lead | Layer 0 of plan 0004 |

## What to look at again

- After the pilot: the hop of D33. If a profile shows a cost, the embedding adapter can batch.
- After M1: the ten environment variables of D34, the single instance of L1, and the internal driver contract.
- When the OpenAI SDK marks `DecisionsClient` stable: the public route target of D32.
- When `Microsoft.Extensions.AI` adds a member for log probabilities: the hook of D30 gets a default implementation.
