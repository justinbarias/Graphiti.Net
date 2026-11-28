# Rulebook: Python to C# for Graphiti.Net

Port phase 4. This file tells an implementer how each Python pattern of upstream Graphiti becomes C#.
The lead owns this file. An implementer follows it as written and never changes it.
Gate 3 is the maintainer's approval of this file. [The design document](../docs/design-docs/rulebook-design.md) gives the reasons for the rules that are not obvious.

Part 1 holds the rules for this repository. Part 2 lists the `PyCompat` helpers. Part 3 holds the general rules from the porting skill.
Part 4 maps each entry of [GAPS.md](GAPS.md) to a rule. Part 5 lists what is still open.
If a rule in Part 1 and a rule in Part 3 disagree, Part 1 applies.

## How to use this file

1. Read the Python file of your unit, then the rules that Part 4 names for the constructs in it.
2. If a construct has no rule, stop. Write `// TODO(port): no rule for <construct>`, set the unit to `blocked`, and tell the lead.
3. If a rule gives a result that the judge rejects, stop and tell the lead. Do not work around the rule.
4. Never fix upstream behavior. Rule R21 lists the known defects that the port reproduces.

## Part 1. Rules for this repository

### R1. Files and layout

| Python | C# |
| --- | --- |
| `graphiti_core/<pkg>/<module>.py` | `src/Graphiti.Net/Core/<Pkg>/<Module>.cs`, namespace `Graphiti.Core.<Pkg>` |
| `graphiti_core/<module>.py` | `src/Graphiti.Net/Core/<Module>.cs`, namespace `Graphiti.Core` |
| A package that holds one module with the name of the package: `driver/graph_operations/graph_operations.py`, `driver/search_interface/search_interface.py` | The file keeps its folder, and the namespace is the one of the parent: `Graphiti.Core.Driver`. With its own namespace, the class `SearchInterface` has the name of its namespace. |
| `graphiti_core/errors.py`, `graphiti_core/llm_client/errors.py` | `src/Graphiti.Net/Errors.cs` and `src/Graphiti.Net/LlmClientErrors.cs`, namespace `Graphiti`. These exception classes are public (R3). |
| `graphiti_core/driver/neo4j/<sub>/<module>.py` | `src/Graphiti.Net.Neo4j/<Sub>/<Module>.cs`, namespace `Graphiti.Neo4j.<Sub>` |
| `graphiti_core/driver/neo4j_driver.py` | `src/Graphiti.Net.Neo4j/Neo4jDriver.cs`, namespace `Graphiti.Neo4j` |
| A folder name such as `llm_client` or `utils/maintenance` | PascalCase for each part: `LlmClient`, `Utils/Maintenance` |
| `__init__.py` that only imports and exports names | No file. Set `target` in the queue to `none: re-exports only`. |
| `__init__.py` with code | `<Pkg>/Package.cs` with a static class `<Pkg>Package` |
| Code that has no upstream module (the logger holder, the facade, the model adapters) | `src/Graphiti.Net/Internal/`, namespace `Graphiti.Internal`. Only the lead adds files there. |
| The public surface: `IGraphiti`, the public records, the options | `src/Graphiti.Net/`, namespace `Graphiti`. Only the lead adds files there, except the two error files of the row above. [The public surface design](../docs/design-docs/public-surface-design.md) lists the types. |

- Ported code never declares a type in the namespace `Graphiti` itself, except the two error files. The public records there have the names of ported classes, for example `EntityNode` and `SearchConfig` (decision D35).
- Use a file-scoped namespace, and write each `using` directive after the namespace line, not at the top of the file. This holds for each C# project below the namespace `Graphiti`: the files of `Core/` and `Internal/`, the Neo4j project, the `PyCompat` project, the judge host with `Bindings.cs`, and the test projects. Most of these files name a ported type. Each of these namespaces is a child of `Graphiti`. C# searches the parent namespace `Graphiti` before it searches a `using` directive at the top of a file.
- A wrong placement binds a name such as `SearchConfig` or `EntityNode` to the public record. The analyzer rule IDE0065 is an error in `.editorconfig`, so the build fails for a directive above the namespace. Without that rule, the build does not always fail. A constructor call gave `error CS1503`, but `typeof(SearchConfig)` and `value is SearchFilters` compiled and used the public record (measured on 2026-11-25 with the .NET 10 SDK). A directive inside the namespace binds to the ported type.
- In the Neo4j project and in each test project, write `using global::Neo4j.Driver;`. Inside `namespace Graphiti.Neo4j.<Sub>;` the name `Neo4j` is `Graphiti.Neo4j`, and `using Neo4j.Driver;` fails with `error CS0234`.
- A ported file needs no `using Graphiti;`. The exception classes and `ModelCallFailure` are visible from each `Graphiti.Core` namespace.
- A branch that builds a default object from a module that is not ported, or the Neo4j driver from core code, is not ported. Keep the parameter, and throw in that branch: `throw new ArgumentException("<python parameter name> must be provided"); // PORT: D3, D28`. The four sites are in `Graphiti.__init__`: `Neo4jDriver(uri, user, password)` at `graphiti.py:372`, `OpenAIClient()` at `:385`, `OpenAIEmbedder()` at `:389`, and `OpenAIRerankerClient()` at `:393`. The `uri is None` test before the first one keeps its upstream message. The facade always passes the four objects.
- A public member needs an XML comment, because the build treats a missing one as an error (CS1591). Where Python has no docstring, write `/// <summary>Initializes a new instance of the <see cref="X"/> class.</summary>` for a constructor, `/// <summary>Initializes a new instance of the <see cref="X"/> class with the exception that caused this one.</summary>` for the constructor with `innerException` (R4), and `/// <summary>Gets the Python attribute <c>name</c>.</summary>` for a property, with the Python attribute name. This applies to the two error files only.
- Put `// Ported from graphiti_core/<path>.py` on the first line of each file.
- Copy the licence text at the top of the Python file into a block comment (`/* ... */`) after that first line, with the same words.
- Keep the definitions in the order of the Python file. One Python module is one C# file.
- Write no private helper that Python does not have. If Python writes the same expression at two sites, write it at both sites. The one exception is a local function inside `ModelValidate` for a reader that two or more fields use, or that does not fit on one line (R7).
- Copy each docstring into an XML doc comment with the same text. If the compiler forces a `<param>` tag for a parameter that the docstring does not name (CS1573), give the tag a short text of its own.
- Copy each code comment to the same place. A comment that explains a C# difference starts with `// PORT:`.
- Port each branch of a function, also the branches for FalkorDB, Neptune, Kuzu, and Relate. The judge calls query builders with each provider value.
- A module with the note `replaced:`, `out-of-mvp:`, or `not-ported:` in [queue.json](queue.json) is not ported. If your unit imports a name from such a module, stop and tell the lead.

### R2. Names

| Python | C# |
| --- | --- |
| Module-level functions and constants | Members of a static class named after the module: `helpers.py` gives `Helpers` |
| Module name equal to its folder name (`search/search.py`, `driver/driver.py`, `graph_operations/graph_operations.py`, `search_interface/search_interface.py`) | The static class gets the suffix `Module`: `SearchModule` |
| Module name equal to a class in the file, when you ignore case and underscores (`tracer.py` with `Tracer`, `llm_runtime.py` with `LLMRuntime`, `search_filters.py`, `change_set.py`, `delta.py`, `search_config.py`, `neo4j_driver.py`, `openai_reranker_client.py`) | The static class gets the suffix `Module`: `TracerModule`, `LlmRuntimeModule`. A module that imports a class with the name of its module class gets the suffix too: `combined_extraction.py` imports the model `CombinedExtraction`, so its class is `CombinedExtractionModule`. A module with no module-level function or constant gets no static class. |
| Two ported classes with one name in two namespaces (`Neighbor` of `community_operations.py` and of `driver/operations/graph_utils.py`) | Each one keeps its Python name. A file that uses both names one of them with its namespace. |
| `snake_case` function | `PascalCase` method. An `async def` gets the suffix `Async`. |
| `snake_case` field, parameter, or local | `PascalCase` property, `camelCase` parameter or local |
| `UPPER_SNAKE` constant | `PascalCase` constant or static readonly field: `DEFAULT_PAGE_LIMIT` gives `DefaultPageLimit` |
| `_private` function or field | The same name without the underscore. `private` if only its own class uses it, `internal` if not. |
| The case of a private field | camelCase: `_conn` is `conn`. A name that is a C# keyword takes the suffix `Value`: `_lock` is `lockValue`. |
| A class | The same name. An exception class follows R4. |
| The class `Graphiti` of `graphiti.py` | `GraphitiCore`. A type named `Graphiti` inside a namespace that starts with `Graphiti` breaks each qualified name (decision D24). The module-level functions of the file go to `GraphitiModule`, by the row above. |
| `Prompt` in each of the 8 prompt modules | The interface `<Module>Prompt`, for example `DedupeNodesPrompt`, with one get-only property of the type `PromptFunction` for each attribute. This is the name that `prompts/lib.py` imports it under. |
| `Versions` in each of the 8 prompt modules | No C# type. It is a `TypedDict` that `prompts/lib.py` reads by key and with `.items()`, so a value of it is an `OrderedDictionary<string, PromptFunction>` (R7, R10). |
| An enum member, `UPPER_SNAKE` or `lower_snake` | PascalCase by the words of the Python name: `NEO4J` gives `Neo4j`, `FALKORDB` gives `Falkordb`, `fact_triple` gives `FactTriple`. The Python name stays in `[PyEnumMember]` (R7). |
| A name that is a C# keyword (`eval`, `params`, `event`) | Add the prefix `@` for a parameter or a local. For a member, use the PascalCase name. |
| Keyword arguments with defaults | Optional parameters in the same order with the same defaults |
| `**kwargs` that holds query parameters | `OrderedDictionary<string, object?> parameters`. The keys `routing_`, `database_`, and `params` are driver options inside that dictionary. `Neo4jDriver.ExecuteQueryAsync` reads them as `neo4j_driver.py:161-170` does, but from a copy: Python `**kwargs` is a new dict for each call, so the method never changes the dictionary of the caller. It builds the merged parameters as a new dictionary: the entries of `params`, then the other entries. A key in both keeps its first position and takes the value of the other entries. A caller with no keyword argument passes `new()`. The port reads no other option key of the Python driver (`impersonated_user_`, `bookmark_manager_`, `auth_`, `result_transformer_`, `parameters_`), and it does not port the check for a key that ends with one `_`. No upstream caller passes such a key. |
| A `**kwargs` dict that a function only spreads into one call, where each caller writes the keys as literals (`commit_kwargs` of `ConcurrentIngestor._run_unit`) | One typed parameter for each key, in the order of the keys. A caller that does not write a key passes the default of the called function. |
| A name that ends in `_` beside the same name with no `_` (`Graphiti.search_` beside `search`) | The name that the public surface design gives the member: `AdvancedSearchAsync`. |
| `x: A \| list[A]` that the function makes a list on its first line and passes to no other function (`episode_uuid` of `build_episodic_edges`) | Two overloads, one for each shape. The union record of Part 3 is for a value that code passes on or branches on later. |
| A wire name: a JSON key, a node property, a Cypher parameter, a prompt name, a span attribute | Keep the Python text exactly. Never derive it from a C# name. |
| A private name that a ported test, a recording test, or a judge case reads (`_SYSTEM_PROMPT`, `_clean_input`, `_get_cache_key`, `_map_answers`) | `internal`, not `private`. A test project cannot name a private member. |
| Two names of one class that give the same C# name after this table (`generate_response` and `_generate_response`) | The private one gets the prefix `Private`: `PrivateGenerateResponseAsync`, `PrivateEnsureChatPrompt`. |
| A private attribute and a method of one class whose C# names differ only in the case of the first letter (`_flat_overrides` and `flat_overrides()` of `LLMModel`) | The private one gets the prefix `Private`: `PrivateFlatOverrides`. The field keeps its place in the field order of Python (R7). |
| A member with the name of its enclosing type (error CS0542) | A field or a property gets the suffix `Value`, and a method gets the suffix `Function`: `SummaryValue`, `EvalPromptFunction`. A property whose type is a delegate, for example a `PromptFunction` member of a prompt interface, counts as a method and gets `Function`. |
| A nested class with the name of a member of the outer class (error CS0102) | The nested type gets the suffix `Group`, and a constant `PythonName` holds the Python name (R15). |
| `__call__`, `__repr__` | `Call(...)` and `Repr()` |
| A type alias (`PromptOverrides = dict[...]`, `PromptName = Literal[...]`) | No C# type. Each use is the aliased type. A `Literal` alias that a judge case reads with `__args__` is also a static list of its values in the static class of the module: `Names.PromptName` and `Names.PromptGroup`. An alias whose value is a union of models (`EdgeOperation` and `Operation` of `change_set.py`) is the abstract record of Part 3 with the name of the alias. If each case of one alias is a case of a second one, the first record derives from the second. |

A member keeps the meaning of its Python name. Do not rename a member to make it clearer.

### R3. Visibility and the public surface

- Every ported type and member is `internal`, or `private` by R2. One family is `public`: the exception classes of `errors.py` and `llm_client/errors.py` (R4), with their constructors and properties.
- [The public surface design](../docs/design-docs/public-surface-design.md) names each other public type. Those types are lead code, not ported code. Never add `public` to another ported type.
- The `PyCompat` project grants `InternalsVisibleTo` to the six assemblies in its project file. The core project grants it to `Graphiti.Net.Neo4j`, `Graphiti.Net.JudgeHost`, and `Port.Evaluation`. The Neo4j project grants it to `Graphiti.Net.JudgeHost`, `Port.Evaluation`, and `Graphiti.Net.E2E.Tests`. The three product projects grant it to `Graphiti.Net.E2E.Tests`, because a test on Neo4j uses the internal driver and the internal models until the public facade exists (layer 9). A test that needs a real Neo4j is in `tests/Graphiti.Net.E2E.Tests`, never in `tests/Port.Evaluation`. Only the lead changes that list.
- `PyCompat` members are `internal` too. The `PyCompat` project grants `InternalsVisibleTo` to the two product projects and to the test projects.
- The core project never references `Neo4j.Driver`. A type from `Neo4j.Driver` appears only in `src/Graphiti.Net.Neo4j`.
- The public facade `IGraphiti` is lead code. It maps public records to ported types and calls `GraphitiCore`. A ported unit never refers to a public record or to `IGraphiti`.
- A custom entity or edge type (GAPS G6) reaches ported code as the internal `GraphTypeDescriptor` (R15, decision D26).

### R4. Exceptions

Each Python exception class becomes one C# class. The name ends in `Exception`, which analyzer CA1710 requires.

| Python | C# | Base class |
| --- | --- | --- |
| `GraphitiError` | `GraphitiException` | `Exception` |
| `EdgeNotFoundError`, `EdgesNotFoundError`, `GroupsEdgesNotFoundError`, `GroupsNodesNotFoundError`, `NodeNotFoundError`, `SearchRerankerError`, `EntityTypeValidationError`, `IdentityPropertyValidationError`, `GroupIdValidationError`, `UnitExpiredError`, `UnitInProgressError`, `StaleCommitError` | The same name with `Exception` in place of `Error` | `GraphitiException` |
| `NodeLabelValidationError(GraphitiError, ValueError)` | `NodeLabelValidationException` | `GraphitiException`. C# has one base class. No upstream `except ValueError` in hand-written code receives this class (7 sites, read on 2026-11-25). Pydantic does catch it as a `ValueError` when a field validator raises it (`nodes.py:110`, `search/search_filters.py:78`), and R7 covers those two sites with `Pydantic.Wrap<NodeLabelValidationException>`. |
| `ChangeSetRejected` | `ChangeSetRejectedException` | `GraphitiException` |
| `RateLimitError`, `RefusalError`, `EmptyResponseError` (`llm_client/errors.py`) | `RateLimitException`, `RefusalException`, `EmptyResponseException` | `Exception` |
| `OpenAIDecisionsInputTooLongError`, `_OpenAIInputTooLongSignal` | `OpenAIDecisionsInputTooLongException`, `OpenAIInputTooLongSignalException`. Both stay `internal` in the file of their module (decision D32). | `Exception` |

- The first constructor takes the same parameters as the Python `__init__`, in the same order, with the same defaults. A class with no `__init__` of its own, for example `GraphitiError`, takes `string message = ""`, because Python accepts `GraphitiError()` and its text is empty.
- Each class also has a second constructor for `raise X(...) from e`: the same parameters with no default, and then `Exception? innerException`. Example: `ChangeSetRejectedException(string reason = "change set rejected")` and `ChangeSetRejectedException(string reason, Exception? innerException)`. A call with no argument then binds to the first constructor.
- A call with two or more positional arguments to a class with no `__init__`, for example `GraphitiError('a', 'b')`, has no C# form. No upstream site makes such a call.
- A `list[str]` parameter is `List<string>`, and an `int` parameter is `long` (R6). One exception: `validate_group_ids` takes an `IEnumerable<string?>?`. Its items can be `None`, because `validate_group_id` declares `str | None`, and a `List<string?>` parameter refuses the `List<string>` of each caller (compiler warning CS8620).
- If `__init__` builds the message before it calls `super().__init__`, a private static method of the class builds it, for example `BuildMessage`.
- Build the message with the same text as Python. GAPS J1 records `str(exception)` for each class.
- `self.message` is the `Message` property. Each other attribute, for example `group_id` on `UnitInProgressError`, is a get-only property.
- A list inside a message is Python `repr` text: `f'... {uuids}'` is `"... " + Py.Repr(uuids)`.

Built-in exceptions:

| Python | C# to throw or catch |
| --- | --- |
| `ValueError` | `ArgumentException(message)` |
| `TypeError` | `InvalidCastException(message)` |
| `KeyError` | `KeyNotFoundException`. A dictionary indexer throws it for a missing key. |
| `IndexError` | `ArgumentOutOfRangeException`. A `List<T>` indexer throws it. |
| `AttributeError` | `MissingMemberException(name)` |
| `NotImplementedError` | `NotImplementedException` |
| `zlib.error` | `InvalidDataException`, thrown by `PyZlib.Decompress` |
| `sqlite3.OperationalError`, `sqlite3.ProgrammingError` | `SqliteException` of `Microsoft.Data.Sqlite` |
| `RuntimeError` | `InvalidOperationException(message)` |
| `ZeroDivisionError` | `DivideByZeroException`. `Py.TrueDiv`, `Py.FloorDiv`, and `Py.Mod` throw it. |
| `OverflowError` | `OverflowException`. The `PyDateTime` helpers throw it for a date outside year 1 to 9999. |
| pydantic `ValidationError` | `Graphiti.PyCompat.ValidationException`, which derives from `ArgumentException` as pydantic's class derives from `ValueError` |
| `json.JSONDecodeError` | `System.Text.Json.JsonException`. `PyJson.Loads` throws it. In Python it is a `ValueError`, so an `except ValueError` around `json.loads` becomes two `catch` clauses. |
| `SyntaxError` from `ast.literal_eval` | `FormatException`. `PyAst.LiteralEval` throws it. |
| `asyncio.CancelledError` | `OperationCanceledException` |
| `asyncio.TimeoutError`, `TimeoutError` | `TimeoutException` |
| `AssertionError` that a test fixture raises (`AsyncMock(side_effect=AssertionError(...))`) | `Xunit.Sdk.XunitException` in the C# fixture. No product code raises it. |
| `neo4j` driver errors (`ClientError`, `DatabaseError`) | The `Neo4j.Driver` exception as it arrives. Never wrap it or translate it in product code. |
| `UnicodeDecodeError` from `b.decode()` | `DecoderFallbackException`. `PyText.DecodeUtf8` throws it in strict mode for bytes that are not UTF-8. It derives from `ArgumentException`, as the Python class derives from `ValueError`. |
| `UnicodeEncodeError` from `s.encode()` | `EncoderFallbackException`. `PyText.EncodeUtf8` throws it. It derives from `ArgumentException`, as the Python class derives from `ValueError`. |
| `httpx.HTTPStatusError`, also `error.response.status_code` | `HttpRequestException` with a `StatusCode` that is not null. To raise one: `new HttpRequestException(message, null, statusCode)`. To catch one: `catch (HttpRequestException e) when (e.StatusCode is not null)`. |
| `httpx.TransportError` | `HttpRequestException` with a null `StatusCode`, or an `IOException`, which a failed body read throws: `catch (Exception e) when (e is HttpRequestException { StatusCode: null } or IOException)` |
| `httpx.TimeoutException` | `TimeoutException`, which `Asyncio.WaitForAsync` throws, or an `OperationCanceledException` for which `Asyncio.IsCancellation(e, cancellationToken)` is false, which `HttpClient` throws for its own timeout: `catch (Exception e) when (e is TimeoutException \|\| (e is OperationCanceledException && !Asyncio.IsCancellation(e, cancellationToken)))` |
| `openai.RateLimitError` | An exception that the classifier of the chat adapter names a rate limit (decision D31). Ported code reaches the classifier through `PortServices.ClassifyModelFailure(e)`, which returns `ModelCallFailure.RateLimit`, `ServerError`, or `None`. The one ported site is `cross_encoder/openai_reranker_client.py:120`. The chat adapter is lead code: for a server error it throws an `HttpRequestException` with the status 500 and the provider exception inside, so that the ported `is_server_or_retry_error` retries it by the first row. |

Catch clauses:

| Python | C# |
| --- | --- |
| `except X:` | `catch (XException)` |
| `except (A, B):` | `catch (Exception e) when (e is AException or BException)` |
| `except Exception:` | `catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))`. Python's `Exception` does not include a cancellation. The helper is true only for an `OperationCanceledException` while the token is canceled. A timeout of `HttpClient` is also an `OperationCanceledException`, and Python catches a timeout, so do not test the type alone. In a sync method with no token, write `catch (Exception)`. |
| `except BaseException:` | `catch (Exception)` |
| `except NotImplementedError: pass` around a driver operation | `catch (NotImplementedException)`. See R24 for the skeleton, which must not throw this type. |
| `raise X(...) from e` | `throw new XException(..., e)` |
| Bare `raise` | `throw;` |

One limit of the integer type. A Python `int` has no size limit, and the port uses `long` (R18).
`Py.Int` and `Py.FloorDiv` throw `OverflowException` for an integer that a 64-bit `long` cannot hold, where Python returns a value.
Python int arithmetic that can pass 64 bits, where Python then compares the result, runs in `Int128`: `max_len * LIST_TOTAL_LENGTH_MULTIPLIER` in `attribute_utils`.
An int outside 64 bits in a model reply throws `OverflowException` at the read (GAPS F41).
`OverflowException` does not derive from `ArgumentException`, so a `catch` for a Python `ValueError` does not catch it.
Example: `utils/maintenance/attribute_utils.py:67` calls `int(raw)` inside `except ValueError`. For the environment value `99999999999999999999` Python gets an int, and the port throws out of the function.
The maintainer chose on 2026-11-26 to document this limit and not to change the helpers. Add no `catch` for it in ported code.

Do not add an argument guard that Python does not have. Do not replace an exception with a result type.
An exception that Python raises with no argument has an empty text: `raise NotImplementedError` is `throw new NotImplementedException(string.Empty)`. The .NET default message is not empty.
A test of the text of a driver error, as `'EquivalentSchemaRuleAlreadyExists' in str(e)` is, tests `e.Code` and `e.Message`. The `{e}` of a driver error in a log line or a `print` is `str(e)` of the Python driver. For an error of the server it has the form `{neo4j_code: C} {message: M} {gql_status: G} {gql_status_description: D}`, built from the properties of the .NET exception in one private member (`Neo4jDriver.DriverErrorText`). Each other exception gives its `Message`. A test of such a text holds the literal of Python. Python puts the code of a Neo4j error into `str(e)`, and `Neo4j.Driver` keeps it in `ClientException.Code`.
`print(x)` is `Console.Out.WriteLine(x)` with the text that Python prints. Do not replace it with a log call. The one site is `Neo4jDriver.health_check`.
The product projects compile with `CheckForOverflowUnderflow`. A `+`, `-`, or `*` on a `long` that leaves the range throws `OverflowException`, by the limit above, and never wraps to a wrong value. Code that needs a wrap, for example a hash, says `unchecked`. The switch also checks a cast: `(long)x` throws `OverflowException` for a `double` that is `nan`, infinite, or out of range. Python raises `ValueError` for `int(nan)` and `math.ceil(nan)`, so that one exception type differs. The `PyCompat` assembly does not compile with the switch, and its helpers check their own ranges.
A method call on a value from open data that can be `None` raises `AttributeError` in Python: `record.get('group_id').replace(...)` at `nodes.py:1166`. Write `Py.NotNone(value, "replace")`, which returns the value or throws `MissingMemberException("'NoneType' object has no attribute 'replace'")`. A method call on a value from open data that can have another type is `Py.Receiver<T>(value, "<method>")`, where `T` is the C# type of the Python type that has the method: `Py.Receiver<OrderedDictionary<string, object?>>(x, "get")`, `Py.Receiver<List<object?>>(labels, "remove")`, `Py.Receiver<string>(groupId, "replace")`. It returns the typed value, or throws `MissingMemberException` with the text of Python for `None` and for each type that has no method of that name. A type that has a method of that name with another signature, for example a list for `pop`, is a difference (GAPS F24). Use it at each method call on a record value in a record parser and on a value that a JSON reply gives (`decision_prompts.py`). Never write a typed read (`Py.AsDict`) at such a site: it throws `TypeError` where Python raises `AttributeError`. A value of another container type that has the method in Python, for example a set as `labels`, also throws (GAPS F24).
An argument of `super().__init__` that `__init__` builds with statements before the call is a private static method of the class, as for a message: `: base(ResolveConfig(config))` in `OpenAIDecisionsClient`. An error that Python raises before `super().__init__` then also comes before the base constructor.
`int(x)` on a value from open data is `Py.Int(x)`. A list or a dict from open data in a place that needs a hash, for example `x in {...}`, throws `InvalidCastException` with the text `unhashable type: 'list'` or `unhashable type: 'dict'`.
A base URL with no `http` or `https` scheme gives a transport error in httpx, which `openai_decisions_client.py` retries. `HttpClient` throws at once. The lead accepts the difference.
`Codec.PythonExceptionName` in the judge host maps each C# type back to its Python name. Part 5 lists the rows that it lacks.

### R5. Logging

Upstream has `logger = logging.getLogger(__name__)` in 34 modules and 130 log calls.

- Each module that logs has `private static ILogger Logger => PortServices.Logger(typeof(<ModuleClass>));`. `PortServices` is the ambient holder of decision D23.
- Each log call becomes a source-generated method: an `internal static partial` method with `[LoggerMessage]` in a nested `private static partial class Log` at the end of the module class. The method cannot be `private`, because the module class calls it.
- If a class of the file that is not the module class logs, for example `EpisodeTypeExtensions` in `nodes.py`, the `Logger` property and the class `Log` of the module class are `internal`, and that class calls them.
- Keep the level: `debug` is `Debug`, `info` is `Information`, `warning` is `Warning`, `error` is `Error`. `logger.exception` and `exc_info=True` pass the exception as the first argument after the logger.
- Keep the message text. Each `{expression}` of an f-string and each `%s` becomes a named placeholder: `f'Saved Node to Graph: {node.uuid}'` gives `"Saved Node to Graph: {Uuid}"`.
- Format a list or a dict argument with `Py.Repr` so that the text is the same as Python's.
- A format spec is applied before the call. `%d` and `%s` take the value. `%r` takes `Py.Repr(value)`. `%.30s` and an f-string spec such as `{x:.0f}` take `Py.Format(value, spec)`. The placeholder then holds the finished text.
- `extra={...}` becomes `using (Logger.BeginScope(dictionary))` around the call, with the same keys.
- If an argument needs work to compute, for example a list of UUIDs or a `Py.Repr` call, guard the call with `if (Logger.IsEnabled(LogLevel.<Level>))`.
- If the analyzer rule CA1873 flags that guard, read the logger into a local first: `var logger = Logger; if (logger.IsEnabled(LogLevel.Debug)) { Log.X(logger, ...); }`. `SearchUtils.cs` shows the form.
- A message that starts with a level word, for example `WARNING: `, fails the build with LOGGEN008. Put the word in a first placeholder, `{Prefix}`, and pass the text `"WARNING: "`. The line that the logger writes stays the same.
- Set no event id. Add no log call that Python does not have, and remove none.
- Security: if the level is above `Debug` and an argument can hold episode text, a prompt, a fact, a model reply, or a credential, port the call and name it in the unit's `notes` in the queue. The lead decides before the unit is `reviewed`. See [SECURITY.md](../docs/SECURITY.md).
- Tracing follows the same security rule for span attributes. Keep each span name and attribute key exactly (GAPS J6).

### R6. The Python value model

Decision D21. Upstream passes open data between model replies, node attributes, prompt contexts, and query parameters.
The port keeps such data as `object?` values from one closed set. `Neo4j.Driver` does not accept each type of the set as a query parameter. The Neo4j project converts the values at the driver boundary (R9).

Use the value model only where the Python annotation is `Any`, `dict[str, Any]`, `list[Any]`, a bare `dict` or `list`, or where [TYPE_HINTS.md](TYPE_HINTS.md) infers such a type.
Typed data stays typed: `dict[str, list[str]]` is `OrderedDictionary<string, List<string>>`.
`x in c`, `c[k]`, `c.get(k)`, and `a + b` on two values of open data have no `Py` helper yet. `DecisionPrompts.cs` is the one site, and it has four private members for them (`Has`, `At`, `Get`, `Concat`), each with a `// PORT:` comment. They throw what Python throws. If a second unit needs one, the lead moves them into `PyCompat` first.
The truth test of a typed parameter has a typed form: `if not uuids` on `list[str]` is `uuids.Count == 0`, and `if uuid_cursor` on `str | None` is `!string.IsNullOrEmpty(uuidCursor)`.
A typed parameter that Python binds again to a value from a record (`group_ids` in `get_community_clusters`) gets a local of open data for that value: read it with `Py.Iter`, and pass each item on as it is.
A call with named arguments and a spread, `f(a=1, **d)`, builds the dictionary with the named keys first and then calls `Add` for each key of `d`. `Add` throws for a key that is there, where Python raises `TypeError`. No filter key of `search_utils.py` is also a named key.
A result row of FalkorDB is an ordered dictionary in the driver contract, where the Python driver gives a list. `dict(zip(header, row, strict=True))` is `Py.ZipStrict(Py.Iter(header), Py.Iter(row))`, which takes the keys of a dictionary row as Python does. The FalkorDB driver is out of the MVP, and its unit decides the row type.

| Python value | C# runtime type |
| --- | --- |
| `None` | `null` |
| `bool` | `bool` |
| `int` | `long`. `PyJson.Loads` gives `BigInteger` for a value outside the range of `long`. |
| `float` | `double` |
| `str` | `string` |
| `list` | `List<object?>` |
| `tuple` | `PyTuple` |
| `dict` with string keys | `OrderedDictionary<string, object?>` |
| `dict` with other keys | `PyDict` |
| `datetime`, aware or naive | `PyDateTime`. See R11. |
| `date` | `DateOnly` |
| `bytes` | `byte[]` |
| An enum member | The C# enum value. `Py.EnumValue` gives its wire value. |

- Only `PyCompat` formats, parses, compares, or copies these values. Never call `ToString()`, `JsonSerializer`, or `Convert` on one.
- Read a typed value out of open data with the `Py.As*` helpers. They throw the exception that Python raises for the wrong type.
- `Py.AsDateTime` reads a datetime. `Py.AsDate` reads a date and refuses a datetime, although Python counts a datetime as a date.
- `d.get(k)` on a dict with values of a value type, for example `dict[str, int]`, is `Py.GetNullable(d, k)`. It gives null for a missing key, where `Py.Get` gives 0.
- A `nan` in a `double` variable is boxed again at each call. A `PyDict` or a `PySet<T>` does not find it again, where Python finds the same object. No MVP module uses a `nan` key.
- `{**a, **b}` is a new dictionary with the entries of `a`, then the entries of `b`. A key of `a` keeps its position and takes the value of `b`.
- `d.get(k)`, `d.get(k, default)`, `d.setdefault`, `d.pop`, and `d.update` become the matching `OrderedDictionary` calls. `d[k]` on a missing key throws `KeyNotFoundException`.
- `copy.deepcopy(x)` is `Py.DeepCopy(x)`. `copy.copy(x)` and `dict(x)` are a new collection with the same items.
- A list or a dict is shared by reference, as in Python. Do not copy one unless Python copies it.
- A typed list or set goes into a parameter dictionary as it is: `["uuids"] = uuids`. Never write `.Cast<object?>().ToList()`, which is a copy.

`for x in v` and `sep.join(v)` on a value from open data are `Py.Iter(v)`: `foreach (var x in Py.Iter(v))` and `string.Join(sep, Py.Iter(v).Select(Py.AsString))`. `Py.Iter` gives the items of a list or a tuple, the items of a set in insertion order (Python gives them in hash order, the difference of decision D43), the keys of a dict, the code points of a `str`, and the ints of `bytes`, and it throws the `TypeError` of Python for each other value. A typed read (`Py.AsList`) is wrong there, because it refuses the other types that Python accepts. The text of a `TypeError` of `sep.join(v)` is not kept: for a value that is not iterable the port gives the text of the `for` loop (`'float' object is not iterable`, where Python says `can only join an iterable`), and for an item that is not a `str` it gives the text of `Py.AsString`. The exception type is the same. A comprehension `[x for x in v]` over an open value is `Py.Iter(v).ToList()`. Use `Py.AsList` for a value that the port built itself.

### R7. Pydantic models and validators

Decision D21 keeps the library choice: records with an explicit `Validate()`. `JsonSerializer` does not bind models.

| Pydantic | C# |
| --- | --- |
| `class M(BaseModel)` | `internal sealed record M`. A base model with subclasses is an `abstract record`. |
| A model that defines `__eq__` or `__hash__` (`Node`, `Edge`, and their subclasses) | A `class`, not a record. A record cannot declare `Equals(object)`, and its own `Equals` is false for another runtime type. See R18. |
| A frozen dataclass with `field(compare=False)` (`LLMModel`) | A sealed record with `Equals(T?)` and `GetHashCode` over the compared fields only. Both members are `public`, because C# asks for that on a record. A field whose class has no `__eq__` (`LLMTransport`) compares by reference. |
| A field | A property in the same order. `{ get; set; }`, because pydantic models are mutable. `{ get; init; }` only for `frozen=True`. |
| A required field | `required` |
| `Field(default=x)`, `= x` | Property initializer `= x` |
| `Field(default_factory=list)`, `dict`, `lambda: utc_now()`, `lambda: str(uuid4())` | Property initializer: `= []`, `= new()`, `= DatetimeUtils.UtcNow()`, `= Py.Uuid4()` |
| `Field(description=...)` | A row in the model's schema (below). Keep the text exactly. |
| `Field(ge=1)`, `gt`, `le`, `lt`, `max_length`, `min_length` | A test in `Validate()` that throws `ValidationException` |
| `@field_validator` | A private static method that the reader of the field and `Validate()` both call (below). The two sites are in the table below. |
| `@model_validator(mode='after')` | Code in `Validate()`, in the order of the Python file. The one site is in the table below. |
| `M.model_construct(...)`, which upstream uses only in tests | `new M { ... }` with no call to `Validate()` |
| `PrivateAttr(default=0)` | An `internal` property with the comment `// PORT: PrivateAttr`. It is not a model field: `ModelDump`, `ModelFields`, and the schema leave it out. |
| A `@classmethod` of a base model with the body `...` that each subclass replaces (`Node.get_by_uuid`, `Node.get_by_uuids`, `Edge.get_by_uuid`) | No member on the base, and a `// PORT:` comment at its place. A C# static method does not override, and a call on a subclass with the token in the third position binds to a base method that does nothing. |
| A `@classmethod` that a subclass inherits and that passes `cls` on (`Node.delete_by_group_id`, `Node.delete_by_uuids`, `Edge.delete_by_uuids`) | A static method on the base that passes `typeof(Node)` or `typeof(Edge)`. Each upstream caller calls it on the base class. |
| A field of the type `type[BaseModel]` | A property of the type `IResponseModel` (below). The reader is `x => Pydantic.SubclassOf<IResponseModel>(x)`. The method group does not compile (`error CS0411`). For `type[BaseModel] \| None`, as `PromptSpec.response_model` is, the property is `IResponseModel?` and the reader is `v => Pydantic.Optional(v, x => Pydantic.SubclassOf<IResponseModel>(x))`. |
| `ConfigDict(validate_assignment=True)` | The `set` accessor of each field that has a validator calls the same validation. Pydantic also checks the type at each assignment. A value that has the type of the property needs nothing. A value from open data, for example a field of a database record, is read under the class name and the field name: `var f = new PydanticFields("EntityNode", new() { ["name_embedding"] = value });`, then `f.Required("name_embedding", <reader>)`, `f.ThrowIfAny();`, and the assignment. `Node` and `Edge` have this configuration (`nodes.py:105`). |
| `ConfigDict(arbitrary_types_allowed=True)` | Nothing |
| `M(**data)` or `M.model_validate(data)` with open data | `M.ModelValidate(data)` |
| `M(a=1, b=2)` with typed arguments | `new M { A = 1, B = 2 }.Validate()` |
| `m.model_copy()`, `m.model_copy(update={...})` | `m with { }`, `m with { X = ... }`. Neither side validates. For a model that is a class, write `ModelCopy()` with `MemberwiseClone`, then assign the updated properties. |
| `Annotated[A \| B, Field(discriminator='op')]` | An `abstract record` with one derived record for each case. `ModelValidate` of the base reads the discriminator key and calls the `ModelValidate` of that case. An unknown value throws `ValidationException`. |
| `m.model_dump_json()` | `Pydantic.DumpJson(m.ModelDump(json: true))`. Pydantic writes JSON with no spaces, which differs from `json.dumps`. |
| `M.model_validate_json(text)` | `M.ModelValidate(Pydantic.LoadJson(text))` |
| `m.model_dump()`, `m.model_dump(mode='json')` | `m.ModelDump()`, `m.ModelDump(json: true)`. See below. |
| `M.model_json_schema()` | `M.ModelJsonSchema` |
| `M.model_fields` | `M.ModelFields`, an `OrderedDictionary<string, PydanticField>` in field order with the Python names as keys. Code reads it by name, by count, and by key order. |
| `__eq__` and `__hash__` on `Node` and `Edge` | `Equals` and `GetHashCode`. See R18 and R21. |

Each model has these members after its properties, in this order. Add a member only if upstream or a judge case needs it.
After them come the methods of the Python class in the order of the Python file: each validator method, each `@classmethod`, and each instance method. Do not put `ModelJsonSchema` or `ResponseModel` above the properties.

1. `internal M Validate()`. It runs the constraints and the validators, and returns `this`. Every construction path calls it.
2. `internal static M ModelValidate(OrderedDictionary<string, object?> data)`. It is the Python constructor for open data. It reads each field with a `Pydantic.*` helper, applies defaults, ignores unknown keys, and ends with `.Validate()`. A missing required field, a `None` for a field that is not optional, or a value that lax mode cannot coerce throws `ValidationException`.
3. `internal OrderedDictionary<string, object?> ModelDump(bool json = false)`. Keys are the Python field names in field order. A nested model is dumped the same way. With `json: true`, each value passes through `Pydantic.JsonValue`, which gives what pydantic's JSON mode gives: an enum becomes its value, and a UTC datetime ends in `Z`, not in `+00:00`.
4. `internal static readonly PydanticSchema ModelJsonSchema`. Build it with the `PydanticSchema` builder in field order, with each `description`, `default`, and `title` that Python has. The judge function `judge:schema_text` pins the text for each response model (GAPS G3).

The `Pydantic.*` helpers apply the coercion of pydantic lax mode at the pinned version. Their tests compare them with values that Python recorded. Do not write a coercion by hand.

How to write the members with the helpers of `PyCompat`:

- A model implements `IPydanticModel`, so that `Pydantic.Dump` can dump it inside another model. Add the explicit member `OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);` after `ModelDump`.
- `ModelValidate` creates `var f = new PydanticFields("<Python class name>", data);`. It reads each field in field order: `f.Required("<python name>", <reader>)` for a required field, and `f.Default("<python name>", <reader>, () => <default>)` for a field with a default or a default factory. It then calls `f.ThrowIfAny();`, which throws one `ValidationException` with each error, as pydantic does. Only after that call does it read `.Value` of each field, build the record, and call `.Validate()`.
- A `@field_validator` is a private static method of the model with the name of the Python validator: `private static T ValidateX(T value, string? field = null)`. It runs the validator code inside `Pydantic.Wrap(..., "<Python class name>", field)` and returns the value. The reader of that field in `ModelValidate` calls it after the type read, with no field: `f.Default("node_labels", v => ValidateNodeLabelFilters(Strs(v)), () => null)`. `PydanticFields` then keeps its error with the errors of the other fields in field order, as pydantic does. `Validate()` calls the same method with the Python field name, so that a typed construction validates too. `SearchFilters` in `Core/Search/SearchFilters.cs` shows the form.
- A validator on an abstract base model, as `Node.validate_labels` is, has two forms, because the pydantic error names the class of the instance. The base declares `internal abstract string PythonName { get; }`, and each derived model returns its Python class name. The static form takes the class name: `internal static List<string> ValidateLabels(List<string> value, string pythonName, string? field = null)`. The reader of the field in each `ModelValidate` calls it with the literal Python class name, so that its error stays with the errors of the other fields. The instance form calls the static form with `PythonName`, and `Validate()` and the `set` accessor call the instance form.
- A `@model_validator(mode='after')` runs in `Validate()` only, after the field reads pass.
- A reader is a `Pydantic.*` member or a lambda of them: `Pydantic.Str`, `Int`, `Float`, `Bool`, `DateTime`, `Any`, `Enum<T>`, `v => Pydantic.Literal(v, "a", "b")`, `Pydantic.StrIntFloat`, `v => Pydantic.Optional(v, Pydantic.Str)` for a reference type or `Pydantic.OptionalValue` for a value type, `v => Pydantic.List(v, Pydantic.Str)`, `v => Pydantic.Dict(v, Pydantic.Any)`, `v => Pydantic.Model(v, "<Python class name>", M.ModelValidate)`, and `Pydantic.Tagged` for a discriminated union.
- `Validate()` of a model with a constraint creates `var f = new PydanticFields("<Python class name>");` with no data, calls `f.Ge`, `f.Gt`, or `f.MaxLength` for each constraint in field order, and then `f.ThrowIfAny();`. A model with no constraint and no validator has `internal M Validate() => this;`.
- `ModelDump` is `Pydantic.DumpFields(json, ("<python name>", Property), ...)` in field order.
- `ModelJsonSchema` is a `PydanticSchema("<Python class name>", <docstring or null>)` with one call for each field in field order: `.Required(name, type, description)`, `.Default(name, type, value, description)`, or `.Factory(name, type, description)`. The type is a `PydanticType` member: `Str`, `Int`, `Float`, `Bool`, `DateTime`, `Any`, `Null`, `List(...)`, `Dict(...)`, `Optional(...)`, `Union(...)`, `Literal(...)`, `Enum<T>()`, and `Model(M.ModelJsonSchema)`.
- `ModelFields`, if upstream reads `model_fields` of the model, comes after `ModelJsonSchema`: one `new PydanticField(isRequired, maxLength)` for each Python field name.
- Worked examples: `tests/Graphiti.Net.Property.Tests/PydanticModels.cs` and `PydanticSchemas.cs` hold test copies of 17 upstream models, which the tests compare with pydantic. They show each reader and each schema form. Where a copy and this rule differ in the order of members, this rule decides.

Facts that the Python class does not show, and that a schema needs:

- The value of a constant that the class imports for a default, for example `DEFAULT_MIN_SCORE` or `MAX_SUMMARY_CHARS`. Use the ported constant, not a number.
- A docstring is the text that `inspect.cleandoc` gives: no indent, and no empty first or last line. This also holds for the docstring of an enum.
- An int default is a `long` (`0L`), also for a `float` field with the default `0`. Pydantic writes `"default": 0` there, not `0.0`.
- `Factory` for `default_factory`, which writes no `default` key, and `Default` for each other default.
- In a union with `None`, `Null` is the last type.

Differences from pydantic that the helpers keep, each one outside what upstream can show:

- `Pydantic.LoadJson` rejects more than 201 nested containers, as pydantic does.
- The datetime reader rejects a digit text of 20 digits or more. Pydantic reads it with 64-bit arithmetic that wraps. No upstream reply field is a `datetime`.
- An int that a `long` cannot hold throws `OverflowException` (R4), unless another field or item of the same data is not valid. In that case the reader throws the `ValidationException` that pydantic throws.
- A validator that throws `ValidationException` keeps its own title and location. Pydantic puts the outer field in front.
- `Pydantic.Dump` returns a value outside the value model as it is, for example a delegate. In JSON mode it throws `ArgumentException`.

Validators at the pinned commit:

| Site | Python | C# |
| --- | --- | --- |
| `nodes.py:107` | `@field_validator('labels')` on `Node`, with `validate_assignment=True` | A validator method that calls `Helpers.ValidateNodeLabels` inside `Pydantic.Wrap<NodeLabelValidationException>`. The reader of `labels`, `Validate()`, and the `set` accessor of `Labels` call it. |
| `search/search_filters.py:75` | `@field_validator('node_labels')` on `SearchFilters` | A validator method that calls `Helpers.ValidateNodeLabels(NodeLabels)` inside `Pydantic.Wrap<NodeLabelValidationException>`. The reader of `node_labels` and `Validate()` call it. |
| `ingestion/config.py:77` | `@model_validator(mode='after')` on `ConcurrentIngestionConfig` | `Validate()`, after the field constraints, inside `Pydantic.Wrap` |

Pydantic catches a `ValueError` that a validator raises and raises `ValidationError` in its place. `NodeLabelValidationError` is a `ValueError` in Python, so a bad label gives `ValidationError`, and 16 judge cases expect that.
`Pydantic.Wrap(() => ...)` runs the validator code. It catches `ArgumentException` and throws `ValidationException` with the caught exception inside. `Pydantic.Wrap<TException>(() => ...)` also catches `TException`. `PyCompat` cannot name `NodeLabelValidationException`, which is a core type, so the call site names it. A call to `Helpers.ValidateNodeLabels` outside a model still throws `NodeLabelValidationException`.
A record compares a list or a dict property by reference. If upstream compares two models with `==`, stop and tell the lead. If only an upstream test compares them, the ported test compares the `ModelDump` of each side.

A response model that code passes as a value (`response_model: type[BaseModel]`) is an `IResponseModel`. The lead writes the interface and one class in `src/Graphiti.Net/Internal/`, namespace `Graphiti.Internal`:

```csharp
internal interface IResponseModel
{
    string PythonName { get; }
    PydanticSchema ModelJsonSchema { get; }
}

internal sealed class BuiltinResponseModel(string pythonName, PydanticSchema modelJsonSchema) : IResponseModel
```

- A built-in response model is a model that upstream passes as `response_model`, or that a judge case names by `$ref` for a response model parameter. A model that is only a field type of another model is not one, and it gets no `ResponseModel` member. Each built-in response model has one more member after `ModelJsonSchema`: `internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("<Python class name>", ModelJsonSchema);`.
- Pass `M.ResponseModel` where Python passes the class `M`. It is one object, so `a is b` on two response models is `ReferenceEquals(a, b)`, which `prompts/lib.py:355` needs.
- `response_model.model_json_schema()` is `responseModel.ModelJsonSchema`. `response_model.__name__` is `responseModel.PythonName`.
- The interface has no `ModelValidate`. Code that validates a reply names the model: `M(**data)` is `M.ModelValidate(data)`. A custom type validates through its descriptor (R15).

Other shapes:

| Python | C# |
| --- | --- |
| `@dataclass` | `internal sealed record` with `{ get; set; }` properties. `frozen=True` gives `{ get; init; }`. `field(default_factory=...)` is a property initializer. |
| `TypedDict` | `internal sealed record` with the same field names. If the code reads it by a string key, use `OrderedDictionary<string, T>`. |
| `Enum`, `str` enum | A C# `enum` with the members in the same order. Each member has `[PyEnumMember("<python name>", <value>)]` with the Python member name and its value. `member.value` is `Py.EnumValue(member)`, and `E(value)` is `PyEnum.FromValue<E>(value)`. Write no `<Enum>Values` class. Never use `Enum.ToString()` or `Enum.Parse` for a wire value. |
| A `@staticmethod` or a `@classmethod` of an `Enum` class (`EpisodeType.from_str` is the one MVP site) | A static method of the static class `<Enum>Extensions`, which follows the enum in the file: `EpisodeTypeExtensions.FromStr(string episodeType)`. A C# enum cannot hold a method. The docstring of the enum class is the XML comment of the enum. |
| `Literal['a', 'b']` | `string`, with a test in `Validate()` |
| `M(a=record['a'], ...)` where a value is open data: `Any`, a record field, or a result of `parse_db_date` | Build an `OrderedDictionary<string, object?>` with the Python keys in call order, and call `M.ModelValidate`. A typed `new M { ... }` has no place to raise `ValidationError`. The record helpers of `nodes.py` and `edges.py` are such sites, and 6 judge cases expect the error. |
| A list or a dict value that enters a model | Pydantic copies it at construction, and also at assignment for a model with `validate_assignment` (`Node`). `ModelValidate` stores a shallow copy of each list and dict value: `[.. value]` or `new(value)`. The value of a `Pydantic.List` or `Pydantic.Dict` reader is a new object already, so `ModelValidate` stores it as it is. So does the set accessor of each list or dict property of a `validate_assignment` model. A model with no `validate_assignment` (`Edge`) keeps the reference at assignment, and so does a typed `new M { ... }` of it, where pydantic copies each list at construction. Where a caller can see that difference, the site copies the list with `[.. value]` and a `// PORT:` comment. The known sites are the two results of `ingestion/scheduler.py:259` to `:266` and `:335` to `:342`, whose lists the change set also holds. The lead accepts the difference at each other site. An assignment of open data to a typed field of a model with no `validate_assignment` reads the value with the reader of the field: `load_fact_embedding` (`edges.py:301`). Python stores the value as it is, so a value that the reader converts or rejects differs (`[1, 2]` becomes `[1.0, 2.0]`, and `'abc'` throws). Neo4j gives a list of floats or null there. On Neptune, an edge with no embedding loads as `[None]`, which the port rejects (milestone M5). |
| `m.model_copy()`, `m.model_copy(update={...})`, `m.model_copy(deep=True)` | `m.ModelCopy()` is `MemberwiseClone`, which also copies the `PrivateAttr` values. With `update`, assign each key after the copy, with no validation. For a `validate_assignment` model (`Node`), the `set` accessor validates and copies the value, where pydantic stores it as it is. No upstream site shows that difference (`ingestion/phases.py:1262`, `ingestion/delta.py:128`), and the lead accepts it. `ModelCopy(bool deep = false)` comes after `ModelJsonSchema` and `ModelFields`. With `deep`, it replaces each property that holds a list or a dict: `[.. x]` for a typed list of strings or numbers, whose items cannot change, and `Py.DeepCopy(x)` for a value-model list or dict. A unit that deep-copies a list of models keeps the identity of an item that the list holds two times, as the memo of `copy.deepcopy` does. The units of `nodes` and `edges` add `ModelCopy`, `ModelDump`, and `ModelValidate`, because `ingestion/phases.py`, `ingestion/delta.py`, `utils/bulk_utils.py`, and `search/search_utils.py` call them. |
| `__post_init__` of a dataclass | A method `Validate()`. The code that builds the value calls it, as for a model. |
| A `TypedDict` with `total=False` | A class with a nullable property for each key. Null stands for a missing key. |

A record with a list member or a dict member does not give the `==` of Python, because C# compares that member by reference. `EdgeSearchConfig(search_methods=[]) == EdgeSearchConfig(search_methods=[])` is `True` in Python and `False` in the port. No upstream site compares a search config or a `SearchResults` value. A unit that ports such a comparison stops and asks the lead.

### R8. Async, concurrency, and cancellation

Decision D22. asyncio runs all code on one thread, so upstream shares lists and dicts between concurrent calls with no lock.
The port runs all ported code on one process-wide exclusive `TaskScheduler`. Code between two `await` points then never runs at the same time as other ported code, as in Python.

- Add no lock and no concurrent collection where Python has none.
- Never call `Task.Run`, `Task.WhenAll`, `Task.WhenAny`, `Parallel.*`, `ConfigureAwait(false)`, `.Result`, or `.Wait()` in ported code. Each one leaves the scheduler or blocks it.
- `Asyncio.Run(func)` starts `func` on the scheduler and returns its task. It does not block the caller. Only the facade, the judge host, and tests call it.
- A library does not keep the scheduler for a lambda that it calls. A `Neo4j.Driver` work function and a Polly callback run on the thread pool (measured on 2026-11-25 with Neo4j.Driver 6.5.1 and Polly 8.6.6). Each lambda that ported code passes to a library starts with `Asyncio.OnLoopAsync(() => ...)`, which runs its body on the scheduler. This covers the work function of `ExecuteReadAsync` and `ExecuteWriteAsync` and each Polly callback.
- `Asyncio.GatherAsync` and `Asyncio.SemaphoreGatherAsync` throw `InvalidOperationException` if the caller is not on the scheduler. A missed `OnLoopAsync` then fails in a test and does not become a race.
- Host services run off the scheduler (decision D33). The lead's adapters in `Graphiti.Internal` call `IChatClient`, `IEmbeddingGenerator`, and a host `ICrossEncoder` through `Asyncio.CallHostAsync(token => ...)`. It starts the call on the thread pool and resumes on the scheduler. Ported code never calls it and never calls a host service directly. The ported reranker calls the lead seam `LogProbabilityChat` (R19).
- A host delegate that ported code calls in line (`enabled_for_group`, a prompt builder, the failure classifier) is a plain call. Add no wrapper.
- A canceled token stays canceled, and a Python cancellation arrives one time. Code that runs after a caught cancellation, for example a cleanup call in `except` or `finally`, passes `CancellationToken.None` to each call. The sites are `ingestion/phases.py:268` and the `finally` of `GraphDriver.transaction` (`driver/driver.py:167`), which closes the session. In `except BaseException` the caught error can be another error than a cancel. A new cancel of the caller must then still reach the cleanup call, as in Python. So the cleanup call there takes `Asyncio.IsCancellation(error, cancellationToken) ? CancellationToken.None : cancellationToken`, and an inner `except Exception` is `catch (Exception e) when (!Asyncio.IsCancellation(e, cleanupToken))`. The sites are `_run_unit` (`ingestion/scheduler.py:166`) and the cleanup of `admit_episode`, `admit_bulk`, `analyze_episode`, `analyze_bulk`, `commit_unit`, and `enrich_unit` in `ingestion/phases.py`. The `transaction` override of the Neo4j driver must do the same in its rollback and close path. With the canceled token, `Asyncio.ToThreadAsync` and `AsyncSemaphore.AcquireAsync` throw at once, and the cleanup does not run.
- Two more cleanup sites pass `CancellationToken.None`: the `finally` blocks of `add_nodes_and_edges_bulk` and of `execute_write_with_timeout` in `utils/bulk_utils.py`, which close the session.
- The work function of `execute_write_with_timeout` takes a token: `Func<Transaction, CancellationToken, Task>`. In the `WaitForAsync` branch it gets the token of the limit, so the limit stops the work, as the cancel of the task does in Python. On Neo4j it gets the token of the call, and the server holds the limit.
- `semaphore_gather(*[f(x[i]) for i in ...])` builds each coroutine before one runs. Read each argument that can throw, for example `episode_tuples[i]`, before the gather. The error then comes with zero calls, as in Python.
- A nested coroutine (`async def` inside a function) is a local async function.
- `max_coroutines or CONSTANT` is `maxCoroutines is null or 0 ? Constant : maxCoroutines`. A negative value goes on to the semaphore, which raises as in Python.
- The Neo4j driver calls `cancellationToken.ThrowIfCancellationRequested()` before each call of `Neo4j.Driver`: in `Neo4jTransaction.RunAsync`, `Neo4jSession.RunAsync`, `ExecuteWriteAsync`, `HealthCheckAsync`, and at the start of `TransactionAsync`. The package takes no token for these calls. `CloseAsync` has no check, because it is a cleanup call.
- `Neo4jSession.CloseAsync` and `DisposeAsync` drop the error of a result that no code read, as the `close` of the Python session does (GAPS F28).
- One token wakes its waiters in reverse order of registration. asyncio wakes them in order. This order is not a contract (GAPS G19).
- A `TaskCompletionSource` in lead code is created with `TaskCreationOptions.RunContinuationsAsynchronously`. Without it, a waiter runs inside `SetResult`, in the middle of code that has no `await`.

| Python | C# |
| --- | --- |
| `async def f(a, b=1)` | `internal static async Task<T> FAsync(A a, long b = 1, CancellationToken cancellationToken = default)` |
| An `async def` with no `await` in its body (each method of `InMemoryIngestionStateStore`) | A method with no `async` that returns `Task.FromResult(x)` or `Task.CompletedTask`. It takes the token and does not read it: a Python coroutine with no suspension point runs to its end, also with a cancel pending. An exception leaves at the call and not at the `await`. Each caller awaits the call at once, so no caller sees the difference. One caller does not: `_begin_admit` (`ingestion/phases.py:268`) starts the call with `asyncio.ensure_future` under `asyncio.shield`, and the port starts it with `Asyncio.CreateTask`. Both run the call one trip later, and the adversarial pass over layer 8 found no difference there. |
| A call to an async function | Pass `cancellationToken`. Analyzer CA2016 fails the build if you do not. |
| `semaphore_gather(*coroutines, max_coroutines=n)` | `Helpers.SemaphoreGatherAsync([() => F1Async(...), () => F2Async(...)], n, cancellationToken)`. Pass a lambda for each call. A C# async method starts when it is called, so a list of tasks ignores the limit. |
| `semaphore_gather` with coroutines of different result types | The one site is `search/search.py:174`, with four coroutines: the overload that takes four lambdas and returns a tuple of four. 43 of the 46 sites pass lists of one type, and 1 passes two coroutines that return `None`. |
| `asyncio.gather(*coroutines)` | `Asyncio.GatherAsync` with lambdas |
| `asyncio.gather(..., return_exceptions=True)` | `Asyncio.GatherAsync(..., returnExceptions: true, cancellationToken)`. Each result is a value or an exception. It does not throw for a canceled token, where Python raises `CancelledError`. |
| `asyncio.gather(task, return_exceptions=True)` on a task that exists (`ingestion/scheduler.py:173`, `ingestion/phases.py:321`) | `Asyncio.GatherAsync([task], returnExceptions: true)`. This form takes tasks, not lambdas. A lambda that returns the task adds one more trip through the queue than Python makes. |
| `asyncio.create_task(coro)`, `asyncio.ensure_future(coro)` | `Asyncio.CreateTask(token => ..., out var cancel)`. `task.cancel()` is `cancel.Cancel()`. Dispose `cancel` after the last use of the task, for example after the cleanup gather. |
| `asyncio.to_thread(f)` | `Asyncio.ToThreadAsync(f)`. It runs `f` on the thread pool and returns to the scheduler. In ported code it is the one allowed way to leave the scheduler. |
| `threading.Lock` | A `System.Threading.Lock` field with the `lock` statement. Keep it: code inside `to_thread` runs off the scheduler. |
| `asyncio.Semaphore(n)` | `AsyncSemaphore(n)` with `AcquireAsync(cancellationToken)`, `Release()`, and `Locked()`. It is a port of the class of CPython 3.12 (`asyncio/locks.py`), and it runs on the scheduler only. Waiters wake in the order of their calls. A woken waiter that a cancel reaches before it resumes gives its slot to the next waiter and throws. Never use `SemaphoreSlim`: its waiter resumes through the thread pool. In a measurement with a window of 2, two callers got their sequence numbers in the wrong order in 518 of 2,000 rounds. `AcquireAsync` throws for a token that is canceled at the call, also with a free slot. Python takes the free slot and raises at the next suspension. |
| `with suppress(asyncio.CancelledError): await task` | `try { await task; } catch (OperationCanceledException) { }` |
| `with suppress(asyncio.TimeoutError): await asyncio.wait_for(...)` (`ingestion/scheduler.py:91`) | `try { await Asyncio.WaitForAsync(...); } catch (TimeoutException) { }`. A cancel and each other error pass. |
| `asyncio.wait_for(x, timeout)` | `Asyncio.WaitForAsync(token => ..., timeout, PortServices.Time, cancellationToken)`. At the limit it cancels the token of the call and throws `TimeoutException` after the call ends. A call that ignores its token runs past the limit (open item O9). |
| `asyncio.shield(x)` | `Asyncio.ShieldAsync(task, cancellationToken)` for a task with a result. Upstream has one site, `ingestion/phases.py:270`. |
| `asyncio.sleep(s)` | `Asyncio.SleepAsync(s, PortServices.Time, cancellationToken)`. A delay of 0 or less yields one time, as Python does. A delay above about 49.7 days, and `inf`, wait until the token is canceled, where `Task.Delay` throws. `nan` yields one time: Python sets a timer whose moment depends on the other timers. No product file calls `Task.Delay`. |
| `asyncio.Lock` | `AsyncLock` with `AcquireAsync(cancellationToken)`, `Release()` in `try` and `finally`, and `Locked()`. It follows the rules of `AsyncSemaphore`. `Release()` of a free lock throws `InvalidOperationException`. |
| `asyncio.Event` | `AsyncEvent` |
| `time.time()` | `Py.TimeSeconds()`, a `double` |
| `time.perf_counter()`, `time.monotonic()` | `PortServices.Time.GetTimestamp()` and `GetElapsedTime` |
| A sync function | A sync method. Do not make it async. |

`Helpers.SemaphoreGatherAsync` is the port of `semaphore_gather` and calls `Asyncio.SemaphoreGatherAsync`. The helper keeps these properties of the Python function:

- It starts the calls in input order and returns the results in input order.
- At most `max_coroutines` calls run at one time. A value of `None` or `0` means `Helpers.SemaphoreLimit`.
- It throws the first failure in time. It does not cancel the other calls.

The order of side effects between concurrent calls is not a contract (GAPS G19).

A cancel that arrives late differs (GAPS F33). Python delivers `task.cancel()` at the next step of the task, also while its last call returns. A C# call that returned reads the token only at its next check. So a search that the caller cancels while its last driver call returns can give the full result, where Python raises `CancelledError`. Add no token check to hide this.

### R9. Cypher strings

The judge compares query text exactly, with each space and line break.

- Copy the text of each query character for character. Never reformat, trim, or indent a query again.
- A Python triple-quoted string keeps the indent of its source lines. The C# text must hold the same indent.
- Write a multi-line query as a raw string literal with the closing `"""` at column 0. A closing delimiter at column 0 removes no indent.
- First apply Python's own string processing, because a C# raw string literal has no escapes. In a Python literal with no `r` prefix, a backslash at the end of a line joins that line with the next one, and `\\`, `\n`, `\t`, `\'`, and `\"` give one character each. Write the text that Python holds after that step. Seven prompt literals start with `"""\` or hold such line joins (`prompts/extract_edges.py:212`, `prompts/extract_nodes.py:397`, `:503`, `:546`, `:625`, `prompts/summarize_sagas.py:51`, `:73`).
- An editor removes spaces at a line end, and `.editorconfig` asks for that. So three kinds of text go in an ordinary string next to the literal, with `\n` written out: the first line break, the last line break with the spaces after it, and each inner line that ends in a space or holds only spaces. Known inner lines: `models/nodes/node_db_queries.py:70`, `:78`, `prompts/eval.py:110`, `:133`, `:135`, `:138`, `:145`, `:149`, `search/search_utils.py:670`. Split the literal at such a line.

```csharp
var query = "\n" + """
            MATCH (n {uuid: $uuid})
            DETACH DELETE n
""" + "\n        ";
```

- For an f-string, use `$$"""` so that a single `{` or `}` is plain text and a hole is `{{expression}}`. Python `{{` and `}}` become `{` and `}`.
- A value that enters query text is a label, a property name, or an index name. Each one passes the same validation call as in Python before it enters the text. Add no validation and remove none.
- A hole formats its value as Python `str()` does. Use `Py.Str` for a value that is not a string.
- Keep each parameter name, each `RETURN` alias, and each property name exactly.
- Build the parameter dictionary in the order of the Python dict, with values from the value model (R6). `created_at` stays a `PyDateTime`.
- `Neo4jDriver.ExecuteQueryAsync` and the Neo4j transaction convert each parameter value before the driver call, at each depth. An `OrderedDictionary<string, object?>` becomes a `Dictionary<string, object?>`. A `PyDateTime` becomes a `DateTimeOffset` if it has an offset, and a `LocalDateTime` if it has none. A `PyTuple` becomes a list. A `PySet<T>` becomes a list, because it stands for Python `list(set(...))` at its one site (`entity_node_ops.py:78`): the Python driver rejects a raw `set` with `ValueError`. A `DateOnly` passes as it is. The conversion walks each `IEnumerable` except `string` and `byte[]`, and each dictionary with string keys, whatever its type arguments are. A `PyDict` with a key that is not a string throws, and so does an enum value. A `BigInteger` or a `ulong` that fits becomes a `long`. Each other value outside the driver's types throws. Records come back the other way, by the list below.
- The conversion runs the strict `PyText.EncodeUtf8` on each string, at each depth. A lone surrogate then throws `EncoderFallbackException`, where Python raises `UnicodeEncodeError`. Without the check, the driver stores U+FFFD.
- A datetime with an offset that `DateTimeOffset` cannot hold becomes the same instant in UTC (GAPS F30).
- The reason is a measurement with `Neo4j.Driver` 6.5.1. For a nested `OrderedDictionary`, a struct, a tuple, a `BigInteger`, a `ulong`, and an enum, the driver throws `ProtocolException`. It reads the `OrderedDictionary` as a list of pairs. A typed list, a `float[]`, a `HashSet<T>`, a `Dictionary<string, object?>`, and a `DateTimeOffset` work.
- `execute_query` returns the tuple `(Records, Summary, Keys)`, and a caller writes `var (records, _, _) = await ...`. `tx.run(...)` is `Transaction.RunAsync`, which returns `Task`: none of the 150 upstream sites reads its result.
- The conversion members are on `Neo4jDriver`: `ToDriverParameters`, `ToDriverValue`, `FromDriverRecord`, and `FromDriverValue`, each `internal static`. A record value that is a map or a list of maps is converted at each depth, because `search/search_utils.py` passes items of such a list to the record parsers. `session()` returns the adapter class `Neo4jSession`, which wraps the `IAsyncSession` of the package and has no Python class. A `run` call waits for the answer to its statement, as the Python driver does. `Neo4jTransaction.RunAsync` and `Neo4jSession.RunAsync` read the keys of the result. A result with no keys is consumed at once, which throws the error of the server. A result with keys stays open, so its rows are not read. In a transaction, a failure that comes with the rows then raises at the next run, as in Python. If the driver refuses that next statement with `TransactionTerminatedException`, the run consumes the earlier result, which throws the error of the server. The facts of Neo4j.Driver 6.5.1 behind this form: `IAsyncTransaction.RunAsync` waits for no answer, and `KeysAsync` waits for the answer to the statement and does not throw for a failed one. GAPS F28 and F37 name the differences that stay.
- A record value converts by its driver type. `null`, `bool`, `long`, `double`, `string`, and `byte[]` stay. A `List<object>` becomes a `List<object?>`, and a `Dictionary<string, object>` becomes an `OrderedDictionary<string, object?>`, at each depth. A `ZonedDateTime` becomes a `PyDateTime` with its offset, and a named zone keeps only the offset. A `LocalDateTime` becomes a naive `PyDateTime`. Nanoseconds are cut to microseconds and not rounded, as `to_native()` does. A `LocalDate` becomes a `DateOnly` (R6).
- These driver types have no place in the value model: `LocalTime`, `OffsetTime`, `Duration`, `Point`, `INode`, `IRelationship`, `IPath`, and a vector. The conversion throws `NotSupportedException` with the type name. No upstream query returns one. Only `properties(n) AS attributes` and `properties(e) AS attributes` can bring one, from a property that another writer stored (GAPS J11).
- Facts of `Neo4j.Driver` 6.5.1. `Neo4jTransaction` wraps an `IAsyncQueryRunner`: the work function of `IAsyncSession.ExecuteWriteAsync` receives one, and `IAsyncTransaction` extends it. `IDriver` has no `CloseAsync`, so `close()` is `DisposeAsync()`. Only `ExecutableQuery(...).ExecuteAsync` takes a `CancellationToken`. `routing_ = 'r'` is `RoutingControl.Readers`, and a missing key is `Writers`. `session(database)` uses `database or self._database`, so an empty string also gives the default: do not write `??`. The second argument of `ExecuteWriteAsync` carries a timeout (`b => b.WithTimeout(...)`), which `Neo4jSession` uses for the write with a timeout of decision D52.
- `Neo4jDriver` has a second constructor `Neo4jDriver(IDriver client, string database = "neo4j")` for a host that gives its own driver (the public surface design). The first constructor chains to it, so the operations objects are created at one place, as in Python. `aoss_client` has no member: only the Neptune driver reads it.
- `session.execute_write(func, *args, **kwargs)` passes the extra arguments to `func`. Write a lambda that closes over them: `session.ExecuteWriteAsync(tx => Asyncio.OnLoopAsync(() => FuncAsync(tx, a, b)))`. `ExecuteWriteAsync` takes a `Func<Transaction, Task>` and returns `Task`. The work function receives the managed transaction, and each upstream work function calls only `tx.run` on it and returns `None`. So a `tx: GraphDriverSession` annotation of a work function is `Transaction`, and so is the `transaction` parameter of the bulk methods of `GraphOperationsInterface`.
- Keep the call shape: one `execute_query` call is one call, and the operations of one session or transaction stay in order. The judge captures both shapes.
- A constant in `graph_queries.py` or a `*_db_queries.py` file is a `const string` or a `static readonly string` with the same text (GAPS F12).
- Never add an `ORDER BY`, a `group_id` filter, or a missing space. See R21.
- If you change a query for any reason, the reviewer makes sure that it still filters by `group_id` ([REVIEW.md](../REVIEW.md)).

### R10. Prompts

The judge compares each prompt character for character.

| Python | C# |
| --- | --- |
| `def node(context: dict[str, Any]) -> list[Message]` | `internal static List<Message> Node(OrderedDictionary<string, object?> context)` |
| `PromptFunction`, `ChatPromptFunction` | Delegates with the same names |
| `PromptVersion`, a `Protocol` with `__call__` only | No second type. It has the shape of `PromptFunction`: upstream gives each of the 25 prompt functions the annotation `PromptVersion`, and names the same function as a `PromptFunction` in the `Versions` dict. Write `PromptFunction` in its place. |
| `context['key']` | `context["key"]`. A missing key throws `KeyNotFoundException`, which the judge expects. |
| `context.get('key')`, `context.get('key', d)` | `Py.Get(context, "key")`, `Py.Get(context, "key", d)` |
| `{context['x']}` in an f-string | `Py.Str(context["x"])`. For a string it is the string. For a list or a dict it is `repr` text (GAPS G1). |
| `{to_prompt_json(x)}` | `PromptHelpers.ToPromptJson(x)`, which calls `PyJson.Dumps` (GAPS G2) |
| `{len(x)}`, `{len(x) - 1}` | `Py.Str(Py.Len(x))`, `Py.Str(Py.Len(x) - 1)` |
| `{x!r}` | `Py.Repr(x)` |
| `{value:.0f}` and other format specs | `Py.Format(value, ".0f")` |
| `if context.get('x'):` and other truth tests on open data | `Py.Truthy(...)` |

- Write prompt text by the string rules of R9: raw string literals with the closing delimiter at column 0, and `$$"""` for an f-string.
- Join text pieces with `+`. Never pass episode text, a fact, or custom instructions through `string.Format` or a composite format string (GAPS F4).
- Keep adjacent string literals as separate pieces in the same order. Python joins them with no separator.
- The `versions` dict of a prompt module is a `static readonly OrderedDictionary<string, PromptFunction>` with the Python names as keys.
- A response model follows R7. Its schema text reaches the prompt through `LLMClient` (decision D17).
- `ChatPrompt.as_messages` adds the Unicode note. Do not add it in a builder.

### R11. Python text output: `repr`, `str`, JSON, and dates

| Python | C# | Gap |
| --- | --- | --- |
| `repr(x)`, `f'{x!r}'`, a list or dict inside an f-string | `Py.Repr(x)` | G1, F1 |
| `str(x)`, `f'{x}'` | `Py.Str(x)`. `True` gives `True`, `None` gives `None`, `1.0` gives `1.0`. | G1 |
| `json.dumps(x, ...)` | `PyJson.Dumps(x, ensureAscii, indent, sortKeys, @default)` with the options of the call site. `indent` is a `long?`: pass a Python `int` as it is, with no cast and no range test. Python's defaults are `ensure_ascii=True` and separators `, ` and `: `. | G2, F3 |
| `json.loads(s)` | `PyJson.Loads(s)`. It returns the value model. | G5 |
| `ast.literal_eval(s)` | `PyAst.LiteralEval(s)` | F5 |
| A C# `int`, for example a loop index or `List.Count`, as an argument of a value-model helper (`Py.Str`, `Py.Repr`, `Py.Format`) or as a value in open data | Cast it to `long` first. The value model has no `int` (R6), and `Py.Str` of an `int` does not give Python's text. | |
| A `datetime` annotation, typed or open | `PyDateTime`, a struct that holds a date and time to the microsecond and an offset that is null for a naive value. Never `DateTime` or `DateTimeOffset` in ported code. A naive value stays naive, and judge cases pin that, for example `pure.models.EntityNode.created_at_naive_kept_naive`. | G4 |
| `dt.isoformat()` | `dt.IsoFormat()`. No fraction if the microseconds are zero. `+00:00`, never `Z`. | G4 |
| `str(dt)`, `f'{dt}'` | `dt.Str()`, and `Py.Str` calls it. A space in place of `T`. | G4, F2 |
| `repr(dt)` | `dt.Repr()`, and `Py.Repr` calls it | G4 |
| `a < b`, `a == b` on two datetimes | The operators of `PyDateTime`. A naive value compared with an aware value throws `InvalidCastException`, as Python raises `TypeError`. No MVP module subtracts two datetimes, and the struct has no subtraction. | G4 |
| `datetime.fromisoformat(s)` | `PyDateTime.FromIsoFormat(s)`, with the rules of Python 3.12 | F8 |
| `datetime.now(timezone.utc)`, `utc_now()` | `DatetimeUtils.UtcNow()`, which calls `PyDateTime.UtcNow(PortServices.Time)` | G4 |
| `dt.replace(tzinfo=timezone.utc)`, `dt.astimezone(timezone.utc)`, `dt.tzinfo is None`, `dt.tzinfo != timezone.utc` | `dt.ReplaceUtc()`, `dt.ToUtc()`, `dt.IsNaive`, `!dt.IsUtc`. `ToUtc()` throws `InvalidOperationException` for a naive value, where Python reads it as local time. Upstream calls `astimezone` only after a test for a naive value (`utils/datetime_utils.py:40`). | G4 |
| `dt.strftime(f)`, `datetime.strptime(s, f)` | Not built. No live code in an MVP module calls them. If your unit needs one, stop and tell the lead. | G4 |
| `int(s)`, `float(s)` on text | `Py.Int(s)`, `Py.Float(s)` | |

A `PyDateTime` never holds more than 6 fraction digits. A value from `Neo4j.Driver` or from `TimeProvider` becomes a `PyDateTime` at the point where it enters ported code, and is cut to microseconds there. `PyDateTime.FromDateTimeOffset` and `ToDateTimeOffset` are the conversions. `ToDateTimeOffset` reads a naive value as UTC (decision D36). A local date and time of the driver has no helper: the Neo4j project builds the value with the constructor.

### R12. Unicode text

Python counts code points. C# counts UTF-16 units. Upstream text is episode content, so it can hold any character.

| Python | C# |
| --- | --- |
| `len(s)` | `Py.Len(s)` |
| `s[i]`, `s[a:b]`, `s[::-1]` | `Py.At(s, i)`, `Py.Slice(s, a, b)`, `Py.Slice(s, a, b, step)` |
| `for ch in s`, `list(s)` | `foreach (var ch in PyText.CodePoints(s))`. Each item is a string with one code point. Never `EnumerateRunes`, which turns a lone surrogate into U+FFFD. A judge case pins a lone surrogate in `lucene_sanitize`. |
| `s.lower()`, `s.upper()`, `s.title()`, `s.capitalize()` | `PyText.Lower`, `PyText.Upper`, `PyText.Title`, `PyText.Capitalize`. Never `ToLowerInvariant` (GAPS F16). |
| `s.isalnum()`, `isalpha`, `isdigit`, `isspace`, `isupper`, `islower` | `PyText.IsAlnum` and the others |
| `s.split()`, `s.strip()`, `s.lstrip()`, `s.rstrip()` with no argument | `PyText.Split(s)`, `PyText.Strip(s)` and the others. Python's whitespace set differs from .NET's. |
| `s.split(sep)`, `s.strip(chars)`, `s.lstrip(chars)`, `s.rstrip(chars)` | `PyText.Split(s, sep)`, `PyText.Strip(s, chars)`, `PyText.LStrip(s, chars)`, `PyText.RStrip(s, chars)`. `LStrip` with `chars` is not built, because no MVP module calls it. Never `string.Split` or `string.Trim`, which work on UTF-16 units. `Trim` with the units of one emoji cuts half of another emoji. `Split` on a lone surrogate cuts an emoji in two. `Split("")` returns the text, and Python raises `ValueError`. All three were measured in both runtimes on 2026-11-26. |
| `s.count(sub)` | `PyText.Count(s, sub)`. It counts matches that do not overlap, by code point. The two sites are in the `*_db_queries.py` files. |
| `s.replace(a, b)` where `a` is a literal that is not empty and holds no lone surrogate | `s.Replace(a, b, StringComparison.Ordinal)`. For such a literal both runtimes give the same text. |
| `s.replace(a, b)` where `a` comes from data, and each `s.replace(a, b, count)` | `PyText.Replace(s, a, b, count)`. For an empty `a`, Python puts `b` between the code points and `string.Replace` throws `ArgumentException`. For a lone surrogate, `string.Replace` changes half of an emoji and Python changes nothing (measured on 2026-11-26). `string.Replace` has no count. The two sites are `llm_client/decision_prompts.py:116` and `:119`. |
| `sep.join(xs)` | `string.Join(sep, xs)`. It only joins the items, so both runtimes give the same text. |
| `s.split(sep, n)`, `s.rsplit(sep, n)` | `PyText.Split(s, sep, n)`, `PyText.RSplit(s, sep, n)`. Python's `n` is the number of cuts. The count of `string.Split` is the number of parts. |
| `s.splitlines()` | `PyText.SplitLines(s)` |
| `s.startswith`, `s.endswith`, `a in s` | The `string` method with `StringComparison.Ordinal` |
| `s.find`, `s.rfind`, `s.index`, with or without a start and an end | `PyText.Find`, `PyText.RFind`, `PyText.Index`. Positions are code points, and they mix with `Py.Slice`. |
| `s.encode('utf-8')`, `s.encode()`, `b.decode('utf-8')` | `PyText.EncodeUtf8(s)`, which throws for a lone surrogate as Python does, and `PyText.DecodeUtf8(b, errors)`. Never `Encoding.UTF8.GetBytes`, which writes replacement bytes. |
| `s.encode('utf-8', errors='ignore').decode('utf-8')` | `PyText.DropLoneSurrogates(s)` |
| `ord(c)`, `chr(n)` | `PyText.Ord(c)`, `PyText.Chr(n)` |
| `str.maketrans` with `s.translate(map)` | A loop over `PyText.CodePoints(s)` with a `switch`, in the order of the Python map |
| `a < b`, `sorted(strings)`, `min(strings)` | `Py.CompareStrings`, `Py.StringComparer`. Python compares by code point. `StringComparer.Ordinal` compares by UTF-16 unit and differs above U+FFFF. |

### R13. Regular expressions

Translate each pattern by hand with the rules in Part 3. Keep the Python pattern in a comment above the C# pattern.
A module-level `re.compile` is a `private static readonly Regex`, or `internal` if another module imports the name (`SAFE_CYPHER_IDENTIFIER_PATTERN` is imported at `nodes.py:35`). A pattern inside a function also becomes a static field.

| Python | C# |
| --- | --- |
| `\s`, also inside a class such as `[\s]` | `PyRegex.Space`. Inside a class that is not negated, use `PyRegex.SpaceClassBody`. Python's `\s` also matches U+001C to U+001F (GAPS F17). |
| `\w`, `\b` | `PyRegex.Word`, `PyRegex.Boundary`. `\w` inside a class becomes an alternation: `(?:` + `PyRegex.Word` + `\|[.-])`. |
| `\S`, `\W`, a negated class that holds `\s`, `\w`, or `\d` | Not built. No MVP site uses one. If your unit needs one, stop and tell the lead. |
| `\d` | `PyRegex.Digit`. .NET's `\d` misses the decimal digits above U+FFFF. Never set `RegexOptions.ECMAScript`. |
| `re.IGNORECASE` | `RegexOptions.IgnoreCase \| RegexOptions.CultureInvariant`, and write three letters out. Python also matches U+017F for `s`, and U+0130 and U+0131 for `i`. .NET does not. Each `s` that the flag covers becomes `[sſ]`, each `i` becomes `[iİı]`, and a class such as `[a-z]` becomes `[a-zſİı]`. The evaluation tests of the unit hold one case for each of the three characters, recorded from Python. |
| `$` with no `re.MULTILINE` | `$`. Both also match before a final line break. Do not change it to `\z` (GAPS F7). |
| `pattern.fullmatch(s)`, `re.fullmatch(p, s)` (`nodes.py:681`) | A second static `Regex` with the pattern text `\A(?:` + pattern + `)\z`. Never `IsMatch` on the pattern of a `match` call: `fullmatch('a\n')` fails where `match('a\n')` with `$` matches. |
| `re.split` with a pattern that has no group | `PyRegex.Split(...)` gives `List<string?>`, because a group that did not match is `None`. With no group no item is null: add `.OfType<string>()`. |
| A pattern built with an f-string from a name | Pass the name through `Regex.Escape` only if Python calls `re.escape` |
| Every pattern | `RegexOptions.CultureInvariant`, and the flags of the Python call |

The 17 sites at the pinned commit:

| Site | Call | Watch for |
| --- | --- | --- |
| `helpers.py:35` | `re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')` | The `$` rule. F7 pins a label with a final line break. |
| `helpers.py:165` | `re.match(r'^[a-zA-Z0-9_-]+$', group_id)` | The `$` rule |
| `utils/text_utils.py:64` | `re.finditer(sentence_pattern, truncated)` | Match positions are code points in Python. Convert before a slice. |
| `llm_client/decision_prompts.py:71` | `re.search(rf'<{name}>\s*(.*?)\s*</{name}>', text, re.S)` | `\s`, `re.S`. The name is not escaped. |
| `llm_client/decision_prompts.py:89` | `re.finditer(r"\{.*?\}(?=...)", text, re.S)` | `\s`, a lookahead with `$` |
| `llm_client/decision_prompts.py:101` | `re.search(rf'["\']{key}["\']\s*:\s*(-?\d+)', span)` | `\s`. `int()` of the group is `Py.Int`. |
| `llm_client/decision_prompts.py:105` | `re.search(rf'...', span, re.S)`, the pattern for a quoted key and a quoted value | `\s`, `re.S`, a lazy group between quotes |
| `utils/maintenance/dedup_helpers.py:42` | `re.sub(r'[\s]+', ' ', name.lower())` | `\s`, `PyText.Lower` |
| `utils/maintenance/dedup_helpers.py:48` | `re.sub(r"[^a-z0-9' ]", ' ', ...)` | One replacement for each code point. An emoji is one space in Python and two in .NET. Match a surrogate pair as one item. |
| `utils/maintenance/dedup_helpers.py:50` | `re.sub(r'[\s]+', ' ', normalized)` | `\s` |
| `utils/maintenance/temporal_edge_utils.py:14`, `:27`, `:32` | `re.compile(...)` with flags | `\b`, `\s`, and the `re.IGNORECASE` row above. These patterns decide stored `valid_at` and `invalid_at` values. |
| `utils/content_chunking.py:403` | `re.split(r'\n\s*\n', content)` | `\s`. Empty items in the result. |
| `utils/content_chunking.py:461` | `re.split(sentence_pattern, text)` | A lookbehind. Empty items. |
| `utils/content_chunking.py:589` | `re.search(speaker_pattern, content, re.MULTILINE \| re.DOTALL)` | Both flags |
| `utils/content_chunking.py:615` | `re.split(pattern, content, flags=re.MULTILINE)` | A split on an empty match. Empty first item. |

`re.split` and `Regex.Split` differ for empty matches and for groups. Use `PyRegex.Split(regex, text, maxsplit)`, which follows Python. The flags are the options of the `Regex`. It throws `ArgumentException` for an inline `(?x)` flag, `RegexOptions.NonBacktracking`, and `RegexOptions.RightToLeft`. Use only named groups or only unnamed groups in a pattern for `Split`, because .NET numbers unnamed groups first.
Each `PyRegex` constant sits in `(?-i:...)`, so `RegexOptions.IgnoreCase` does not change it. .NET folds case with a newer Unicode version than the Python build.
GAPS.md G16 counts 19 `re` calls with a wider `grep` pattern. The table above names each call site of the 114 MVP modules.

### R14. Order: sorts, dicts, and sets

| Python | C# |
| --- | --- |
| `sorted(xs)`, `sorted(xs, key=f)` with keys that are text or `int` | `xs.Order(comparer)`, or `OrderBy` with a key. Both are stable and give a new sequence. For strings the comparer is `Py.StringComparer`. |
| A sort with a key that is a `float`, a datetime, a value from open data, or a tuple that holds one of these | `Py.SortInPlace(list, key, reverse)`, on a copy of the list for `sorted`. It is a port of the list sort of CPython 3.12 and makes the same comparisons, so `nan` keys give Python's order (`search/search_utils.py:1701`). A tuple key is `new PyTuple(a, b)`. A comparison that Python refuses throws `InvalidCastException` as itself, for example a naive datetime against an aware one. A key of a type outside the value model, for example a `ValueTuple`, throws `InvalidOperationException`. |
| `xs.sort()`, `xs.sort(key=f)`, `xs.sort(key=f, reverse=True)` | `Py.SortInPlace(xs, key, reverse)`. Python sorts the list object itself, and a caller can hold the same list. Never assign a new list. |
| A tuple key of text and `int` items, or a sort of such tuples | `OrderBy(a).ThenBy(b)` in the order of the tuple items |
| `reverse=True` with `Order` or `OrderBy` | `OrderByDescending`, and `ThenByDescending` for each further tuple item. It keeps equal items in their first order, as Python does. Never sort and then reverse. |
| `min(xs, key=f)`, `max(xs, key=f)` | `MinBy`, `MaxBy`. Both return the first of equal items, as Python does. If a key can be `nan` or null, write the loop, because .NET orders those in a different way. |
| `List.Sort`, `Array.Sort` | Not allowed for a ported sort. They are not stable. |
| A `dict` that code iterates, dumps, or passes to a prompt or a query | `OrderedDictionary<TKey, TValue>` |
| A `dict` that code reads only by key | `Dictionary<TKey, TValue>` |
| A `dict` keyed by `id(obj)` that code iterates (`ResolutionTrace._edge_states`) | `OrderedDictionary<T, V>` with `ReferenceEqualityComparer.Instance`. The key is the object. |
| `dict.fromkeys(xs)` to remove duplicates in order | A loop with a `HashSet<T>` that keeps the first of each item in order. Do not rely on `Distinct()` for order. |
| `defaultdict(list)` | `OrderedDictionary` with `Py.SetDefault` |
| `defaultdict(int)` with `d[k] += n` | `d[k] = Py.Get(d, k, 0) + n` on an `OrderedDictionary` |
| `OrderedDict` with `move_to_end(k)` and `popitem(last=False)` (`ingestion/delta.py:152`) | `OrderedDictionary`: remove the key and add it again, and `RemoveAt(0)` |
| `MappingProxyType(d)` | `d.AsReadOnly()`, a live view that cannot be changed. For an `OrderedDictionary<K, V>` write the type arguments, `d.AsReadOnly<K, V>()`, because the call is ambiguous with no arguments (error CS0121). |
| `itertools.combinations(xs, k)`, `itertools.zip_longest(a, b)` | `Py.Combinations(xs, k)` in Python's order, `Py.ZipLongest(a, b)` with null for a missing item |
| A `set` or `frozenset` that code tests only with `in`, or compares, or takes the size of | `HashSet<T>`. For strings pass `StringComparer.Ordinal`. |
| A `set` that code passes to `sorted()` | `HashSet<T>`, then the sort rule above |
| A `set` that code iterates with no sort | `PySet<T>`, which iterates in insertion order. See the table below. |
| A `tuple[int, ...]` or a `frozenset` as an item of a set | `PyTuple` for the tuple, and a frozen `PySet<long>` for the frozenset. `PyTuple` overrides `Equals` and `GetHashCode`, so a `HashSet<PyTuple>` works. `PySet<T>` overrides neither. It has the equality of the value only as an item of a `PySet<T>` or with `PyEqualityComparer`, so write `PySet<PySet<long>>` and never `HashSet<PySet<long>>`. |

A Python set has no stable order, so each run of upstream can differ at these sites. The port picks insertion order, which is one of the orders that Python can give.

| Site | Python | Port |
| --- | --- | --- |
| `utils/maintenance/node_operations.py:365`, `combined_extraction.py:204` | `list({'Entity', str(entity_type_name)})` | `Entity` first, then the type name if it differs (decision D18, GAPS F13) |
| `driver/neo4j/operations/entity_node_ops.py:51`, `:78`, `utils/bulk_utils.py:216` | `list(set(node.labels + ['Entity']))` | `Entity` first, then the other labels in their first order, each one time |
| `graphiti.py:2563`, `:2565` | `list(set(resolved.labels) \| set(source.labels))` | `Entity` first if present, then the labels of `resolved` in order, then the new labels of `source` in order |
| `utils/maintenance/dedup_helpers.py:359`, `ingestion/delta.py:91` | `for candidate_id in candidate_ids:` with `score > best_score` | `PySet<string>`. Candidates in the order of the bands and of each bucket. The first best score wins. |
| `utils/maintenance/edge_operations.py:469` | `list(referenced_node_uuids)` as a query parameter | `PySet<string>`, in the order of the edges |
| `utils/bulk_utils.py:792` | `UnionFind(all_uuids)` and a dict over the set | `PySet<string>`, in the order of the pairs |
| `utils/content_chunking.py:745`, `:820` | `for pair in uncovered_pairs:` | `PySet` of pairs, built in `(i, j)` order. The sampling path reaches the loop (GAPS F9), and so does a call with `k` of 0 or 1. CPython gives one fixed order there, because the hash of a frozenset of ints has no seed. The port gives insertion order: the same chunks in another order (decision D43, for the maintainer at gate 4). |
| `utils/maintenance/attribute_utils.py:179`, `:256` | `for field in dropped:` writes keys into `merged` | `PySet<string>`, in the order of `response`. The key order of the merged attributes reaches prompt text. |

If you find another set that code iterates with no sort, and its order can reach a prompt, a query, a result, or a stored value, stop and tell the lead.

Forms that the units of layer 7 use:

- `if x not in s: s.add(x)` is one `if (s.Add(x))` (analyzer rule CA1868). `if k not in d: d[k] = []` is `d.TryAdd(k, [])`, or one `TryGetValue` (CA1854).
- `zip(a, b, c, strict=True)` with three inputs is two nested calls of `Py.ZipStrict`.
- `dict(pairs)` on a list of pairs is a loop that sets each key. The last value of a key wins, and the key keeps its first position.
- `summaries.pop()` is a read of `summaries[^1]` and then `RemoveAt`.
- `a and b` on two dicts that can be `None` is `is { Count: > 0 }` on each one.
- A recorder binds the module name `set` to a class that iterates in insertion order, because that is the order of the port (decision D43). It marks each case whose Python result depends on the hash seed with `"set_order": true`.
- A recorder subclass of a model, for example one that puts `Entity` first in the labels, sets `__name__` and `__qualname__` to the name of its base class. The title of a pydantic error then equals upstream.

### R15. Duck typing and protocols

No `dynamic` and no reflection, except the one site that the table names. Each `getattr` or `hasattr` site has a typed answer.

| Site | Python | C# |
| --- | --- | --- |
| 20 sites in `node_operations.py`, `edge_operations.py`, `bulk_utils.py`, `graph_reader.py`, `phases.py`, `search.py` | `getattr(clients, 'max_coroutines', None)` | `clients.MaxCoroutines`. It is a declared field of `GraphitiClients`. |
| `search/search.py:115` | `getattr(clients, 'tracer', None)` | `clients.Tracer` |
| `search/search.py:73` | `value.value if hasattr(value, 'value') else value` | Two overloads of `EnumValue`: one for `string`, one for an enum that returns its wire value |
| `graphiti.py:527`, `:535`, `:541`, `:727`, `:2029` | `getattr(self, 'concurrent_ingestion', None)` and the like | Nullable fields of the facade class, read directly |
| `decorators.py:53`, `:54`, `:63`, `:95` | `hasattr(self, 'clients')`, `getattr(driver, '_database', None)`, `getattr(self, 'max_coroutines', None)` | The explicit wrapper of R16 takes the two values that it reads from `self`: `GraphDriver? clientsDriver` and `long? maxCoroutines`. A null `clientsDriver` stands for a `self` with no `clients.driver`. `driver._database` is `driver.Database`. |
| `utils/bulk_utils.py:195` | `hasattr(session, 'query_timeout_ms')` | The interface `IQueryTimeoutSession` with the one property, in `Core/Driver/Driver.cs` after `GraphDriverSession`. Test with `is`. |
| `utils/bulk_utils.py:283` | Tests whether the interface class overrides an operation | Reflection is allowed at this one site: compare `GetMethod(name).DeclaringType` with `typeof(GraphOperationsInterface)`. Keep the Python operation names in the result. |
| `utils/maintenance/attribute_utils.py:87`, `:88`, `:98` | `getattr(field_info, 'metadata', [])`, `max_length`, `is_required` | `GraphFieldDescriptor.MaxLength` (a `long?`) and `GraphFieldDescriptor.IsRequired` of the custom type descriptor (below) |
| `namespaces/nodes.py:352`, `namespaces/edges.py:352` | `__getattr__` raises `NotImplementedError` for the names of the operations that a driver lacks | One property for each name that Python tests. Its getter throws `NotImplementedException` with the same message if the driver has no such operations. Other names do not exist in C#. |
| `prompts/lib.py:477` to `:618` (11 sites) | `getattr(library, group_name)`, `getattr(group, method_name)`, `hasattr(library, 'specs')` | `IPromptLibrary` and `IPromptGroup` (below) |
| `llm_client/prompt_config.py:96`, `:108`, `:118`, `:292`, `:312` | `getattr(parent, nested_name)`, `getattr(overrides, group_name)`, `fields(overrides)` | A static table from the Python group name to the nested type and to a getter, in field order |

The prompt library is read by name, so it gets two interfaces:

- `IPromptGroup` has `bool TryGetFunction(string name, out ChatPromptFunction? function)`. The names are the Python function names.
- `IPromptLibrary` has `bool TryGetGroup(string name, out IPromptGroup? group)` and `IReadOnlyDictionary<string, PromptSpec>? Specs`. A `null` value of `Specs` stands for a library with no `specs` attribute.
- `getattr` on a missing name throws `MissingMemberException(name)`. `hasattr` is the `TryGet` call.
- A class with `__getattr__` (`_OverrideGroup`, `_ComposedPromptLibrary`, `_LibraryWithBuiltinSpecs`) implements the interface and forwards in `TryGet`.
- The typed members of the Python classes (`library.dedupe_nodes.node`) stay as properties and methods next to the lookup.
- The two interfaces are in `src/Graphiti.Net/Core/Prompts/Lib.cs`, the file of `prompts/lib.py`. Each `<Module>Prompts` class of a prompt module (an `ABC`, for example `DedupeNodesPrompts`) implements `IPromptGroup`: `TryGetFunction` gives the method for each Python method name of the class.
- One class `PromptTypeWrapper` implements the 8 `<Module>Prompt` interfaces with explicit members, and `PromptLibraryWrapper` implements `PromptLibrary` (Part 5, O12, closed).

A custom entity or edge type is the internal class `Graphiti.Internal.GraphTypeDescriptor` (decision D26). The lead writes it. The public `GraphTypeDefinition` builds it. It implements `IResponseModel` (R7), because upstream passes a custom type as the response model of the attribute prompt.

The schema description of a custom type is `PyText.Cleandoc(Doc)`, and `Doc` stays the raw text, as `__doc__` does. The schema holds `maxLength` for a string field with a limit, and each field title follows `PyText.Title`. A field name that pydantic refuses at class creation throws `ArgumentException` at `Add*`: a name that starts with `_`, `model_config`, and the `model_dump` and `model_validate` members of `BaseModel`.

| Python on a custom type class | C# on the descriptor |
| --- | --- |
| `dict[str, type[BaseModel]]` | `OrderedDictionary<string, GraphTypeDescriptor>`, in the order of the caller's list |
| `type_model.__doc__` | `descriptor.Doc`, a `string?` |
| `type_model.model_fields` | `descriptor.ModelFields`, an `OrderedDictionary<string, GraphFieldDescriptor>` with the Python field names |
| `type_model.model_json_schema()` | `descriptor.ModelJsonSchema` |
| `type_model(**data)` | `descriptor.ModelValidate(data)`. It applies lax coercion by the kind of each field and throws `ValidationException`. It returns the validated dictionary. |
| `type_model.__name__` | `descriptor.PythonName` |

| Python | C# |
| --- | --- |
| `typing.Protocol` | `interface` with the member names in PascalCase (GAPS G8) |
| `ABC` with `@abstractmethod` | `abstract class`. Keep it a class if any member has a body. |
| A base method that raises `NotImplementedError` | A `virtual` method that throws `NotImplementedException` |
| `isinstance(x, T)` | `x is T`. For the value model, test the runtime type from the table in R6. Test `bool` before `long`, because a Python `bool` is an `int`. `isinstance(x, list)` on an open value is `Py.TypeName(x) == "list"`, because `x is IList` also matches `byte[]` and an `OrderedDictionary`. |
| `isinstance(x, (A, B))` | `x is A or B` |
| `callable(x)` | `x is not null` for a delegate type |
| `type(x).__name__`, `cls.__name__`, `__qualname__` in a message or a result | A `PythonName` constant on the type with the Python name. Never `GetType().Name`. |
| `type(error).__name__` of a caught exception in a stored reason or a log line (`ingestion/phases.py:224`, `:1426`) | `error.GetType().Name`, the .NET type name (decision D50). It is diagnostic text, and no code reads it. For `CancelledError` the stored name is `TaskCanceledException` or `OperationCanceledException`. The first is the type that the await of a canceled task throws. The second comes from a call that reads its token, for example `AsyncSemaphore.AcquireAsync` and `Asyncio.SleepAsync`. A test compares the stored name with the .NET name that R4 gives for the recorded Python name. |
| The class name of an exception in the telemetry of a span: the status description `f'{type(e).__name__}: {e}'` and the exception event | The .NET type name, and the event that `Activity.AddException` writes (decision D46). A .NET exception has a .NET message, so a Python class name in front of it names an error that never existed. This is the one exception to the row above. Python's event has one more key, `exception.escaped`. |
| The class name of a driver, of a model client, or of a nested override group | `GraphDriver` and `LLMClient` have `internal abstract string PythonName { get; }`, and each subclass returns its Python class name. Each nested `*Group` record of `prompt_config.py` has `internal const string PythonName`. A qualified name is the outer name, a dot, and that value. |
| An attribute of a prompt group that exists and is not callable | `TryGetFunction` returns true with a null function. `callable(x)` is then the test `x is not null`. |
| An attribute of a prompt library that exists and is not a group | `TryGetGroup` returns true with a null group. `hasattr(group, name)` is then false for each name, and a `getattr` on it throws `MissingMemberException`. |
| A group property of a typed `ChatPromptLibrary` that returns null | `TryGetGroup` returns true with a null group, and a composed library stores the null as Python stores `None`. `validate_prompt_library` then says "missing function", as Python does. |
| `isinstance(x, T)` on a value of a typed mapping that can still be null at run time (`specs[name]` in `prompts/lib.py`) | Keep the branch as a null test, with the error text of Python. |
| `callable(x)` for a delegate of another delegate type | The typed read gives null, so the port says "not callable" where Python calls each callable. Only the decoder of the judge host for `types:SimpleNamespace` can pass one, and no case does. The lead accepts the difference. |
| The Python name of a nested override or route class of `prompt_config.py`, where code has only its `Type` | One static table `PythonNames` from the type to the name. |

### R16. Decorators, context managers, generators, and caches

| Python | C# |
| --- | --- |
| `@handle_multiple_group_ids` (4 uses) | Port the function body to `<Name>CoreAsync`. The decorated method calls `Decorators.HandleMultipleGroupIdsAsync(Clients.Driver, MaxCoroutines, groupIds, driver, (ids, d) => <Name>CoreAsync(...))`. The wrapper does not take `self`, because the host of the upstream test `tests/test_handle_multiple_group_ids.py` is not a `Graphiti`. It has three overloads, one for each result shape that Python merges by type: `List<TItem>`, `SearchResults`, and a tuple of two lists. The three share the branches of the Python wrapper in one private method `HandleMultipleGroupIdsCoreAsync<T>`, which takes the merge as a `Func<List<T>, T>`. This is the one private helper that R1 allows here. The branch `else: return results` has no typed form and no caller. `get_parameter_position` has no member: the wrapper receives `group_ids` and `driver` as parameters. |
| `@retry(...)` on `_generate_response_with_retry` | A Polly pipeline from `Microsoft.Extensions.Resilience`: 4 attempts in total, the predicate `is_server_or_retry_error`, and the last exception thrown again. The warning log runs after an attempt only if its number is above 1: after attempts 2, 3, and 4, and not after attempt 1. Keep the message text of upstream. Each Polly callback follows the `OnLoopAsync` rule of R8. A `DelayGenerator` computes the wait with the formula of tenacity's `wait_random_exponential(multiplier=10, min=5, max=120)`. Polly's own jitter is a different formula. The pipeline takes its clock from `PortServices.Time` and its random source from `PortServices.Random` (GAPS J3). |
| The log line of a retry | It runs in `ShouldHandle` of the Polly options. Polly calls `ShouldHandle` also for the last attempt. |
| A cancellation during an attempt that fails with a retried error | Python cancels inside the sleep of tenacity, so the call leaves with `CancelledError` after the log line of that attempt. Polly returns the last outcome for a canceled token. Catch around `ExecuteAsync`: if the token is canceled and the failed attempt was not the last one, throw `OperationCanceledException`. The span gets no error status. |
| `sqlite3.connect(path)` in `llm_client/cache.py` | `Microsoft.Data.Sqlite` with pooling off. The statements run as raw SQLitePCL calls on the handle, because the package refuses the `?` parameters of the upstream SQL text (R9). Call `raw.sqlite3_busy_timeout(handle, 5000)` after the open, for the default `timeout=5.0` of Python. Encode each text parameter with the strict `PyText.EncodeUtf8` first, so that a lone surrogate throws (R12). |
| `sqlite3.Row` (`ingestion/sqlite_state_store.py`) | `OrderedDictionary<string, object?>` keyed by column name, in the order of the columns. A value is a `long`, a `double`, a `string`, a `byte[]`, or null. |
| `conn.execute(sql, params)` with `fetchone` or `fetchall`, `conn.executescript`, and `conn.executemany` (`ingestion/sqlite_state_store.py`) | Private members of the class that holds the connection: `Execute`, `ExecuteScript`, and `Handle`. They are the C# form of the library calls, as in `llm_client/cache.py`. They run raw SQLitePCL calls, because the package refuses the `?` parameters of the upstream SQL text. |
| `zlib.compress(data)`, `zlib.decompress(data)` | `PyZlib.Compress` and `PyZlib.Decompress`. The compressed bytes differ from the bytes of CPython (GAPS F39). `Decompress` is an inflate of its own (decision D51): it checks the header, each block, the end of the stream, and the check value, in the order of zlib, and it gives the message of zlib for a truncated stream, a wrong header, and a wrong check value. |
| `@contextmanager` | A method that returns an `IDisposable`. Code before `yield` runs in the method. Code in `finally` runs in `Dispose`. |
| `@asynccontextmanager` | A method that returns an `IAsyncDisposable`, used with `await using`. `GraphDriver.transaction()` is the exception, because it commits or rolls back by the outcome of the body, which a dispose call cannot see: `Task TransactionAsync(Func<Transaction, Task> body, CancellationToken cancellationToken)`. |
| A context manager that reads the exception, for example to roll back a transaction or to set a span status | `try`, `catch`, and `finally` written out at the call site, or a method that takes the body as a lambda. `Dispose` cannot see the exception. |
| `with self.tracer.start_span(name) as span:` | `await Tracer.StartSpanAsync(name, async span => { ... }, cancellationToken)`. The body of the `with` is the lambda, and a `return` in it is the result of the call. A `set_status('error')` in an `except` stays in a `catch` inside the lambda. The tracer takes the body, because the end of the Python context manager depends on the exception of the body (defect U19), and a `Dispose` call cannot see it. |
| `@lru_cache(maxsize=n)` | `PyLruCache<TKey, TValue>` with the same size, in a static field. The cached function is `cache.GetOrAdd(key, compute)`. The cache holds a lock for each read and each change, as `lru_cache` does, and it calls the function outside the lock. A sync test calls a cached function from its own thread, at the same time as another test class. |
| `@property`, `@staticmethod`, `@classmethod` | A property, a static method, a static method. A classmethod that builds an instance is a static factory. |
| `@abstractmethod` | `abstract` |
| A generator with `yield` | `IEnumerable<T>` with `yield return`. An async generator is `IAsyncEnumerable<T>` with `[EnumeratorCancellation]`. |
| `functools.wraps`, `inspect.signature` | Not needed. The explicit wrapper replaces them. |
| `__del__` | No member. `LLMCache.__del__` is the one site. The cache is off in the product, and a finalizer on a class that holds a connection is not safe. Write `// PORT: __del__ is not ported (R16).` The class has `IDisposable` with `Dispose() => Close()`, because analyzer CA1001 asks for it. |

### R17. Module state and environment

| Python | C# |
| --- | --- |
| A module-level constant | `const` for a number, a `bool`, or a string. `static readonly` for other types. |
| A module-level value read from the environment at import (11 sites in `helpers.py`, `driver/driver.py`, `embedder/client.py`) | `internal static readonly` field with the same name, read one time with `PyEnv`. The read runs at the first use of the class, not at import. A value that does not parse then gives `TypeInitializationException` with the `ArgumentException` inside, where Python raises `ValueError` at import. |
| A read inside a function or a constructor (`attribute_utils.py:63` at each call, `openai_decisions_client.py:68` and `:84` in `__init__`) | A `PyEnv` call at the same place. Do not move it to a static field. GAPS.md G10 counts 7 reads with another `grep` pattern. These 14 are each site. |
| `int(os.getenv('X', 20))`, `float(os.getenv('X', 0.15))` | `PyEnv.Int("X", 20)`, `PyEnv.Float("X", 0.15)`. A value that does not parse throws, as Python does at import. |
| `bool(os.getenv('X', False))` | `PyEnv.Truthy("X")`. Python gives `True` for each value that is not empty, also for `"false"`. |
| `os.environ.get('X', 'default')` | `PyEnv.Str("X") ?? "default"`, with the type `string` |
| A test that needs an environment value | `using (PyEnv.Use(name => ...))`. It holds for a read inside a function. Never start the static initializer of a ported class inside such a scope, because a `static readonly` field keeps the value for the whole process. |
| A module-level value that an upstream test replaces with `monkeypatch.setattr(module, 'NAME', value)` (`CHUNK_DENSITY_THRESHOLD` and `CHUNK_MIN_TOKENS` in `tests/utils/test_content_chunking.py`, the alias `_sleep` in `tests/llm_client/test_openai_decisions_client.py`, `SAGA_SUMMARY_MAX_EPISODE_CHARS` and `SAGA_SUMMARY_MAX_PROMPT_BYTES` of `graphiti.py` in `tests/test_saga_summary_budget.py`, and `SUMMARY_REBUILD_BATCH_SIZE` of `graphiti.py` in `tests/test_graphiti_mock.py`) | `internal static readonly PyPatch<T> Name = new(<the value>);`. Code reads it with `.Value` at each site. The type has no implicit conversion, because with one the compiler accepts `Name == null`, `Name.Equals(x)`, and `var x = Name`, and each reads the holder. The test writes `using (Helpers.Name.Use(value))` in the test method or in the constructor of the test class, which holds for the flow of execution of that test. A call after an `await` in a fixture method does not reach the test. Nested scopes end in reverse order. Use `PyPatch<T>` only for a name that a ported test replaces. The lead adds each name to this row. |
| A parameter default that names a module-level value (`max_episode_chars: int = SAGA_SUMMARY_MAX_EPISODE_CHARS`, `graphiti.py:180`) | The literal value. Python binds a default one time at import, so a later replacement of the name does not change it. |
| `load_dotenv()` | Not ported. The library reads no `.env` file. |
| A module-level mutable object, for example a search recipe | A `static readonly` field that holds the same single object. Upstream changes one of them in a call (R21, U8). |
| A module-level cache or a "warned once" set | A `static readonly` field. The exclusive scheduler of R8 protects it. |
| An instance of a class with `__call__` that Python reads as an attribute (`VersionWrapper` in `prompts/lib.py`) | Build one delegate for the instance in its constructor and return that delegate at each read. Two reads then give the same object, as in Python. |
| Code that runs on import | A static field initializer. Never a static constructor. |
| `Neo4jDriver.__init__` starts an index build as a background task | Not ported. The .NET driver builds indexes only in `BuildIndicesAndConstraintsAsync` (GAPS G20). Write `// PORT: G20` at that place. |
| An optional import in `try` and `except ImportError` | Port the branch that runs when the import works, if the package is in [DECISIONS.md](DECISIONS.md). If not, stop and tell the lead. |

The ambient holder `Graphiti.Internal.PortServices` (decision D23) has four members for ported code: `Logger(Type)`, `Time` (a `TimeProvider`), `Random` (a `PyRandom`), and `ClassifyModelFailure(Exception)` (R4, decision D31). Ported code never sets a value and never reads `LoggerFactory`, `ModelFailureClassifier`, or `DefaultClassifyModelFailure`: these are for the facade. A test replaces a value with `using (PortServices.Use(time: ..., random: ..., loggerFactory: ..., modelFailureClassifier: ...))`, which holds only for the flow of execution of that test.
Read the clock, the random source, and loggers only through it. Never use `DateTime.Now`, `DateTimeOffset.UtcNow`, `Stopwatch`, `Random.Shared`, or `new Random()`.

`attribute_utils._warned_invalid_env` is a `PyPatch<HashSet<string>>`: an upstream test replaces the set.

### R18. Numbers, vectors, hashes, random values, and UUIDs

| Python | C# |
| --- | --- |
| `int` | `long`. Use `ulong` for a MinHash value, which goes up to 2^64 - 1 (GAPS F15). Use `int` only for a .NET API that needs it. |
| `a / b` | `Py.TrueDiv(a, b)`. It returns `double` and throws `DivideByZeroException` for a zero divisor, also for floats. |
| `a // b`, `a % b` | `Py.FloorDiv(a, b)`, `Py.Mod(a, b)` |
| `round(x)` on a float | `Math.Round(x, MidpointRounding.ToEven)`, and then `Py.Int` where Python needs an int. `PyCompat` has no `Py.Round` yet: the lead adds it with the first unit that calls `round(x, n)`. |
| `int(x)` on a float | `Py.Int(x)`. It cuts toward zero and throws for `nan` and `inf`. |
| `math.comb`, `math.log2`, `math.ceil`, `math.floor` | `Py.Comb`, `Math.Log2`, `(long)Math.Ceiling(x)`, `(long)Math.Floor(x)`. `Py.Comb` returns a `BigInteger`, because `comb(100, 20)` does not fit in a `long` and upstream compares it with a limit. |
| `math.isnan(x)` | `double.IsNaN(x)` |
| `sum(xs)`, `sum(1 for ...)` | A loop or `Sum()` over the same order. Float sums keep the Python order. |
| A function that returns the integer `0` in one branch and a float in others | `double` (GAPS F18) |
| `numpy` L2 norm, dot product, and array math (25 calls) | A sum that numpy does with `add.reduce` is `PyNumpy.AddReduce`. It sums in pairs as numpy does and gives the same bits, where a plain loop gives other last bits. The one float64 site is `np.linalg.norm(a, 2, axis=0)` in `normalize_l2` (`helpers.py:127`), which is the square root of the sum of the squares. `ingestion/delta.py:56` is a norm with `axis=1` over a float32 matrix. numpy sums it in float32, so `PyNumpy.AddReduce` for float64 gives other bits there. `PyNumpy.AddReduce` has a float32 form for it. A float32 matrix is a `float[,]`, and an empty one has the shape that numpy gives, `(0, 0)` or `(1, 0)`. `queries @ candidate_vectors.T` (`delta.py:211`) is a plain loop in index order with a float32 sum. `np.dot` and `np.linalg.norm(v)` with no axis go through BLAS, whose order of additions differs by platform, also between two Python hosts. Write a plain loop in index order for them, and the judge accepts a difference below 1e-6 (GAPS G17). No project references `System.Numerics.Tensors`. `inf` and `nan` inputs give `NaN` in the same places (GAPS F10). |
| `np.where(norm == 0, a, a / norm)` | A test of the norm, then the division. Do not divide first. |
| `np.argsort(a)` with the default kind (`delta.py:213`) | `PyNumpy.ArgSort(values)` for a float32 array. It is the generic sort of numpy 2.4.1: a quicksort with the median of three, an insertion sort for 16 items or fewer, a heapsort past the depth limit, and `nan` last. It is not stable for 17 items or more, so a stable sort such as `OrderBy` gives another order of equal values. GAPS F47 names the platform limit. |
| A comparison of a float32 value with a Python float (`scores[row, column] >= min_score`, `delta.py:215`) | NumPy 2 compares in float32 (NEP 50). Cast the Python float to `float` first, and then compare. `1e300` becomes `inf`. |
| A count that Python holds as an `int` and the port holds in a map of `double` (the report of `commit_unit`, `phases.py`) | The model field stays a map of `double`, as pydantic gives it. A span attribute and a log scope get the count as a `long`, because Python passes the `int` there. |
| A conditional with a `long` branch and a `double` branch, where the result is an open value (`key.EndsWith("_ms") ? value : (long)value`) | C# gives the conditional the type `double`, so the count is a `double` again. Box the `long` branch: `(object?)(long)value`. The commit span of `phases` got `2.0` where Python gives `2`, and the recording of layer 9 found it. |
| `hash(self.uuid)` in `__hash__` | `Uuid.GetHashCode(StringComparison.Ordinal)` (GAPS G12) |
| `hash(unit.payload)` in `ingestion/delta.py:163` | The SHA-256 of the bytes as a hex string (GAPS G12) |
| `hashlib.md5(b).hexdigest()` | `Convert.ToHexStringLower(MD5.HashData(b))` (GAPS J4) |
| `blake2b(b, digest_size=8)` | `PyHash.Blake2b(b, 8)`. .NET has no BLAKE2b. |
| `int.from_bytes(b, 'big')` | `BinaryPrimitives.ReadUInt64BigEndian(b)` for 8 bytes |
| `zlib.compress`, `zlib.decompress` | `PyZlib.Compress` and `PyZlib.Decompress` (R16, decision D51). No judge case compares compressed bytes (GAPS F39). |
| `random.*` | `PortServices.Random`, a `PyRandom` (GAPS F9). Its members are `Random()`, `Uniform(a, b)`, and `SampleRange(n, k)` for `random.sample(range(n), k)`. The sequence for a seed is not Python's. The random path of `generate_covering_chunks` has no judge case. Its evaluation test uses a fixed seed and the property that each pair is covered. |
| `str(uuid4())`, `uuid4().hex` | `Py.Uuid4()`, `Py.Uuid4Hex()`. Lower case, as Python writes them. |
| `str(uuid5(NAMESPACE_URL, name))` (`ingestion/phases.py:330`) | `Py.Uuid5(Py.NamespaceUrl, name)`. .NET has no version 5 UUID. The value is a stored episode UUID, so the helper is tested against Python values. |
| `a == b` where `a` is a node or an edge | `a.Equals(b)`. `Node` and `Edge` are classes (R7) that override `Equals(object)` with the body of the Python `__eq__`, and nothing more. No reference test: `e == e` is `False` for an edge in Python (R21, U12). A node equals an edge with the same UUID, and two judge cases pin that. |
| `a == b` as an expression on two values of open data (`merge_edge_attributes`, `change_set.py:286`) | `PyValueEquality.PlainEqual(a, b)`. It has no identity test at the top, so a NaN does not equal itself. `PyValueEquality.Equal` and `PyEqualityComparer` are for `in`, a dict key, and an item of a container, where Python tests identity first. |
| `x in list`, `list.index(x)`, `list.remove(x)`, `list.count(x)` for nodes or edges | `Py.Contains`, `Py.IndexOf`, `Py.Remove`, `Py.Count`. A Python container tests identity first and `==` second, with the list item on the left, and the helpers do the same. Never `List.Contains` for these types. |
| A set or a dict with nodes or edges as keys | Pass `PyEqualityComparer<T>.Instance`, which tests identity first |

### R19. Import cycles and assemblies

- C# has no import order, so a cycle inside one assembly is legal (GAPS G11).
- Port each cycle group of [queue.json](queue.json) (`"cycle": true`) in one unit of work. The MVP has two groups: group 10 with 18 modules around the driver contract, and group 85 with `graphiti`, `ingestion.phases`, and `ingestion.scheduler`.
- A lazy import inside a function, used to break a cycle, becomes a normal reference.
- The driver contract of group 10 lives in the core project. It never names a `Neo4j.Driver` type. Upstream core code uses the `neo4j` package in two places.
- `neo4j_time.DateTime` in `helpers.parse_db_date`: the Neo4j project converts a driver date to `PyDateTime` before the value enters the core.
- `httpx.AsyncClient` in `llm_client/openai_decisions_client.py` is an `HttpClient` that the constructor takes, as Python takes `http_client` (decision D32). No other MVP module uses `httpx` for a request. The request `http_client.post(url, headers=..., json=..., timeout=...)` follows these rules:
  - The default client of the constructor is `new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }`. httpx follows no redirect by default. The time limit of a request comes only from `Asyncio.WaitForAsync`, so the constructor accepts each value of `timeout_seconds` that Python accepts.
  - The body is the UTF-8 bytes of `PyJson.Dumps(body, ensureAscii: false, compact: true, allowNan: false)`. httpx 0.28.1 writes `json=` with `ensure_ascii=False`, the separators `,` and `:`, and `allow_nan=False`.
  - Send the body as a `ByteArrayContent` with `Headers.ContentType = new MediaTypeHeaderValue("application/json")`. `Content-Type` is a content header, and `StringContent` adds a `charset` that Python does not send. `Authorization` is a request header.
  - A client that the host passes keeps its own `Timeout`, which is 100 seconds by default. Python applies `timeout_seconds` to each request of a passed client. A host that wants the limit of Python sets the `Timeout` of its client to an infinite value. The lead accepts the difference.
  - Wrap the send in `Asyncio.WaitForAsync` with `timeout_seconds`. R4 gives the `catch` clause for `httpx.TimeoutException`.
  - `response.json()` reads the bytes of the body and ignores the charset of the header, as httpx does. Decode the bytes as strict UTF-8, skip a UTF-8 byte order mark, and call `PyJson.Loads`. httpx also detects UTF-16 and UTF-32, and the port does not: the lead accepts that difference.
  - `response.text` decodes the bytes with the charset of the header if .NET knows the name, and as UTF-8 with replacement if not. `response.status_code` is `(long)response.StatusCode`.
- `cross_encoder/openai_reranker_client.py` stays a ported unit (decision D30). Its `self.client` is the lead seam `Graphiti.Internal.LogProbabilityChat`.
  - The constructor is `OpenAIRerankerClient(LLMConfig? config = null, LogProbabilityChat? client = null)`. The branches that build `AsyncOpenAI` or unwrap an `OpenAIClient` are not ported. A null client throws by the default-object rule of R1.
  - `self.client.chat.completions.create(model=..., messages=..., temperature=0, max_tokens=1, logit_bias={...}, logprobs=True, top_logprobs=2)` is `Client.CreateAsync(model, messages, temperature: 0, maxTokens: 1, logitBias, logprobs: true, topLogprobs: 2, cancellationToken)`. `model` is `Config.Model`, and null where Python falls back to `DEFAULT_MODEL` (the design page lists this difference).
  - `messages` is the `List<Message>` that the unit builds with the ported `Message` of `prompts/models.py`. The call returns a `LogProbabilityChatResponse` with the shape of the Python response: `Choices[0].Logprobs?.Content?[0].TopLogprobs`, and each item has `Token` and `Logprob`. `Logprobs` and `Content` are nullable for the two `is not None` tests of upstream. The unit ports the reads of `openai_reranker_client.py:97-101` one by one.
  - `np.exp(x)` is `Math.Exp(x)`. `except openai.RateLimitError` follows R4.
- `_is_input_too_long(response)` in `llm_client/openai_decisions_client.py` reads the body of the response with `response.Content.ReadAsStream()`. After `PostAsync` with the default completion option the body is buffered, so the sync read needs no `.Result`.
- `from neo4j import unit_of_work` in `utils/bulk_utils.py:192`: the session member `GraphDriverSession.ExecuteWriteAsync(func, double timeout, cancellationToken)` takes the limit in seconds (decision D52). A C# delegate carries no attribute, so the `timeout` attribute of the work function is a parameter.
  - `Neo4jSession` converts the limit as `tx_timeout_as_ms` of the Python driver 6.1.0 does: whole milliseconds, half to even, and 1 ms for a value between 0 and 0.5 ms. `Neo4j.Driver` rounds up, so the port gives the driver whole milliseconds.
  - It throws `ArgumentException` for a negative value or `nan`, and `OverflowException` for `inf`, before the driver call. GAPS F46 gives the inputs where the two drivers differ.
  - Behavior of the Python driver below the contract is copied in the adapter of the Neo4j project, with a `// PORT:` comment. The core never copies it.
- The pilot (plan 0003, task 6) ports `search.search_filters` alone, although it is in group 10. For each name that it uses from the other 17 modules, it follows the pilot rule of R24. This is the one exception to the rule above.

### R20. Type hints

- Use the Python annotation. Part 3 gives the type table.
- For a function with no annotation (127 of 1,178), use the signature that [TYPE_HINTS.md](TYPE_HINTS.md) gives (GAPS G22).
- If TYPE_HINTS.md gives `Any` or `Unknown`, read the call sites and choose the real type. If the data is open, use the value model of R6.
- The `_cls: Any` parameter of the 55 methods of `GraphOperationsInterface` is `Type? cls`. A caller passes `typeof(X)` where Python passes `cls`, and `null` where Python passes `None`.
- `EmbedderClient.create` takes `List<string> inputData`: each upstream caller passes a `list[str]`. A `NDArray` of floats is a `List<double>`.
- Never change the upstream source to add a hint.
- A function that returns its input unchanged for a type outside its annotation returns `object?`. `parse_db_date(input_date: DateTime | str | None) -> datetime | None` ends with `return input_date`, so an int comes back as an int, and pydantic then reads it as a Unix time in the model that receives it. A typed result would throw where Python returns a value.
- A reply value that the annotation gives as `str` can have another type in open data: `llm_response.get('summary', '')` in `community_operations`. The port reads it with `Py.AsString`, which throws where Python passes the value on (GAPS F44). The same holds for a `group_id` record value in `get_community_clusters` (GAPS F45).

### R21. Upstream defects

The port reproduces upstream at the pinned commit, with its defects. The golden files record them. Never fix one.
Write `// PORT: upstream defect U<n>` at the place of each defect, so that a reviewer does not report it and nobody repairs it.

| Defect | Rule for the port |
| --- | --- |
| U1 | Keep the batched delete inside the transaction, for entity, episode, community, and saga nodes. The call fails with the driver's `DatabaseException`. Each `delete_by_group_id` and `delete_by_uuids` of the four node operations classes has the marker. |
| U2 | Keep the saga save query with the five parameters that are not passed |
| U3 | Keep `$group_idsAND e.source` with no space |
| U4 | Keep the full-text query `group_id:"a" OR group_id:"b" AND (q)` with no parentheses |
| U5, U6, U15, U16 | Add no `ORDER BY`. Keep each query as it is. |
| U7 | Keep the save that fails for a null embedding |
| U8 | Keep the write of `num_results` into the shared module-level recipe object (R17) |
| U9 | Keep the skipped source and target filters when `group_ids` is null |
| U10 | Keep the same parameter name for date filters in different OR groups |
| U11, F6 | Port `label_propagation` as it is. It does not end for some inputs. |
| U12, F14 | `Edge.Equals` is the Python test: `other is Node` and the UUIDs are equal. It is `false` for each other edge, also for the same object (R18). |
| U13 | Not product code. No rule. |
| U14 | Keep the two-name unpacking in `add_episode` with `update_communities=True`: destructure the result list into exactly two names with `Py.Unpack2`, which throws `ArgumentException` for any other count |
| U17 | Keep the concurrent `update_community` calls with no lock |
| U18 | Keep the merge onto the smallest UUID in `compress_uuid_map`, compared with `Py.CompareStrings` |
| U19 | `OpenTelemetryTracer.start_span` yields a second time in its `except Exception`. With an OpenTelemetry tracer, an exception in the body of a span leaves the `with` as `RuntimeError: generator didn't stop after throw()`, and the span has the error status and one exception event. `OpenTelemetryTracer.StartSpanAsync` reproduces it: it throws `InvalidOperationException` with that message and the exception of the body as the inner exception. The no-op tracer passes the exception of the body on. |
| F22 | Keep the `expired_at` behavior for a fact that arrives with an end date |
| F23 | Keep the base path of decision D17. The schema text stays at the end of the prompt. |

The four lookup methods that apply no group filter (`node_distance_reranker`, `episode_mentions_reranker`, `get_mentioned_nodes`, `get_communities_by_nodes`) stay as they are.

### R22. Judge bindings and wrong-type cases

- Each ported unit adds one file, `tests/Graphiti.Net.JudgeHost/Units/<Module>.cs`, with a static class `<Module>Bindings` in the namespace `Graphiti.JudgeHost.Units` and one method `internal static void Register(BindingTable table)`. `Bindings.cs` is lead code: it finds each such class and calls the method. A binding decodes the arguments, calls the product code, and encodes the result. It does nothing else.
- The usual binding names the Python parameters in order and gives the ported method: `table.Pure.Add("graphiti_core.helpers:lucene_sanitize", call => call.Signature("query").InvokeAsync(Helpers.LuceneSanitize));`. `InvokeAsync` converts each argument to the type of the C# parameter, takes a missing argument from the C# default, passes `CancellationToken.None` to a last token parameter, awaits a task, and encodes the result. An overloaded or generic method needs a cast to a `Func` type.
- A constructor, a property, or a method on an instance uses the explicit reads: `call => Codec.Result(new XException(call.Signature("uuid").String("uuid")))`. For a parameter with a default, read `Has("name")` only to choose between two constructor calls that Python also tells apart. Never write a default value in a binding.
- A unit that ports a member of another module for real (R24) puts the bindings of that member in the file of that module, for example `Units/Helpers.cs` for `helpers.validate_node_labels`. If the file exists, the unit adds its lines at the end of `Register`. `Add` throws for a name that is bound two times, so the host stops at its start.
- A unit adds each model of its module to the schemas, with the Python path of the class as the key: `table.Schemas.Add("graphiti_core.prompts.dedupe_nodes:NodeDuplicate", NodeDuplicate.ModelJsonSchema);`. The judge function `judge:schema_text` reads that dictionary (R7).
- A unit registers each `$model`, `$enum`, and `$ref` path that its cases use, and the encoder of each model type that a result holds, in `table.Registry`. Each path is registered one time, in the bindings file of the module that defines the name: the enum `GraphProvider` is in `Units/Driver.cs`, also when the cases of another module pass it. A path that two files register stops the host at its start. The unit of the module that defines a name registers each path of that module that a judge case names, also when only the cases of a later unit use it. It registers no encoder (`AddType`) for a model that no case returns.
- `Units/Builtins.cs` is lead code. It binds `builtins:str`, `builtins:repr`, and `builtins:list`, which cases of several units call on a `$ref`. A unit registers only the `$ref`.
- A model constructor `M(**kwargs)` is bound with a private helper of the unit file that passes `call.Keywords` to `M.ModelValidate` and rejects a positional argument with `JudgeTypeRejectedException`. `Units/SearchFilters.cs` shows it. An enum call `E(value)` is `call.Signature("value").InvokeAsync((Func<object?, E>)PyEnum.FromValue<E>)`.
- `Signature` has no mark for a keyword-only parameter (a name after `*`). A case that passes such an argument by position gets a result where Python raises `TypeError`. No case does it today (Part 5, O11).
- An exception type with no row in `Codec.PythonExceptionName` gives a `host_error`. If ported code throws such a type by a rule of R4, tell the lead. Only the lead changes `tests/Graphiti.Net.JudgeHost/Core/`.
- A `{"$ref": "fixtures:Name"}` argument is a custom type. `tests/Graphiti.Net.JudgeHost/Fixtures.cs` holds one `GraphTypeDescriptor` for each of the five classes of `port/judge/host-python/fixtures.py`, built through the public `GraphTypeDefinition`. The lead writes that file.
- A `$model` argument is decoded with `ModelValidate` (R7). A `$enum` argument is decoded with `PyEnum.FromValue<T>`. A `$ref` to a constant or a function is decoded to the ported member.
- A model that the product code never validates, for example `RawEpisode` (`utils/bulk_utils.py:105`), has no `ModelValidate`. Its unit file registers a private factory with `table.Registry.AddModel`. The factory reads each field with a typed read, and it gives a field that the data leaves out the default of Python. No product member is added for the judge.
- The typed read of `InvokeAsync` also gives a `ulong` from an int in its range, a value tuple from a tuple of the same length, and an `OrderedDictionary<K, V>` or a `Dictionary<K, V>` from a dict, with each key and each value read by the same rules. An `IReadOnlyCollection<T>` takes a list or a set. A value that is not a namespace is a library with no group for the parameter of `validate_prompt_library`, because it has no attribute with the name of a prompt group. A parameter of the exact type `IEnumerable<T>` takes a list or a tuple, because its Python parameter iterates the value. It takes no set: no case passes one. A `List<T>`, an `IReadOnlyList<T>`, and an `IReadOnlyCollection<T>` do not take a tuple. A list item whose C# type is nullable takes `None`.
- An argument for a union record of Part 3 (`SagaOrName`, `EpisodeOrEpisodes`): the binding reads the decoded value and builds the case by its runtime type. A `str` gives `SagaOrName.Name`, a `SagaNode` gives `SagaOrName.Node`, and `None` gives null. For a function with two overloads for `A \| list[A]`, the binding picks the overload by the runtime type. Both are a decode of the argument.
- A model in a result is encoded as `$type` with the Python class name and the Python field names. Register it with `table.Registry.AddType<M>("<Python class name>", m => [new("<python name>", m.Property), ...])`, with the property values as they are. Do not use `ModelDump`: it turns a nested model into a dict, and the Python codec keeps a `$type` for each nested model.
- A function that only changes its arguments returns nothing, so no pure case observes it (GAPS F20). Bind it only if a case names it. The flow layer covers it.
- `driver/neo4j/operations/search_ops.py` is reached only by the direct flow scenarios (GAPS F21). Its unit is not `parity_pass` before those scenarios pass.
- The order of keys in a dict is a contract only where it reaches prompt text or Cypher text (GAPS J15). R14 still applies to each dict that code iterates.

Wrong-type cases, decision D20 (GAPS F11):

- A case passes an argument that the C# parameter type cannot hold, for example `lucene_sanitize(42)`. The binding cannot make the call.
- The host then reports `JudgeTypeRejected`. The runner accepts that result only if the golden result is `TypeError` or `AttributeError`.
- Give each parameter the type that its Python annotation and its call sites give. Never widen a type to `object` to make a wrong-type case pass. Never add a guard for one. One exception: the 8 fields of `PromptRoutes` are `object?`. Each one holds an `LLMModel` or its nested record, and `flatten_routes` tests it with `is`, as Python tests it with `isinstance`.
- If Python handles `None` for a parameter (the annotation has `| None`, or the code tests for it), the C# type is nullable and the case runs.
- `GraphProvider` and `EpisodeType` are plain `Enum` classes. A member does not equal its value text, so a case that passes the text for an enum parameter is a type rejection. Never decode such an argument with `PyEnum.FromValue<T>`. Only a `$enum` tag gives an enum value.
- The reviewer found 23 more cases with a golden `TypeError` or `AttributeError` whose id does not hold `wrong_type`. The rule is the same for them.
- A function that ported code calls with a value from open data takes `object?`, because its Python caller passes each value: `parse_db_date` (R20) and `EpisodeType.from_str`, which the record parsers call with a record value. This is not a widened type for a judge case. The judge cases of such a function then run and pass.
- A case that passes a built-in model class as a custom type (`{"$ref": "graphiti_core.nodes:EntityNode"}` for a `type[BaseModel]` parameter) gets a `GraphTypeDescriptor` that the bindings file of the model builds from `M.ModelFields`: the name and `IsRequired` of each field. The descriptor has no kind for a `datetime` or a `dict` field, so a case that reads the kind of such a field stays listed (Part 5, O4).
- A built-in model with no `ModelFields` (`ExtractedEntity`, `ExtractedEntities`, `NodeDuplicate`, `NodeResolutions`) gets a descriptor that its bindings file writes by hand: the name and `is_required()` of each Python field. Its schema and its validation are not the ones of Python, and a `// PORT:` comment names each difference. No case reads them.
- Part 5 lists the wrong-type cases that this rule does not settle.

Cases that now pass:

- An implementer never edits `port/judge/expected-failures.txt`. It reports the ids of the cases that now pass.
- The lead removes only those ids from the file, in the pull request of the unit. The pull request lists each id (decision D38).
- Each other change under `port/judge/` is a patch file that the lead writes and the maintainer applies.
- A unit is `parity_pass` only when each covering case is out of the file, or stays in it with the host answer `JudgeTypeRejected` and a row in the wrong-type table of Part 5. The runner does not fail for a listed case that answers `JudgeTypeRejected` (decision D40), so the lead lists the covering ids of each unit and reads the answer of each one.
- The path `types:SimpleNamespace` has two uses: a driver in the 19 `pure.queries.fulltext_query.*` cases, and a prompt library in three `validate_prompt_library` cases. A path is registered one time, so the lead file `Units/Types.cs` registers it. `TypesBindings.Namespace` implements `IPromptLibrary` and `IPromptGroup` over the attributes of the namespace, by the R15 rows for `hasattr` and `callable`. The unit of `fulltext_query` extends it for a driver, with the lead.

### R23. Surfaces that the judge cannot reach

Each surface gets expected values that a script in `port/tools/` records from Python at the pinned commit, and a test in `tests/Port.Evaluation` that compares the port with them.
The unit that ports the surface delivers both. The lead writes or approves the recording script.

| Gap | Surface | Rule for the unit |
| --- | --- | --- |
| J1 | Exception messages | The `errors` unit records `str(exception)` for each class with representative arguments, also a list with two items and a non-ASCII name |
| J2 | Equality and hashing | The `nodes` and `edges` units record `a == b` for the pairs that GAPS names |
| J3 | Retry and backoff | The `llm_client.client` unit tests the Polly pipeline with a fake `TimeProvider`: 4 attempts, the retried exception types, each wait between 5 and 120 seconds |
| J4 | The LLM response cache | Record the cache key for fixed inputs. The key is the MD5 of `model:` and `PyJson.Dumps(messages, sortKeys: true)` (R18). |
| J5, J9, J12 | The provider wire format, malformed replies, native structured output | The end-to-end layer with an HTTP stub (plan 0004). No rule for a ported unit. |
| J6 | Tracing spans | Record span names and attribute keys with an in-memory exporter. Keep each name and key exactly (R5). |
| J7 | The SQLite state store file | Record the table definitions and one row of each table. Keep each SQL string exactly, by the string rules of R9. |
| J8 | Token usage tracking | Record tracker totals for scripted usage values |
| J10 | `SEMAPHORE_LIMIT` under load | One end-to-end test counts the highest number of calls in flight (R8) |
| J11 | Data written by Python and read by .NET | One end-to-end test loads a graph dump from Python. Never change a stored property name or a stored value shape. That is an ask-first item. |
| J13 | Surfaces with no scenario | A unit that only these surfaces reach cannot get `parity_pass`. Set it to `tests_pass` and name the surface in `notes`. Debt T12. |
| J14 | Four open behaviors | Port them as they are (R21). The evaluation tests hold the property that the graph equals the graph of some serial order. Debt T16. |
| J15 | Dict key order | R22 |

### R24. Skeleton, blocked work, and stubs

- Write no stub and no simplified version. If you cannot port something, write `// TODO(port): <reason>`, set the unit to `blocked`, and name it in [STATE.md](STATE.md).
- A skeleton member (plan 0004, layer 0) has the final signature and the body `throw new NotSupportedException("TODO(port): <Python name>");` with a `// TODO(port)` comment above it.
- The skeleton ports these in full, with no marker: an enum, a `Protocol` or an abstract class whose bodies are only `...` or a docstring, a `TypedDict`, a constant whose value is a literal or one environment read, and a body that is only `return None` or only `raise NotImplementedError`. Each one is the real behavior. The scanner of the porting skill reports each `NotImplementedException`, and the lead lists those sites for gate 4.
- A model in the skeleton has its fields as properties. Its unit adds the R7 members.
- The pilot runs before layer 0. A judge case that reaches a skeleton member cannot pass, so a pilot unit ports for real each member below that its code calls. The member goes in the target file of its module by R1.
  - `helpers.validate_node_labels` with `SAFE_CYPHER_IDENTIFIER_PATTERN`, and the constants `CHUNK_TOKEN_SIZE`, `CHUNK_OVERLAP_TOKENS`, `CHUNK_MIN_TOKENS`, and `CHUNK_DENSITY_THRESHOLD` of `helpers`.
  - The enums `nodes.EpisodeType` and `driver.driver.GraphProvider`.
  - The modules `prompts.models` and `prompts.prompt_helpers` in full.
  - `utils.datetime_utils.utc_now`. On `nodes.Node` and `nodes.EntityNode`: the fields, the validation of `labels`, `__hash__`, and `__eq__` (R18). A skeleton `Node` with reference equality lets a later unit pass its tests with the wrong meaning.
- A module whose Python bodies are only `...` or a docstring, for example an abstract base class, is ported in full. It has no `TODO(port)` marker, and its unit is not a skeleton.
- Each other name from a module that is not ported is a skeleton member, and so is each other name that `entity_node_ops` uses. That unit has no pure case. The lead adds the files to the unit's file list. Layer 0 and the later layers complete them.
- A skeleton member never throws `NotImplementedException`. Ported code catches that type to fall back from a driver operation (71 sites), and such a `catch` must not hide a member that is not ported yet.
- A call of a member that only the Neptune driver has (`save_to_aoss`, `run_aoss_query`) keeps a `// TODO(port)` marker and throws `NotSupportedException` at the call (decision D10). The code of the branch after the call is not ported, because the compiler rejects code after a throw. The marker does not block the unit. [STATE.md](STATE.md) lists each site, and the maintainer accepts or rejects each one at gate 4.
- Not allowed in product code: `dynamic`, the null-forgiving `!` operator, `#pragma warning disable`, `[SuppressMessage]`, an empty `catch` block that Python does not have, `async void`, `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()` on a task that can still run. One exception, in `Internal/GraphitiFacade.cs`: the sync `Dispose` and the cleanup after a failed build block on the close of the core, because `IDisposable` and a service factory are sync. Both block only off the Graphiti scheduler. A sync `Dispose` on the scheduler thread throws `InvalidOperationException` and names `DisposeAsync`. The cleanup on the scheduler thread starts the close, does not wait for it, and logs a failure of the close.
- Where Python reads a value with no test and the C# type is nullable, write `Py.NotNone(value, "<attribute>")`, and not `!`. `scripts/check.sh` finds the operator after a name, a closing parenthesis, or a closing square bracket, when no `=` follows (`port/tools/scan_port.py`). It does not find the operator after a space, unless a dot and a name follow.
- A Python `except X: pass` is a `catch` block with the comment `// PORT: Python passes`.
- If the same mistake shows up in two units, the lead changes this file and the affected units are ported again. Nobody patches output by hand.

### R25. Tests

- Port the upstream pytest file that covers a module into `tests/Port.Evaluation`, with the same test names. These tests are temporary (decision D6).
- A test method keeps its Python name in snake_case, so that a search finds it. A test class keeps its Python name.
- If a pytest file covers several modules, port the tests whose code under test is ported with your unit. Name each other test in the `notes` of the module that it needs, so that the unit of that module ports it.
- The ported pytest tests are mostly property tests. In the review of `content_chunking` they found none of 13 seeded defects, and a recording of real Python results found most of them. So a unit with logic also delivers a recording: a script in `port/tools/` calls each function of the module with generated inputs from a fixed seed and writes the results to `tests/Port.Evaluation/values/`, and one test compares the port with each result (R23).
- A recording also holds each module-level constant of the module by name (`getattr(module, NAME)`), and the test compares each one. The review of the query modules found 13 query constants with no test: no function of their module returns them. The same holds for a number, for example `SEMAPHORE_LIMIT`.
- A test never compares a value of the port with itself. The expected value comes from Python or from a literal. A test of concurrency records the largest count of calls that ran at the same time, and it has a time limit, so that a broken port fails and does not hang.
- A unit of `driver/neo4j/operations` delivers a recording too. The script runs the Python operations class with a fake executor and a fake transaction. It records the query text, the parameter keys in order, each value with its type, the log lines, and the result or the exception of each call. One test runs the same inputs through a fake `QueryExecutor`. `port/tools/record_entity_node_ops.py` shows the form.
- A test compares a recorded pydantic message after it removes `, input_value=..., input_type=...]` and the link line from the Python text. `ValidationException` does not carry them. A `KeyError` is compared by type only.
- A pytest file that mocks an internal class is ported as it is. A recording is the second place where a fake between internal classes is allowed ([TESTING.md](../docs/TESTING.md)): the recording of a model module with driver methods (`nodes`, `edges`) and of the driver contract replaces `GraphDriver`, its session, the graph operations interface, and the embedder on both sides. The test compares the recorded calls with Python, and never with the port.
- Each fake method that returns a task runs `await Task.Yield();` first, and then records the call. A call that the port does not await then lands late, and the comparison with the recording fails. A fake that completes at once cannot show a missing `await`.
- If the next fake call also only yields, a yield alone does not show a missing `await`, because the loop runs the calls in order. Give the fake a yield and a short delay there (`Slow.Pause()` in `Neo4jDriverFakes.cs`).
- A unit with async methods adds one theory with no Python original: it calls each async method with a canceled token and expects `OperationCanceledException`. The fake throws it with `cancellationToken.ThrowIfCancellationRequested()`. This is the test of the token rule of R8. One exception: a method that ports an `async def` with no `await` (R8) does not read the token. Its test asserts that a call with a canceled token completes, as the Python coroutine does with a cancel pending.
- A test that calls a constructor with only the named arguments of a recorded case uses one reflection helper with `Type.Missing` for each other parameter (`LlmClientClientSupport.MakeConfig`). The C# defaults then run.
- A test of code that makes a file or a folder in the working directory removes it only if the test made it.
- That theory asserts that the fake recorded no call. A method with more than one awaited call adds a case for each later call: the fake cancels the token source at call k, and the test expects `OperationCanceledException` and no call after k.
- A test that calls a function that does not end for some inputs (upstream defect U11) first runs that function on a background thread and calls `Join(5000)`. If the thread does not end, the test fails the case and does not start the product call. `Neo4jGraphOperationsTests` shows the form.
- The canceled-token theory proves only that some call reads the token. So the test of a recording gives the product call the token of a live `CancellationTokenSource`. Each fake records the token that it gets, and the test asserts that each one equals the given token.
- A Python fake of a recording yields at the same place as the C# fake: `await asyncio.sleep(0)` first, and then the record. Without it, Python runs gathered calls one after the other, and a port that runs them in series gives the same list.
- For each function with a concurrency limit, the fakes count the calls in flight on both sides, and the recording has a case with more calls than the limit.
- Each async test has a time limit (`Timeout` on the test attribute). The limit does not stop a body that blocks the one thread of the scheduler: each later test then waits behind it. So a test that enters `Asyncio.Run` goes through `LoopGuard.RunAsync` (`tests/Port.Evaluation/LoopGuard.cs`, which the E2E project links). The limit is 4 seconds. A test on a real database passes a larger limit, under the time limit of the test. If the scheduler does not answer after the limit, the guard ends the test host with a message, and the run fails and does not hang.
- A body that Python gives no token has no token to record: the decorated function of `handle_multiple_group_ids` is a lambda with no token parameter (R16). There the test uses a fake that reads no token, and a canceled token must give `OperationCanceledException` and no call of the fake.
- `asyncio.run` cancels the tasks that a failed call leaves and runs them to their end. `Asyncio.Run` returns at once, and the tasks run on. So the test of a recorded call that fails or is canceled gives the scheduler 50 turns before it reads the event list (`SearchValuesTests`).
- A recording of a filter with a limit has an input on the limit and an input that the filter removes. If an earlier filter uses the same limit, check that it leaves such inputs for the later one.
- A recording keeps the calls and the log lines in one ordered list, so that a log line that moves across a call fails the test. It calls each method with its defaults one time, and it records `a == b` and `hash(a)` for the pairs of GAPS J2.
- A recording calls each function one time with each optional argument left out, on both sides. A changed default then fails a test.
- A value that the code reads from the first item of a list gets a case where the items differ: episodes of two groups, with another content and another `valid_at`.
- The theory that cancels at call k compares with Python. The recorder runs the same cancel on Python, and it records the count of attempts and the log events after call k. A point where the port makes more attempts or writes more log lines is the late cancel of GAPS F33. A list `LateCancel` of the test names each such point, and a listed point that agrees with Python fails the test.
- The recorder cancels at each counted call, the last one included, and it keeps the log lines after call k. A listed point names its kind: `logs`, `attempts+N`, `result` (a full result at the last call), `cleanup`, or `failure` (GAPS F33). The events of Python after k stay a part of the events of the port, in their order. A test makes sure that each listed key names a recorded cancel point.
- A recorder replaces each random value with a placeholder, also where the value is a dict key, for example the `uuid_map` of `_extract_and_resolve_nodes`.
- A recording has no order that comes from random `uuid4` values. `ingestion/phases.py:943` sorts the missing uuids, so the recorder gives the nodes distinct embeddings and the order of the matches decides.
- The recorder keeps the `extra` fields of a log record, and the test compares them with the values of the open logger scopes (R5).
- Each driver, session, and transaction event of a fake driver records the database of its driver. A query on the shared driver, where Python uses the clone of the request, then fails a test.
- A test that changes shared state of another module puts the state back, and its class runs alone. `Graphiti.search` changes the limit of a shared recipe (GAPS U8). So `GraphitiValuesTests` runs with no parallel class, each run puts the two limits back, and the recorder does the same after each case. In the end-to-end project, a class that makes `Graphiti.search` run with a limit other than the default, or that writes the limit of a shared recipe, is in the collection `Shared search recipes`. That collection runs alone, and each such test puts the two limits back. The public `SearchAsync` with `NumResults` is one such call.
- A graph call inside a write with a time limit gets the linked token of the limit, not the token of the caller. The cancel points of the cases with a real `Graphiti` prove the link.
- A fake that cancels calls `CancellationTokenSource.Cancel()`. It runs the callbacks of the token at once on the scheduler thread, as `task.cancel()` acts at one point of the loop in Python. `CancelAsync` runs the callbacks on the thread pool, so the count of attempts changes from run to run. A `CancelAsync` call before the port runs is not such a call. The same holds for a test body on the scheduler that ports `task.cancel()`. The clocks of `VirtualLoop` wait only for the scheduler and for a call that `VirtualLoop.Track` holds. So a `CancelAsync` there can arrive after a sleep of the virtual clock ends. `LoggingStore` tracks each call of its inner store, because the SQLite store runs each call on the thread pool. A body on `VirtualLoop` starts no other work on the thread pool.
- A recording wrapper of a real object, for example the state store, records the token of each call, as a fake does. The reviews of `phases` passed `CancellationToken.None` to five store calls, and each test passed.
- A test that reads or writes module state of the port, for example the digest cache of `delta`, makes those calls in one turn of the scheduler (`LoopGuard.RunAsync`). Product code reaches such state only on the scheduler, so a call from a test thread races with the other tests. Keep a turn short: a turn of 1 second made the loop guard of another test stop the run. Test classes that assert the content of such state share one test collection.
- `Asyncio.WaitForAsync` disposes its linked source when the call ends. So a test cannot prove the link from a recorded token to the token of the caller after the call. A cancel at call k inside the limit proves it.
- A values file stays under 1.5 MiB (1,572,864 bytes). A recording stores a long rendered text as its SHA-256 and its length, and the test hashes the text of the port (`record_decisions_client.py`).
- Do not drop a test because it is hard to port. Mark it `// TODO(port)` and set the unit to `blocked`.
- Write no new unit test that restates the implementation. A gap in coverage gets a judge case, a property test, or an end-to-end test.
- The table in Part 3 maps pytest constructs to xUnit.

## Part 2. `PyCompat` helpers

`src/Graphiti.Net.PyCompat`, namespace `Graphiti.PyCompat`. Plan 0003, task 3 builds the helpers that the queue needs, in the order that units need them.
Each helper has property tests against values that Python recorded. An implementer calls a helper and never writes its logic again at a call site.
If a unit needs a helper that does not exist, stop and tell the lead.

| Class | Members | Matches Python |
| --- | --- | --- |
| `Py` | `Repr`, `Str`, `Format`, `Truthy`, `Len`, `Get`, `GetNullable`, `SetDefault`, `Unpack2`, `DeepCopy`, `EnumValue` | `repr`, `str`, format specs, `bool(x)`, `len`, `dict.get`, `dict.setdefault`, tuple unpacking, `copy.deepcopy`, `enum.value`. `Format` has the specs that upstream uses and throws `InvalidOperationException` for `e`, `g`, `%`, `n`, `#`, and grouping with `0`. |
| `Py` | `AsBool`, `AsLong`, `AsDouble`, `AsString`, `AsList`, `AsTuple`, `AsDict`, `AsPyDict`, `AsDate`, `AsDateTime`, `AsBytes`, `AsEnum<T>` | A typed read from the value model. A wrong type throws `InvalidCastException`. |
| `Py` | `TrueDiv`, `FloorDiv`, `Mod`, `Int`, `Float`, `Comb` | `/`, `//`, `%`, `int()`, `float()`, `math.comb` |
| `Py` | `At`, `Slice`, `CompareStrings`, `StringComparer` | Indexing and slicing by code point with negative indexes, string order by code point |
| `Py` | `Contains`, `IndexOf`, `Remove`, `Count`, `SortInPlace`, `LessThan`, `Combinations`, `ZipLongest` | `in`, `list.index`, `list.remove`, `list.count` with the identity test first, `list.sort` with the comparisons of CPython, `<` on two values, `itertools.combinations`, `itertools.zip_longest` |
| `Py` | `Uuid4`, `Uuid4Hex`, `Uuid5`, `NamespaceUrl`, `TimeSeconds` | `str(uuid4())`, `uuid4().hex`, `str(uuid5(...))`, `uuid.NAMESPACE_URL`, `time.time()` |
| `PyText` | `Lower`, `Upper`, `Title`, `Capitalize`, `IsAlnum`, `IsAlpha`, `IsDigit`, `IsSpace`, `IsUpper`, `IsLower`, `Split`, `RSplit`, `SplitLines`, `Strip`, `LStrip`, `RStrip`, `Replace`, `Count`, `Find`, `RFind`, `Index`, `CodePoints`, `Ord`, `Chr`, `EncodeUtf8`, `DecodeUtf8`, `DropLoneSurrogates` | The `str` methods with full Unicode case mapping, Python's whitespace set, and positions in code points. A lone surrogate stays as it is. The tables come from the pinned Python build. |
| `PyJson` | `Dumps`, `Loads` | `json.dumps` with `ensure_ascii`, `indent`, `sort_keys`, `default`, the compact separators `,` and `:`, `allow_nan`, Python float text, `NaN` and `Infinity` tokens, and keys that are not strings. `json.loads` into the value model. |
| `PyAst` | `LiteralEval` | `ast.literal_eval` for strings, bytes, numbers, `True`, `False`, `None`, lists, tuples, sets, and dicts. A complex number and `...` throw `ArgumentException`, where Python returns a value. A `\N{name}` escape resolves only the names of the generated table (about 400 common characters), and each other name throws `FormatException`. |
| `PyDateTime` | A struct. `IsNaive`, `IsUtc`, `IsoFormat`, `Str`, `Repr`, `ReplaceUtc`, `ToUtc`, `ToDateTimeOffset`, the field properties, the comparison operators, equality and hashing, and the static `FromIsoFormat`, `UtcNow`, `FromDateTimeOffset` | A `datetime` of Python 3.12, aware or naive, to the microsecond |
| `PyRegex` | `Space`, `SpaceClassBody`, `Word`, `Digit`, `Boundary`, `Split` | The character classes of `re` for `str` patterns, and `re.split` |
| `PyTuple`, `PyDict`, `PySet<T>`, `PyEqualityComparer<T>` | Types | A tuple, a dict with keys of any type in insertion order, a set that iterates in insertion order, a comparer that tests identity first |
| `PyEnum`, `PyEnumMemberAttribute` | `FromValue<T>`, `Name`, `Value`, `ClassName` | The Python member name and value of an enum member, and `E(value)` |
| `PyLruCache<TKey, TValue>` | Type. `GetOrAdd`, `Hits`, `Misses`, `Count` | `functools.lru_cache` |
| `PyRandom` | Type. `Random`, `Uniform`, `SampleRange` | The `random` functions that upstream calls, with a seed for tests. The sequence is not Python's (GAPS F9). |
| `PyEnv` | `Str`, `Int`, `Float`, `Truthy`, `Use` | `os.getenv` with the conversions of R17. `Use` is the seam for tests. |
| `PyPatch<T>` | Type. `Value`, `Use(value)` | A module-level value that an upstream test replaces with `monkeypatch.setattr` (R17, R25) |
| `Py.NotNone(value, "attribute")` | Method | A method call on a value from open data that can be `None`: the value, or `MissingMemberException` with the text of Python (R4) |
| `PyText.Title(s)` | Method | `str.title()`, with the Unicode tables of Python: the title of a schema field (R15) |
| `PyText.Cleandoc(s)` | Method | `inspect.cleandoc(s)`: the schema description of a custom type (R15) |
| `PydanticType.StrMaxLength` | Member | `Field(max_length=n)` on a `str` field: the `maxLength` key of the schema (R7) |
| `Py.Receiver<T>(value, "method")` | Method | A method call on a value from open data: the value as `T`, or `MissingMemberException` with the text of Python for `None` and for another type (R4) |
| `Py.Int(value)` | Method | `int(x)` on a value from open data: a bool, an int, a float, text, or bytes. Another type throws the `TypeError` of Python (R4) |
| `Py.ZipStrict(a, b)` | Method | `zip(a, b, strict=True)`: the pairs, then `ArgumentException` with the text of Python if one input is longer (Part 3) |
| `Py.Iter(value)` | Method | `for x in value` and `sep.join(value)` on a value from open data (R6) |
| `PyNumpy.AddReduce(values)` | Method | The sum of a float64 array or of a float32 array with the bits of numpy (R18) |
| `PyNumpy.ArgSort(values)` | Method | `np.argsort` of a float32 array with the default kind, in the order of numpy also for equal values (R18) |
| `PyHash` | `Blake2b` | `hashlib.blake2b` with a digest size |
| `Pydantic` | `Str`, `Int`, `Float`, `Bool`, `DateTime`, `Any`, `Enum<T>`, `Literal`, `StrIntFloat`, `Callable<T>`, `Optional`, `OptionalValue`, `List`, `Dict`, `Model`, `Tagged`, `Wrap`, `Wrap<TException>`, `SubclassOf<T>`, `Dump`, `DumpFields`, `JsonValue`, `DumpJson`, `LoadJson`. Also `PydanticFields` (`Required`, `Default`, `Ge`, `Gt`, `MaxLength`, `ThrowIfAny`), `PydanticField`, `IPydanticModel`, `ValidationException`, `PydanticSchema` (`Required`, `Default`, `Factory`, `ToDict`), and `PydanticType`. | Lax-mode coercion, the wrapping of a validator's `ValueError`, `ValidationError` with pydantic's text, the text of `json.dumps(model_json_schema())`, and pydantic's JSON mode. R7 says how a model uses them. |
| `Asyncio` | `Run`, `OnLoopAsync`, `GatherAsync`, `SemaphoreGatherAsync`, `CreateTask`, `WaitForAsync`, `ShieldAsync`, `ToThreadAsync`, `CallHostAsync`, `IsCancellation`, `IsOnLoop`, `Scheduler`, `SleepAsync`, `AsyncEvent`, `AsyncSemaphore`, `AsyncLock` | The asyncio calls of R8 on the exclusive scheduler. `CallHostAsync` has no Python form: it is the hop to a host service (decision D33). |

There is no general regex helper. Translate each pattern by hand (R13).

Each helper throws the C# type that R4 gives for the Python error, because ported `catch` clauses depend on it:

| Helper | Throws |
| --- | --- |
| `PyDateTime.FromIsoFormat`, `Py.Int`, `Py.Float` on text that does not parse | `ArgumentException` (`ValueError`) |
| `PyJson.Dumps` for a type that JSON cannot hold, for example a datetime, a set, or a C# enum with no `default` | `InvalidCastException` (`TypeError`) |
| `PyJson.Loads` | `JsonException` (`JSONDecodeError`). Near the depth limit of the parser, it also throws `JsonException` where Python raises `RecursionError`. |
| `PyAst.LiteralEval` | `FormatException` for text that is not Python syntax (`SyntaxError`), `ArgumentException` for syntax that is not a literal (`ValueError`) |
| `PyText.EncodeUtf8` for a lone surrogate | `EncoderFallbackException` (`UnicodeEncodeError`) |
| `PyText.DecodeUtf8` in strict mode for bytes that are not UTF-8 | `DecoderFallbackException` (`UnicodeDecodeError`) |
| `Py.Int` on text or on a float, and `Py.FloorDiv`, for a result that a `long` cannot hold | `OverflowException`. Python returns a value here. See the limit in R4. |
| A `PyDateTime` comparison of a naive and an aware value, a `Py.As*` read of the wrong type | `InvalidCastException` (`TypeError`) |
| A `PyDateTime` result outside year 1 to 9999 | `OverflowException` (`OverflowError`) |

## Part 3. General rules from the porting skill

This part is the starting rulebook of the `port-python-to-dotnet` skill (`references/rules.md`), copied on 2026-11-25.
A row that Part 1 replaces now points to its rule.

### Layout and names

| Python | C# |
| --- | --- |
| One module | One file. R1 gives the path. |
| Module-level functions | Static methods on a static class named after the module (R2) |
| `snake_case` functions and variables | `PascalCase` methods and properties, `camelCase` locals and parameters |
| `_private` names | `private` or `internal` members (R2) |
| Module docstring and function docstrings | XML doc comments with the same text |
| Order of definitions in the file | Keep the same order |

### Behavior that differs without a compile error

These are the risky ones. Wrong code still builds and still passes simple tests.

| Python | C# | Rule |
| --- | --- | --- |
| `int` has no size limit | `int` and `long` wrap on overflow | Use `long` inside a `checked` context. Use `BigInteger` where the code relies on large values. |
| `-7 // 2` is `-4`, `-7 % 2` is `1` | `-7 / 2` is `-3`, `-7 % 2` is `-1` | Call `Py.FloorDiv` and `Py.Mod` (R18) |
| `/` on two ints gives a float | `/` on two ints gives an int | Call `Py.TrueDiv` (R18) |
| `round(2.5)` is `2` (rounds half to even) | `(int)2.5` cuts off and `2.5.ToString("F0")` gives `3` | Write `Math.Round(x, MidpointRounding.ToEven)`. `PyCompat` has no `Py.Round` yet (R18). Do not round by a cast or by formatting. |
| `len(s)`, `s[i]`, and slices count code points | `Length` and indexers count UTF-16 units | R12 |
| `pos += 2` or `s[pos:pos + 2]` steps over two code points | The same code steps over two UTF-16 units and can stop inside an emoji | A scanner can keep UTF-16 positions while it steps over characters that it tested and knows are ASCII. A fixed-width step or slice over text that it did not look at must count code points. Convert positions to code points before they reach output. |
| String formatting ignores locale | `ToString()` and `string.Format` use the current culture | Pass `CultureInfo.InvariantCulture`. Compare with `StringComparison.Ordinal`. |
| `if x:` is false for `None`, `0`, `""`, and empty containers | A condition must be `bool` | Write the exact test meant: `is not null`, `!= 0`, `Count > 0`, `!string.IsNullOrEmpty`. For open data use `Py.Truthy`. |
| `x or default` | `x ?? default` only covers null | Use `??` only when `x` cannot be `0`, `""`, or empty. If not, write the full condition. |
| `dict` keeps insertion order | `Dictionary` has no defined order | R14 |
| `==` on lists, dicts, and dataclasses compares contents | `==` on classes compares references | Use records for value types. Use `SequenceEqual` for collections. |
| Default argument values are created one time | Not allowed for mutable values | Use `null` as the default and create the value inside |
| Mutable default shared between calls on purpose | No equivalent | Make it a field and tell the lead, who adds it to GAPS.md |
| `str` and `bytes` | `string` and `byte[]` or `ReadOnlySpan<byte>` | R12: `PyText.EncodeUtf8` and `PyText.DecodeUtf8` at each boundary |
| `datetime.now()` is naive local time | `DateTime.Now` is local with a kind flag | R11 and R17 |

### Regular expressions

Python's `re` and .NET's `Regex` look alike and differ in details that change output. R13 lists each site.

| Python | C# | Rule |
| --- | --- | --- |
| `re.match(p, s)` | No direct form | `Regex.Match` with `\A` in front of the pattern |
| `re.fullmatch(p, s)` | No direct form | Wrap as `\A(?:p)\z` |
| `re.search`, `re.findall`, `re.finditer` | `Match`, `Matches` | `findall` returns groups when the pattern has them. Find out which the code uses. |
| `\Z` means the very end | `\Z` also matches before a final newline | Write `\z` |
| `(?P<name>...)`, `(?P=name)` | `(?<name>...)`, `\k<name>` | Rewrite |
| Replacement `\1`, `\g<1>`, `\g<name>` | `$1`, `${1}`, `${name}` | Rewrite. A literal `$` in a .NET replacement is `$$`. |
| Replacement text that comes from data | A `$` in it is read as a group reference | Use a `MatchEvaluator`, and handle Python's own backslash escapes in the data as Python does |
| `re.sub(p, r, s, count=n)` | `new Regex(p).Replace(s, r, n)` | The static method has no count |
| `re.IGNORECASE` | `RegexOptions.IgnoreCase` uses culture rules | R13. Three characters differ, and the rule writes them out. |
| `re.ASCII` | No flag | Write the ASCII classes out. Never use `RegexOptions.ECMAScript` (R13). |
| `re.VERBOSE`, `re.DOTALL`, `re.MULTILINE` | `IgnorePatternWhitespace`, `Singleline`, `Multiline` | Direct |
| `\w`, `\b`, `\s`, `\d` on Unicode text | The .NET classes hold different characters | R13 |
| `.` matches one code point | `.` matches one UTF-16 unit | An emoji is two matches. Use an explicit surrogate-pair pattern where it matters. |
| Possessive quantifiers `*+`, `++` | Not supported | Rewrite as an atomic group `(?>...)` |
| A compiled pattern at module level | A `static readonly Regex`, or `[GeneratedRegex]` | Do not build a `Regex` inside a loop |

### Text and Unicode

R12 replaces this table. Three rows stay:

| Python | C# | Rule |
| --- | --- | --- |
| `unicodedata.normalize(form, s)` | `s.Normalize(NormalizationForm...)` | Results depend on the Unicode version of each runtime. Stop and tell the lead, who records both versions in DECISIONS.md. |
| `s.encode("ascii", "ignore")` | No one-call form | Filter to code points below 128, then encode |
| A string can hold a lone surrogate, and a lone high surrogate next to a lone low one stays two code points | So can a `string`, but a high and a low surrogate side by side read as one character. A recorder in `port/tools/` never puts the two side by side. | The judge protocol carries lone surrogates. Use `PyText.DropLoneSurrogates` where Python cleans the text. |

### Types

| Python | C# |
| --- | --- |
| `Optional[T]`, `T \| None` | `T?` with nullable reference types on |
| `X \| Y` of different shapes | A sealed abstract record with one derived record for each case. If Python names each case with a class (`CommitTurn`, `CommitWait`, `CommitRecover`), each case is a top-level record. The base record has the name of the alias, or a name that the lead gives (`BeginCommitResult`). If Python names no case (`str \| SagaNode`, `EpisodicNode \| list[EpisodicNode]`), the cases nest in the base record behind a private constructor. The base record then has the name `<A>Or<B>` (`SagaOrName`, `EpisodeOrEpisodes`), and it is in the first module in queue order whose function reads the value. |
| An annotated union whose value comes from open data: a node dict, a record | `object?` by R6. The row above is for a value that ported code builds. The same holds for a private function whose one caller passes `context.get(...)`: its parameters are `object?`, and it gets no typed twin. A default `False` of such a parameter is `null`, because C# has no `false` default for `object?` and the body only tests truth. `_build_edge_types_section` shows it. |
| A union of scalars, for example `str \| int \| float \| None` | `object?` that holds a value of the model of R6. Read it with `Pydantic.StrIntFloat`, in `Pydantic.Optional` if `None` is a member. |
| `Any`, `dict[str, Any]` | Find the real type. If the data is open, use the value model (R6). Never `dynamic`. |
| `list[T]` | `List<T>`. Python lists are mutable and shared, so keep `List<T>` in signatures too. |
| `tuple[T, ...]` | `IReadOnlyList<T>` |
| `tuple[A, B]` | A value tuple `(A, B)` for a return value or a local. A named record if it is stored in a field or crosses three or more calls. This row and the `list[T]` row differ from the skill's rulebook on purpose: upstream returns many pairs and shares mutable lists. |
| `datetime`, `date` | `PyDateTime` (R11), `DateOnly` |
| `dict[K, V]`, `set[T]` | R14 |
| `Protocol`, `ABC` | R15 |
| `Enum`, `TypedDict`, `dataclass`, pydantic `BaseModel` | R7 |
| `Callable[[A], B]` | `Func<A, B>`, or a named delegate when Python names the type |
| `type[BaseModel]` as a value | For a custom entity or edge type: `GraphTypeDescriptor` (R15). For a response model parameter: `IResponseModel` (R7). A descriptor implements it, and each built-in response model has a static `ResponseModel` object. |

### Pydantic models and JSON

R6 and R7 replace this section. `JsonSerializer` does not bind models in this port.

### Async

R8 replaces this section.

### Control flow and resources

| Python | C# |
| --- | --- |
| `with open(...) as f` | `using var f = ...` |
| `async with` | `await using` |
| A context manager whose `__exit__` suppresses exceptions | `try`, `catch`, `finally` written out. `Dispose` cannot see the exception. |
| `try` / `except A as e` / `else` / `finally` | `try` / `catch (A e)` / code after the try guarded by a flag / `finally`. The flag is a separate `bool`. Never use the result as the flag: a call that returns `None` runs the `else` branch in Python. If the `else` body uses a result of the `try` body, write the `else` body after the call inside the `try`: set `returned = true` between them, and filter the catch with `catch (A) when (!returned)`, so that an exception of the `else` body is not caught. `EpisodicNode.GetByUuidAsync` shows the form. |
| `match x:` with `case` | `switch` with the cases in the same order. `case _:` is `default:`. |
| `for ... else`, `while ... else` | A flag that the loop sets when it breaks, and an `if` after the loop |
| List, dict, and set comprehensions | LINQ, ended with `ToList()`, `ToDictionary()`, or `ToHashSet()` so that they run one time. For a dict whose order is read, fill an `OrderedDictionary` in a loop. |
| A generator expression passed to `sum`, `any`, `all`, `min`, `max` | LINQ with no `ToList()` |
| `for i, x in enumerate(xs)` | `foreach (var (i, x) in xs.Index())` |
| `zip(a, b)` | `a.Zip(b)`. Both stop at the shorter input. |
| `zip(a, b, strict=True)` | `foreach (var (x, y) in Py.ZipStrict(a, b))`. It gives the pairs and then throws the `ValueError` of Python. Never compare the counts first: Python runs the loop body for each pair before it raises. |
| `xs[a:b]`, `xs[-1]` on a list | `Py.Slice(xs, a, b)`, `xs[^1]` |
| `a, b = pair` | Deconstruction. For a list of unknown length, see R21 (U14). |
| `assert x` | `Debug.Assert(x)`. Python runs asserts unless it is started with `-O`. If the assert guards behavior that a case observes, stop and tell the lead. |

R4 holds the exception rules. R16 holds decorators and generators.

### Tests

| pytest | xUnit |
| --- | --- |
| A test function | A `[Fact]` method |
| `@pytest.mark.parametrize` | `[Theory]` with `[InlineData]` or `[MemberData]`. Inside `TheoryData` rows, write values as tuples. |
| A fixture | Constructor setup, `IAsyncLifetime`, or a class or collection fixture |
| `tmp_path` | A temp folder created in the constructor and removed in `Dispose` |
| `monkeypatch.setattr(module, 'NAME', value)` on a module-level value | `using (Module.Name.Use(value))` on the `PyPatch<T>` field of R17 |
| `monkeypatch.setattr` or `mock.patch` on a module-level function or on a method of a class | The test runs the real function in two cases. In the first, a fake at the network boundary gives the result that the patched function returns, and no assertion reads the arguments of the patch. In the second, the arguments of the patch pass unchanged into one call that the fake sees, and the test asserts that call. A `// PORT:` comment names the patch and the fake, and the Python oracle must show that the fake gives the result of the patch. Each other test keeps `// TODO(port): O10` with the name of the recorded case that covers the same behavior, and the `notes` of the unit name it (Part 5, O10). This does not block the unit. |
| `monkeypatch`, `mock.patch` on an object that the code receives | An injected interface with a fake. Use `PortServices.Use(time: ...)` for clocks and a stub `HttpMessageHandler` for HTTP. Never assign `PortServices.Time` in a test: tests run at the same time. |
| `pytest.raises(X, match=...)` | `Assert.Throws<X>`, then an assertion on the message |
| `caplog` | A fake `ILoggerFactory` in `PortServices.Use(loggerFactory: ...)` that collects entries |

## Part 4. Gap map

Each entry of [GAPS.md](GAPS.md) and the rule that answers it.

### Language gaps

| Gap | Subject | Rule |
| --- | --- | --- |
| G1 | `repr` of a list or dict in prompt text | R10, R11 |
| G2 | `json.dumps` text in prompts | R10, R11 |
| G3 | The pydantic JSON schema text | R7 |
| G4 | `str(datetime)` and `isoformat()` | R11 |
| G5 | Pydantic models, validators, and coercion | R6, R7 |
| G6 | Custom entity and edge types | R3, R15, decision D26 |
| G7 | Duck typing | R15 |
| G8 | `typing.Protocol` | R15 |
| G9 | Decorators | R16 |
| G10 | Module-level configuration from the environment | R17 |
| G11 | Import cycles | R19 |
| G12 | The built-in `hash()` | R18 |
| G13 | Stable sort and tuple comparison | R14 |
| G14 | Dict and set iteration order | R14 |
| G15 | Code points in length, slicing, and case mapping | R12 |
| G16 | Regular expressions | R13 |
| G17 | `numpy` vector math | R18 |
| G18 | Generators | R16 |
| G19 | `semaphore_gather` and `asyncio.gather` | R8 |
| G20 | The index build in the `Neo4jDriver` constructor | R17 |
| G21 | Exceptions | R4 |
| G22 | Functions without type hints | R20 |

### What the judge cannot reach

| Gap | Subject | Rule |
| --- | --- | --- |
| J1 | Exception messages | R4, R23 |
| J2 | Equality and hashing of nodes and edges | R18, R23 |
| J3 | Retry and backoff | R16, R23 |
| J4 | The LLM response cache | R18, R23 |
| J5 | The provider wire format | R23 |
| J6 | Tracing spans | R5, R23 |
| J7 | The SQLite state store file | R23 |
| J8 | Token usage tracking | R23 |
| J9 | Malformed JSON, empty responses, refusals | R23 |
| J10 | Rate limits and `SEMAPHORE_LIMIT` under load | R8, R23 |
| J11 | Data written by one build and read by the other | R23 |
| J12 | The provider path of the default client | R23, decision D17 |
| J13 | Surfaces with no scenario | R23 |
| J14 | Four behaviors that upstream leaves open | R21, R23 |
| J15 | The order of keys in a dict | R14, R22 |

### Upstream defects

| Gap | Subject | Rule |
| --- | --- | --- |
| U1 | Batched delete inside a transaction | R21 |
| U2 | Saga save with missing parameters | R21 |
| U3 | `$group_idsAND` with no space | R9, R21 |
| U4 | Full-text query for two or more groups | R9, R21 |
| U5 | Order of `get_community_clusters` | R21 |
| U6 | Queries with no `ORDER BY` | R9, R21 |
| U7 | Save with no embedding | R21 |
| U8 | `search` writes into a shared recipe object | R17, R21 |
| U9 | Skipped filters when `group_ids` is null | R21 |
| U10 | One parameter name for two date filters | R21 |
| U11 | `label_propagation` does not end | R21 |
| U12 | `Edge.__eq__` tests for `Node` | R18, R21 |
| U13 | `tests/test_add_triplet.py` fails | R21. Not product code. |
| U14 | Two-name unpacking with `update_communities=True` | R21 |
| U15 | Full-text rows with equal scores | R21 |
| U16 | `build_communities` pairs summaries in row order | R21 |
| U17 | Concurrent `update_community` | R8, R21 |
| U18 | Merge onto the smallest UUID | R14, R21 |
| U19 | An exception in a traced span becomes `RuntimeError` | R16, R21 |

### Findings from the case writers

| Gap | Subject | Rule |
| --- | --- | --- |
| F1 | `repr` of a datetime inside a list | R11 |
| F2 | `str(datetime)` uses a space | R11 |
| F3 | Float text and keys that are not strings in `to_prompt_json` | R11 |
| F4 | No escaping of episode text in prompts | R10 |
| F5 | `parse_list` falls back to `ast.literal_eval` | R11 |
| F6 | `label_propagation` does not end | R21 |
| F7 | `$` matches before a final line break | R13 |
| F8 | `fromisoformat` of Python 3.12 | R11 |
| F9 | Random sampling in `generate_covering_chunks` | R14, R18 |
| F10 | `normalize_l2` with `inf` or `nan` | R18 |
| F11 | Wrong-type inputs | R22, decision D20 |
| F12 | Constants are reached only through functions | R9 |
| F13 | Node labels built from a set | R14, decision D18 |
| F14 | Two edges with one UUID are not equal | R18, R21 |
| F15 | MinHash values up to 2^64 - 1 | R18 |
| F16 | Full Unicode case mapping in `str.lower()` | R12 |
| F17 | `\s` matches U+001C to U+001F | R13 |
| F18 | `calculate_cosine_similarity` returns the integer `0` | R18 |
| F19 | Ties in `rrf` and `maximal_marginal_relevance` | R14 |
| F20 | Functions that only change their arguments | R22 |
| F21 | `search_ops.py` is not on the public search path | R22 |
| F22 | `expired_at` for a fact with an end date | R21 |
| F23 | The schema text at the end of the prompt | R21, decision D17 |
| F24 | A record value of a type that no query returns | R4, R20 |
| F25 | An operations interface that returns `None` | Part 3, control flow |
| F26 | The text of `ZeroDivisionError` for an int score | R18 |
| F27 | A reranker record with a null `uuid` | R6 |
| F28 | `session.run` with a statement that fails at once | R8, R9 |
| F29 | An error at commit | R9 |
| F30 | A datetime offset that `DateTimeOffset` cannot hold | R9, R11 |
| F31 | `model_dump(mode='json')` keeps `nan` and `inf` | R7 |
| F32 | The embedding lookups of Neptune return text lists | R6, decision D10 |
| F33 | A cancel that arrives while the last call returns | R8 |
| F34 | The charset of a reply of the decisions client | R19 |
| F35 | Two wrong questions in one call of the decisions client | R6 |
| F36 | The timeout of an `HttpClient` that the host gives | R19 |
| F37 | `tx.run` with a statement that fails at once | R8, R9 |
| F38 | A cancel at the end of a transaction body | R8 |
| F39 | The compressed bytes of a change set | R16, R18 |
| F40 | A type error and a constraint error in one model input | R7 |
| F41 | An int outside 64 bits in a model reply | R4 |
| F42 | A `nan` limit of `execute_write_with_timeout` | R8 |
| F43 | The recursion depth of `UnionFind.find` | R4 |
| F44 | A community summary reply that is not a string | R20 |
| F45 | A `group_id` record value that is not a string in `get_community_clusters` | R20 |
| F46 | A transaction timeout outside the range of the .NET driver | R19 |

## Part 5. Open items

These items need the lead or the maintainer. An implementer that meets one stops.

| # | Item | Owner | Needed before |
| --- | --- | --- | --- |
| O1 | Closed on 2026-11-26. The runner accepts `JudgeTypeRejected` (decisions D20 and D40, pull request 21). | | |
| O2 | Closed on 2026-11-26. The judge host has the exception rows, the decoders, and the `$type` encoder (plan 0003, task P7, pull request 19). `Fixtures.cs` comes with the first unit that needs a custom type. | | |
| O3 | Closed on 2026-11-25. [The public surface design](../docs/design-docs/public-surface-design.md) and decisions D24 to D35 answer it: R1, R2, R3, R4, R8, R15, and R19 hold the rules. | | |
| O4 | 22 cases with `wrong_type` in the id have a golden result that is not `TypeError` or `AttributeError`, and so do 23 more cases with other ids. The skeleton reviews added 25 cases, in the two tables at the end of this part. The tables below give the proposed answer for each group. Decision D40 keeps each of these cases in `expected-failures.txt` with the answer `JudgeTypeRejected` until then. Plan 0005, task 2 closed 21 cases that need no decision, and the rows below say so. 45 pure cases stay listed, each with the answer `JudgeTypeRejected`. | Maintainer, by name at gate 4 | Gate 4 |
| O5 | Closed on 2026-11-26 (D38). The lead removes a passing case from `port/judge/expected-failures.txt`, and the deny list leaves that one file open (pull request 22). R22 holds the rule. | | |
| O6 | Closed on 2026-11-27 by decision D52. The session contract has `ExecuteWriteAsync(func, double timeout, cancellationToken)`, and `Neo4jSession` gives the limit to the driver (R19). | | |
| O8 | Closed on 2026-11-26. `.editorconfig` makes IDE0065 an error for each C# file, so a `using` directive above the namespace fails the build (plan 0003, task P1). | | |
| O7 | Closed on 2026-11-27. `Neo4jSession.ExecuteWriteAsync` is the first library callback, and it enters the scheduler again through `Asyncio.OnLoopAsync` (R8). The test `A_session_runs_a_query_and_a_write_function_and_closes` fails without that call. | | |
| O9 | `Asyncio.WaitForAsync` returns only after its call ends. A session call of Neo4j.Driver 6.5.1 takes no `CancellationToken`, so the write budgets of `ingestion/phases.py:1292` to `:1319` and of `utils/bulk_utils.py:197` do not hold if the ported save goes through a session. The adversarial review of the `Asyncio` helpers found it. Since layer 7 the work function of `execute_write_with_timeout` gets the token of the limit, so the limit holds for each call inside that reads its token. Since layer 9 a `run` call of the Neo4j adapter waits for the keys of its result, and consumes a result with no keys. `KeysAsync` and `ConsumeAsync` take no token either, so a cancel does not stop that wait (GAPS F50). | Maintainer | Layer 8 of plan 0004 |
| O10 | About 250 sites of the upstream tests replace a module-level function or a method with `monkeypatch.setattr` or `mock.patch`, for example `edge_ops.semaphore_gather` and `EntityNode.get_by_uuid`. C# cannot replace a static method. A seam for each such name changes the product code at each call. Most of these tests cover `utils/maintenance` and `ingestion`. On 2026-11-27 the lead decided a reading under D49 (Part 3, the row of `monkeypatch.setattr` on a function): a test that a fake at the network boundary can serve is ported, and each other test keeps its marker. | Maintainer: accept the tests that keep a marker, or ask for a seam | Gate 4 |
| O11 | The judge host lacks three members, and `tests/Graphiti.Net.JudgeHost/Core/` changes in its own pull request: a helper for a model constructor (each model unit writes the same private helper today), a mark for a keyword-only parameter in `Signature`, and `Fixtures.cs` (debt T18). | Lead | Layer 2 of plan 0004 |
| O12 | Closed on 2026-11-27. One class `PromptTypeWrapper` implements the 8 `<Module>Prompt` interfaces with explicit members. No caller in `graphiti_core/` tests the class of a group or iterates its attributes, and the 19 reads go through the `PromptLibrary` interface. The review of skeleton packet S3 kept the form. | Lead | Done |

Wrong-type cases of O4:

| Cases | Python result | Proposed answer |
| --- | --- | --- |
| `pure.dedupe.name_entropy.wrong_type_none`, `pure.text.get_default_group_id.wrong_type_none`, `pure.chunking._chunk_json_array.wrong_type_none`, `pure.chunking.should_chunk.wrong_type_episode_none` | A normal result for `None` | Make the parameter nullable if the Python code handles `None` by its own logic. If it works only by accident, the case stays open. |
| `pure.queries.get_range_indices.wrong_type`, `pure.queries.get_entity_edge_save_query.wrong_type`, `pure.text.get_default_group_id.wrong_type_str`, `pure.chunking._estimate_high_density.wrong_type_episode_str`, `pure.chunking.should_chunk.wrong_type_episode_str` | A normal result for text in place of an enum member. The enums are plain `Enum` classes, so the text equals no member and the default branch runs. For `get_default_group_id('falkordb')` Python gives `''`, and the member gives `'_'`. | The C# type cannot hold the value. Open: the maintainer accepts each difference by name, or extends decision D20. |
| `pure.filters.search_filters.construct_wrong_type_labels`, `pure.dedupe.ConcurrentIngestionConfig.state_store_wrong_type` | `ValidationError` | `ModelValidate` throws `ValidationException`. The case runs. |
| `pure.text.validate_group_id.wrong_type_zero`, `.wrong_type_false`, `pure.text.validate_group_ids.wrong_type_str`, `.wrong_type_str_invalid`, `pure.text.validate_node_labels.wrong_type_str`, `pure.text.validate_excluded_entity_types.wrong_type_str`, `pure.chunking.chunk_json_content.wrong_type_size_str` | A normal result or a domain error for a value of the wrong type | The C# type cannot hold the value. Open: the maintainer accepts each difference by name, or extends decision D20. |
| `pure.text.normalize_l2.wrong_type_str_elems`, `.wrong_type_scalar`, `pure.text._build_directed_uuid_map.wrong_type_short_pair`, `pure.text.compress_uuid_map.wrong_type_short_pair` | `ValueError`, `AxisError`, `IndexError` | The C# type cannot hold the value. Open, as above. |

The review of the judge host codec (plan 0003, task P7) found 23 more cases of this kind. The id of each one does not hold `wrong_type`. The count is a lower bound, because the review did not read the model constructors.

| Cases | Argument | Proposed answer |
| --- | --- | --- |
| `pure.dedupe.minhash_signature.none`, `pure.filters.node_search_filter_query_constructor.provider_none`, `pure.filters.edge_search_filter_query_constructor.provider_none`, `pure.text.truncate_at_sentence.none`, `pure.queries.get_fulltext_indices.none`, `pure.queries.get_nodes_query.none_name`, `pure.queries.get_episode_node_save_query.none`, `pure.queries.get_entity_node_return_query.none`, `pure.queries.get_entity_edge_return_query.none` | `None` for a parameter that the annotation does not make nullable. Python gives a normal result. | The first row of the table above: nullable only if the Python code handles `None` by its own logic. None of these nine cases meets that rule: each one passes a falsy test or a parameter that the function does not read. `pure.text.validate_group_ids.contains_none` met it, because `validate_group_id` declares `str | None`. It is closed with plan 0005, task 2: the parameter is an `IEnumerable<string?>?`. |
| `pure.models.EpisodeType.from_str.invalid_none`, `.invalid_int` | `None` or an int for a `str` parameter. Python raises `NotImplementedError`. | Closed with layer 2: `from_str` takes `object?`, because the record parsers call it with a record value (R22). Both cases pass. |
| `pure.dedupe.hash_shingle.seed_str`, `pure.queries.get_entity_edge_save_bulk_query.neo4j_truthy_aoss` | Text for an int, an int for a bool | The C# type cannot hold the value. Open, as above. |
| `pure.queries.get_entity_node_save_query.list_labels`, `.list_with_empty`, `.list_unsafe` | A list for `labels`. The function passes the value unchanged to `_validate_entity_labels`, which declares `str | list[str]`. | Closed with plan 0005, task 2: the parameter is an `object?`, as the helper of the port already is. A parameter that goes unchanged to a helper with a wider declared type takes the type of the helper. The cases pass. |
| `pure.queries.build_neo4j_fulltext_query.wrong_group_type`, `pure.ranking.rrf.inner_not_list_string`, `pure.text.validate_identity_properties.property_not_list_str`, `.property_not_list_tuple`, `pure.ranking.normalize_l2.two_dimensional_per_column`, `pure.text.normalize_l2.nested_2d` | Text or a tuple for a list, and a nested list for `list[float]` | The C# type cannot hold the value. Open, as above. |

Also, 19 `pure.queries.fulltext_query.*` cases pass a `types:SimpleNamespace` object as the driver. Their answer depends on how the unit binds that `$model`, so the unit of `fulltext_query` reports them.

The review of the first skeleton packet found 14 more cases of open item O4. Each one stays in `expected-failures.txt` with the answer `JudgeTypeRejected` (decision D40).

| Cases | Argument | Proposed answer |
| --- | --- | --- |
| `pure.text.validate_excluded_entity_types.known_custom`, `.unknown_with_types`. `pure.text.validate_entity_types.clean_model`, `.first_ok_second_conflict`. `pure.text.validate_identity_properties.none_props`, `.empty_props`, `.valid`, `.type_not_defined`, `.empty_property_list`, `.unknown_property`. | A built-in pydantic class (`ExtractedEntities`) as the value of a dict of custom types. The C# parameter is a `GraphTypeDescriptor`. | Closed with plan 0005, task 2. The three functions read only the names of the fields, so no field kind is needed. The judge host gives the `$ref` of `ExtractedEntities` a descriptor that its binding file writes by hand, as it does for `ExtractedEntity` (rule R22). The ten cases pass. |
| `pure.queries.get_vector_cosine_func_query.non_str` | `1` and `[2, 3]` for two parameters of the type `string`. Each caller passes Cypher text. | Accept by name. |
| `pure.parsers.entity_node.created_at_int` | `created_at: 12345`. `parse_db_date` returns the int unchanged, and pydantic reads it as a Unix time. | Closed with layer 2 by rule R20. The case passes. |
| `pure.parsers.episodic_node.source_none` | `source: None` reaches `EpisodeType.from_str(None)`. | Closed with layer 2, as `pure.models.EpisodeType.from_str.invalid_none`. The case passes. |
| `pure.queries.build_neo4j_fulltext_query.wrong_group_type` | A `str` for `group_ids`. Python iterates its characters and returns a query text. | The C# parameter is `List<string>?`, and each caller passes a list. Accept by name: the case stays listed with the answer `JudgeTypeRejected`. |

The reviews of skeleton packets S3 and S5 found 11 more cases. Each one stays in `expected-failures.txt` (decision D40) until the maintainer decides it at gate 4.

| Cases | Argument | Proposed answer |
| --- | --- | --- |
| `pure.prompts.lib.create_prompt_library.unknown_group_raises`, `.unknown_function_raises`, `.non_callable_raises` | The override value is a `PromptFunction` or a value that is not callable, where the dict takes a `ChatPromptFunction`. Python raises `ValueError` for the name before it reads the value. | Accept by name. |
| `pure.prompts.lib.validate_prompt_library.empty_dict_raises`, `.empty_namespace_missing_group_raises`, `.groups_without_functions_raises`, `.non_callable_function_raises` | A dict or a `types:SimpleNamespace` as the library. | The three namespace cases are closed with layer 4: the judge host decodes the namespace as a library (`Units/Types.cs`), and the cases pass. The dict case is closed with plan 0005, task 2: a dict has no attribute with the name of a prompt group, so the host decodes it as a library with no group, and Python and the port raise `ValueError`. |
| `pure.prompts.prompt_config.LLMModel.blank_id_raises` | A blank id with a wrong-type field beside it. | Accept by name. |
| `pure.prompts.decision_prompts.parse_list.json_non_list` | JSON text that is not a list, where the result type is a list of dicts (R20). | Closed with layer 3: `parse_list` returns `object?`, because its result is open data (R20). The case passes. |
| `pure.ranking.rrf.rank_const_fraction` | A fraction for `rank_const`, which is a `long`. Python gives the parameter no annotation, pyright infers `int` from its default, and no caller passes it. | Accept by name. Plan 0005, task 2 tried a `double` and took it back: a whole number of 2 to the power of 53 or more then gives another result than Python. |
| `pure.datetime.ensure_utc.wrong_type_date_only`, `pure.datetime.parse_db_date.neo4j_datetime_naive`, `pure.schemas.graphiti.AddEpisodeResults`, `.AddBulkEpisodeResults`, `.AddTripletResults` | No wrong type. The host answered `JudgeNotBound`, because it did not know a class, a model, or a schema. | Closed with plan 0005, task 2. The host registers `datetime:date` as a class. It builds a `LocalDateTime` for `neo4j.time:DateTime` and passes it through `Neo4jDriver.FromDriverValue` of the product. The three result records have a `ModelJsonSchema` member. The five cases pass. |
| `pure.ranking.SearchConfig.model_copy.recipe_update_unvalidated_bad_type` | `model_copy(update=...)` stores a value of the wrong type with no validation, and a typed property cannot hold it. | Accept by name. |

The review of skeleton packet S6 found 2 more cases. Each one stays in `expected-failures.txt` (decision D40) until the maintainer decides it at gate 4.

| Cases | Argument | Proposed answer |
| --- | --- | --- |
| `pure.dedupe.lsh_bands.strings` | Text items for `signature`, which is a list of `ulong` hash values. Python returns a normal result. Each caller passes a MinHash signature. | Accept by name. |
| `pure.dedupe.lsh_bands.tuple_input` | A tuple for `signature`. Python declares `Iterable[int]`, and its own caller passes the tuple of `_minhash_signature`. | Closed with plan 0005, task 2: the typed read of an `IEnumerable<T>` parameter takes a tuple (rule R22). The case passes. |

## Rule changes

| Date | Change | Units to port again |
| --- | --- | --- |
| 2026-11-25 | First version, from the skill's `references/rules.md` and GAPS.md. Decisions D20 to D23. | None. No unit is ported. |
| 2026-11-25 | After the adversarial review (plan 0003, results of task 1): `OnLoopAsync` for library callbacks, `Node` and `Edge` as classes with identity-first container helpers, `Pydantic.Wrap` for validators, the `PyDateTime` struct for each `datetime`, lone surrogates, Python escapes and inner trailing spaces in string literals, `re.IGNORECASE` and `\d`, one more set site, the retry log condition, the `except Exception` filter, call-time environment reads, and rules for 14 constructs that had none. Plain enums are not decoded from their value text. New open items O6 and O7. | None. No unit is ported. |
| 2026-11-25 | After the public surface design (plan 0003, task 2, decisions D24 to D35): ported code moves to `src/Graphiti.Net/Core/` and the namespace `Graphiti.Core`, the class `Graphiti` is `GraphitiCore`, the exception classes of the two error files are public in `Graphiti`, four provider error rows, `Asyncio.CallHostAsync` for host services, the custom type descriptor and `IResponseModel`, `PortServices.ClassifyModelFailure`, and `HttpClient` for the decisions client. Open item O3 is closed. | None. No unit is ported. |
| 2026-11-25 | After the adversarial review of the public surface design (plan 0003, results of task 2): the `using` placement covers each project and `global::Neo4j.Driver`, the default-object branches of `Graphiti.__init__`, XML comments on public exception members, the inner-exception constructor, the timeout and transport rows of R4, the server error wrap of the chat adapter, `Pydantic.Wrap<TException>`, `ModelFields` as an ordered dictionary, the exact `IResponseModel`, `Asyncio.Run` returns a task, the HTTP rules of the decisions client, the reranker unit on the seam `LogProbabilityChat`, and judge fixtures as descriptors. | None. No unit is ported. |
| 2026-11-26 | After the integrated review of the pull request stack. R12 sends `split(sep)` and `strip(chars)` to the `PyText` members. R12 has two rows for `replace`, and Part 2 lists `PyText.Replace`. R1 names the `PyCompat` project in the `using` rule. R4 and Part 2 have rows for `UnicodeDecodeError` and `DecodeUtf8`. R4 states the limit of `OverflowException` for an integer above 64 bits. | None. No unit is ported. |
| 2026-11-26 | After the pre-pilot scan (plan 0003, results). R24 names the members of modules outside the pilot that a pilot unit ports for real, and R19 points to it. R22 says who removes a passing case from `expected-failures.txt`. Open item O5 is decided (D38), and O1 and O2 name tasks P8 and P7. | None. No unit is ported. |
| 2026-11-26 | Open item O8 is closed: the build enforces the `using` placement of R1. | None. No unit is ported. |
| 2026-11-26 | After the reviews of the `PyCompat` helpers (plan 0003, tasks P2, P4, P5). R7: an enum member carries `[PyEnumMember]`, and there is no `<Enum>Values` class. R8: cleanup after a cancellation passes `CancellationToken.None`, `WaitForAsync` takes the clock, a gather of existing tasks has its own form, and three differences from asyncio are stated. R11: no `strftime`, `strptime`, or subtraction, and `ToUtc()` throws for a naive value. R12: `PyText.Count`. R13: the `PyRegex` constants and the limits of `Split`. R14: `Py.SortInPlace` for float, datetime, and open keys. R18: `math.isnan`. Part 2 lists the members that exist. New open item O9. | None. No unit is ported. |
| 2026-11-26 | After `PyJson`, `PyAst`, `Pydantic`, the judge host codec, and the first pilot unit (`errors`). R1: the licence text is a block comment, and the constructor with `innerException` has its own XML comment. R4: a class with no `__init__` takes `string message = ""`, each class has two constructors, and the row of `NodeLabelValidationError` names the pydantic sites. R7: how a model uses `PydanticFields`, `PydanticSchema`, and `IPydanticModel`, the schema facts that the Python class does not show, and five differences from pydantic. R22: the binding form `InvokeAsync`, the registry, and the `parity_pass` rule for a listed case. Part 2: the members of `Pydantic` and the limits of `PyAst`. Open items O1, O2, and O5 are closed, and O4 has 23 more cases. | `errors`: no. Its code already follows each new rule. |
| 2026-11-26 | With the pilot base. R7: a static method of an enum class goes to `<Enum>Extensions`. R22: each unit has its own bindings file in `Units/`, and it adds its models to the schemas for `judge:schema_text`. `Bindings.cs` has no union merge, because that merge gave code that did not compile. R3 names the three assemblies that the core project grants access to, and R17 and Part 3 give `PortServices.Use` as the seam of a test. | None. No ported unit has an enum or a model. |
| 2026-11-26 | After two reviews of each of three pilot units (`search_filters`, `dedupe_nodes`, `content_chunking`). R1: no private helper that Python lacks. R2: `Versions` has no type, and enum member names. R4: an empty message for a bare `raise`, and checked arithmetic. R5: the `Log` methods are `internal`. R7: a field validator runs inside the read of its field, `model_construct`, `type[BaseModel]`, and the place of the Python methods. R10: one delegate, `PromptFunction`. R11: `indent` is a `long?`, and a C# `int` is cast to `long`. R13: `internal` patterns, `fullmatch`, and `OfType` after a split. R14: a tuple or a frozenset as a set item, and the pair loop of `content_chunking` (D43). R15: `IPromptGroup` on each `<Module>Prompts` class. R17: `PyPatch<T>`. R18: `Py.Comb` returns a `BigInteger`. R22: `AddType` for a result model, one place for each path, the lead file for builtins, and the constructor form. R25: test names, shared pytest files, patched values, and a recording for each unit with logic. New open items O10, O11, and O12. | `search_filters`: yes, for the member order of R7. The same pull request moves `ModelJsonSchema` and the validator below the four members. `dedupe_nodes` and `content_chunking`: their fix rounds apply these rules before their merge. |
| 2026-11-26 | After the adversarial review of the pilot support pull request, and the two reviews of `entity_node_ops`. R2: the driver option keys in `parameters`. R6 and R9: the driver does not accept each value type, so the Neo4j project converts at the boundary, typed lists pass as they are, and `Transaction.RunAsync` returns `Task`. R24: `utc_now`, and the equality and the label validation of `Node`. R1: `global::Neo4j.Driver` in test projects, and when a local reader function is allowed. R4: the switch checks casts. R7: the optional form of `type[BaseModel]`, and the type check of `validate_assignment`. R10: the `PromptVersion` reason. R14: `PySet<T>` has value equality only through `PyEqualityComparer`. R17: `PyPatch<T>` has no implicit conversion, and code reads `.Value`. Part 3: two surrogates side by side. | `content_chunking`: yes, each read of a `PyPatch<T>` gets `.Value` before its merge. `entity_node_ops`: its fix round applies R7. |
| 2026-11-26 | After the fix round of `entity_node_ops`. R3: the Neo4j project grants `InternalsVisibleTo` to `Port.Evaluation`. R7: `PythonName` for a validator on an abstract base model. R25: a recording for each Neo4j operations unit, and how a test compares a pydantic message. | None. `entity_node_ops` follows them. |
| 2026-11-26 | After the first skeleton packet of plan 0004, layer 0. R1: the namespace of a package with one module of its name. R9: `ExecuteWriteAsync` returns `Task`. R16: `GraphDriver.transaction()` takes the body. R17: `os.environ.get` with a default. R20: `_cls`, `EmbedderClient.create`, and `NDArray`. R24: what the skeleton ports in full. | None. |
| 2026-11-26 | After the adversarial review of the first skeleton packet, and after the packets for the node and edge types and for the Neo4j driver. R4: the text of a driver error, and `print`. R7: `PrivateAttr`, and the two forms of a base `@classmethod`. R8: the four-lambda form of `semaphore_gather`. R9: the work function of `ExecuteWriteAsync` receives a `Transaction`, and the conversion members of `Neo4jDriver`. R15: the place of `IQueryTimeoutSession`, and `IsRequired`. R16 and R21: the tracer takes the body of a span, and the new defect U19. 14 more cases in open item O4. | None. No unit uses these shapes yet. |
| 2026-11-27 | After the reviews of skeleton packets S2, S3, and S4. R2: `ExecuteQueryAsync` reads its options from a copy, and rows for a private name that a test reads, the prefix `Private`, the suffixes `Value`, `Function`, and `Group`, `__call__`, `__repr__`, and a type alias. R4: a method call on `None` from open data, and an argument of `super().__init__`. R7: open data as keyword arguments, the copy of a list or a dict value, `model_copy`, `__post_init__`, and `total=False`. R9: the conversion by type in both directions, and facts of the driver package. R15: `PythonName` on `GraphDriver`, `LLMClient`, and the nested group records. R16: `__del__`. R17: `_sleep`. R19: the shape of the reranker seam. R22: `object?` for `PromptRoutes`, and `types:SimpleNamespace`. After the review of skeleton packet S5, R15 and R16: the wrapper of `handle_multiple_group_ids` takes two values and not `self`, and it has three overloads. Open item O12 is closed, and O4 has 11 more cases. | None. No unit of these modules is ported. |
| 2026-11-27 | After two reviews of each layer 1 packet. R6: `Py.Iter` for a loop over an open value. R15: the telemetry of an exception keeps its .NET type name (decision D46). R18: `PyNumpy.AddReduce` for a numpy sum, and a plain loop for a BLAS call. R20: `parse_db_date` returns `object?`, which closes the O4 case `pure.datetime.parse_db_date.wrong_type_int`. R25: a recording holds each module constant, and a test never compares the port with itself. Part 3: an annotated union from open data is `object?`. | None. The fix rounds of the layer 1 packets applied each change. |
| 2026-11-27 | After two reviews of each layer 2 packet (`nodes`, `edges`, the driver contract). R4: `Py.NotNone` at each method call on a record value. R7: the static form of a validator on an abstract base model, the place and the deep form of `ModelCopy`, `model_copy(update=...)` on a `validate_assignment` model, and `load_fact_embedding`. R22: a function that ported code calls with open data takes `object?` (`EpisodeType.from_str`), and a built-in model as a custom type. R25: fakes in a recording of a model module, `Task.Yield` in each fake, the canceled-token theory, and one ordered list of calls and log lines. Part 3: `Py.ZipStrict`, and the flag of `try`/`except`/`else`. | `nodes`, `edges`, and the record parsers: yes. Their fix rounds apply each change before the merge. |
| 2026-11-27 | After two reviews of the prompt packets of layer 4. R2: the suffix `Function` for a delegate property. R6: the text of a `join` error, and a comprehension over an open value. R7: what a built-in response model is, and the value of a reader needs no second copy. R22: the defining unit registers each path of its module, no encoder without a case, and the lead file `Units/Types.cs` for `types:SimpleNamespace`. Part 3: `object?` parameters for a private function that only open data reaches. | The seven prompt units: yes. Their fix round applied each change. |
| 2026-11-27 | After two reviews of `prompts.lib` (layer 4). R15: a group property that returns null, the `isinstance` test of a null entry in a typed mapping, and `callable` for a delegate of another type. R17: one delegate for an instance with `__call__`. R14: `AsReadOnly<K, V>` for a `MappingProxyType`. R15: `isinstance(x, list)` is `Py.TypeName(x) == "list"`, and an attribute of a prompt library that is not a group. | `prompts.lib`, in its fix round. No other unit has these constructs. |
| 2026-11-27 | After two reviews of each layer 3 packet. R1: a `<param>` tag that the compiler forces. R2: the case of a private field. R4: `Py.Int`, an unhashable value, and a base URL with no scheme. R7: a frozen dataclass with `compare=False`. R15: the `PythonNames` table. R16: the retry log, a cancel between attempts, and `sqlite3.connect`. R19: the default `HttpClient`, and the bytes of a reply. R24: `.GetAwaiter().GetResult()`. R25: constructor defaults, and files in the working directory. | The layer 3 units, in their fix rounds: `llm_client.cache`, `llm_client.client`, `llm_client.token_tracker`, `cross_encoder.openai_reranker_client`, and `llm_client.openai_decisions_client`. |
| 2026-11-27 | After packet P3g of layer 3. R2: the prefix `Private` for a private attribute beside a method of the same name. R25: the size limit of a values file and the hash form of a long text. | None. `llm_client.llm_runtime` has the field, and the recorder has the hash form. |
| 2026-11-27 | After two reviews of each layer 5 packet. R2: the other option keys of the driver. R4: the text of a driver error in a log line. R6: the truth test of a typed parameter, and a typed parameter that Python binds again to open data. R21: the marker of an upstream defect at each site. R8: the token checks of the Neo4j driver, and the close of a session. R9: a lone surrogate and an odd offset in a parameter. R25: a slow fake, and the check for a function that does not end. R25: the token theory for a later call. | The layer 5 units, in their fix rounds: `driver.neo4j_driver` and the operations of `driver.neo4j.operations`. The pilot unit `entity_node_ops` gets its U1 markers in the same round. |
| 2026-11-27 | After two reviews of each layer 6 packet. R6: named arguments with a spread, and a result row of FalkorDB. R7: `==` of a record with a list member. R8: a cancel that arrives late. R24: a member that only the Neptune driver has. R25: each fake records its token, a Python fake yields, the fakes count the calls in flight, each async test has a time limit and a loop guard, the turns for the tasks that a failed call leaves, and a replacement on an object that the real function reads first (Part 3). | None. The fix rounds of the layer 6 packets applied each change. |
| 2026-11-27 | After the reviews of skeleton packets S6 and S7. R2: a module that imports a class with its name, two classes with one name, a `**kwargs` dict that a function spreads into one call, a name that ends in `_`, overloads for `A \| list[A]`, and an alias of a union of models. R14: a dict keyed by `id(obj)`. R15: `type(error).__name__` in a stored reason (decision D50). R16: `sqlite3.Row`. R18: a float32 matrix. R22: more typed reads, and an argument for a union record. Part 3: the name and the place of a union record. | None. No unit of these modules is ported. |
| 2026-11-27 | After two reviews of packet P8a of layer 8. R4: `zlib.error` and the errors of `sqlite3`. R6: `==` as an expression on two open values. R8: an `async def` with no `await`. R16: the `execute` forms of `sqlite3`, and `zlib`. R25: the token test of a method with no `await`. | None. |
| 2026-11-27 | After two reviews of each layer 7 packet. R4: `Int128` for int arithmetic past 64 bits. R5: the logger local form and a level word in a placeholder. R8: two more cleanup sites, the token of a work function with a limit, eager arguments of a gather, a nested coroutine, and `max_coroutines or CONSTANT`. R14: the forms for CA1868 and CA1854, `zip` with three inputs, `dict(pairs)`, `pop()`, and two recorder forms. R15: the schema description, `maxLength`, the field title, and a refused field name of a custom type. R17: `_warned_invalid_env`. R18: no `Py.Round`. R19: the write with a timeout (decision D52). R20: a reply value that is not a string. R22: a built-in model with no `ModelFields`. R24: `Py.NotNone` in place of `!`. R25: defaults, items that differ, a cancel that is compared with Python, and a fake that cancels with `Cancel()`. Part 3: the reading of open item O10. O6 is closed. | None. Each unit of layer 7 follows them after its fix round. Four test files of layers 2, 5, and 7 changed to `Cancel()` with the rule. |
| 2026-11-27 | After the fix rounds of packets P8b and P8c of layer 8. R4: `AssertionError` of a fixture. R8: `Asyncio.SleepAsync` for `asyncio.sleep`. R15: the stored name of a cancel. R18: `PyNumpy.ArgSort`, the float32 form of `PyNumpy.AddReduce`, a float32 comparison, and a count in a span. R25: the token of a recording wrapper, module state on the scheduler, and the link of `WaitForAsync`. | `OpenaiDecisionsClient` of layer 3 changed one line to `Asyncio.SleepAsync`. |
| 2026-11-28 | After two reviews of packet P8d of layer 8. R7: a typed construction that shares a list with its caller. R8: `AsyncSemaphore` and `AsyncLock` for `asyncio.Semaphore` and `asyncio.Lock`, the token of a cleanup call in `except BaseException`, and `suppress(asyncio.TimeoutError)`. | `Asyncio.SemaphoreGatherAsync` now uses `AsyncSemaphore`. Seven late cancel points of layers 6 and 7 left the `LateCancel` lists, because they now agree with Python (GAPS F33). |
| 2026-11-28 | After the packets of layer 9 that port `graphiti` and run the flow cases. R9: a `run` call reads the keys of its result, so a failed statement raises where Python raises. `KeysAsync` and `ConsumeAsync` take no token, so a cancel does not stop that wait (GAPS F50, open item O9). R17: three more names, and a default that Python binds at import. R18: a conditional with a `long` branch and a `double` branch. R22: the factory of a model with no `ModelValidate`. R25: the kinds of a late cancel point, a placeholder for a dict key, no order from random uuids, the `extra` fields, the database of a fake driver event, shared state of another module, and the linked token of a write with a limit. | `Neo4jTransaction.RunAsync`, `Neo4jSession.RunAsync`, and one line of `commit_unit` in `Phases.cs`, in the packets P9e and P9b. One late cancel point of the graphiti unit left its list. |
