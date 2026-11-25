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
- A wrong placement binds a name such as `SearchConfig` or `EntityNode` to the public record. The build does not always fail. A constructor call gave `error CS1503`, but `typeof(SearchConfig)` and `value is SearchFilters` compiled and used the public record (measured on 2026-11-25 with the .NET 10 SDK). A directive inside the namespace binds to the ported type.
- In the Neo4j project, write `using global::Neo4j.Driver;`. Inside `namespace Graphiti.Neo4j.<Sub>;` the name `Neo4j` is `Graphiti.Neo4j`, and `using Neo4j.Driver;` fails with `error CS0234`.
- A ported file needs no `using Graphiti;`. The exception classes and `ModelCallFailure` are visible from each `Graphiti.Core` namespace.
- A branch that builds a default object from a module that is not ported, or the Neo4j driver from core code, is not ported. Keep the parameter, and throw in that branch: `throw new ArgumentException("<python parameter name> must be provided"); // PORT: D3, D28`. The four sites are in `Graphiti.__init__`: `Neo4jDriver(uri, user, password)` at `graphiti.py:372`, `OpenAIClient()` at `:385`, `OpenAIEmbedder()` at `:389`, and `OpenAIRerankerClient()` at `:393`. The `uri is None` test before the first one keeps its upstream message. The facade always passes the four objects.
- A public member needs an XML comment, because the build treats a missing one as an error (CS1591). Where Python has no docstring, write `/// <summary>Initializes a new instance of the <see cref="X"/> class.</summary>` for a constructor and `/// <summary>Gets the Python attribute <c>name</c>.</summary>` for a property, with the Python attribute name. This applies to the two error files only.
- Put `// Ported from graphiti_core/<path>.py` on the first line of each file.
- Keep the definitions in the order of the Python file. One Python module is one C# file.
- Copy each docstring into an XML doc comment with the same text.
- Copy each code comment to the same place. A comment that explains a C# difference starts with `// PORT:`.
- Port each branch of a function, also the branches for FalkorDB, Neptune, Kuzu, and Relate. The judge calls query builders with each provider value.
- A module with the note `replaced:`, `out-of-mvp:`, or `not-ported:` in [queue.json](queue.json) is not ported. If your unit imports a name from such a module, stop and tell the lead.

### R2. Names

| Python | C# |
| --- | --- |
| Module-level functions and constants | Members of a static class named after the module: `helpers.py` gives `Helpers` |
| Module name equal to its folder name (`search/search.py`, `driver/driver.py`, `graph_operations/graph_operations.py`, `search_interface/search_interface.py`) | The static class gets the suffix `Module`: `SearchModule` |
| Module name equal to a class in the file, when you ignore case and underscores (`tracer.py` with `Tracer`, `llm_runtime.py` with `LLMRuntime`, `search_filters.py`, `change_set.py`, `delta.py`, `search_config.py`, `neo4j_driver.py`, `openai_reranker_client.py`) | The static class gets the suffix `Module`: `TracerModule`, `LlmRuntimeModule`. A module with no module-level function or constant gets no static class. |
| `snake_case` function | `PascalCase` method. An `async def` gets the suffix `Async`. |
| `snake_case` field, parameter, or local | `PascalCase` property, `camelCase` parameter or local |
| `UPPER_SNAKE` constant | `PascalCase` constant or static readonly field: `DEFAULT_PAGE_LIMIT` gives `DefaultPageLimit` |
| `_private` function or field | The same name without the underscore. `private` if only its own class uses it, `internal` if not. |
| A class | The same name. An exception class follows R4. |
| The class `Graphiti` of `graphiti.py` | `GraphitiCore`. A type named `Graphiti` inside a namespace that starts with `Graphiti` breaks each qualified name (decision D24). The module-level functions of the file go to `GraphitiModule`, by the row above. |
| `Prompt` and `Versions` in each of the 8 prompt modules | `<Module>Prompt` and `<Module>Versions`, for example `DedupeNodesPrompt`. These are the names that `prompts/lib.py` imports them under. |
| A name that is a C# keyword (`eval`, `params`, `event`) | Add the prefix `@` for a parameter or a local. For a member, use the PascalCase name. |
| Keyword arguments with defaults | Optional parameters in the same order with the same defaults |
| `**kwargs` that holds query parameters | `OrderedDictionary<string, object?> parameters` |
| A wire name: a JSON key, a node property, a Cypher parameter, a prompt name, a span attribute | Keep the Python text exactly. Never derive it from a C# name. |

A member keeps the meaning of its Python name. Do not rename a member to make it clearer.

### R3. Visibility and the public surface

- Every ported type and member is `internal`, or `private` by R2. One family is `public`: the exception classes of `errors.py` and `llm_client/errors.py` (R4), with their constructors and properties.
- [The public surface design](../docs/design-docs/public-surface-design.md) names each other public type. Those types are lead code, not ported code. Never add `public` to another ported type.
- The `PyCompat` project grants `InternalsVisibleTo` to the five assemblies in its project file. The core project grants none yet. The lead adds it to the core project for `Graphiti.Net.Neo4j`, `Graphiti.Net.JudgeHost`, `Port.Evaluation`, and `Graphiti.Net.Property.Tests` when each project first needs it. Only the lead changes that list.
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
| `NodeLabelValidationError(GraphitiError, ValueError)` | `NodeLabelValidationException` | `GraphitiException`. C# has one base class. No upstream `except ValueError` receives this class (7 sites, read on 2026-11-25). |
| `ChangeSetRejected` | `ChangeSetRejectedException` | `GraphitiException` |
| `RateLimitError`, `RefusalError`, `EmptyResponseError` (`llm_client/errors.py`) | `RateLimitException`, `RefusalException`, `EmptyResponseException` | `Exception` |
| `OpenAIDecisionsInputTooLongError`, `_OpenAIInputTooLongSignal` | `OpenAIDecisionsInputTooLongException`, `OpenAIInputTooLongSignalException`. Both stay `internal` in the file of their module (decision D32). | `Exception` |

- A constructor takes the same parameters as the Python `__init__`, in the same order, with the same defaults. A class with no `__init__` of its own, for example `GraphitiError`, takes `string message`.
- Each class also has the same constructor with one more last parameter, `Exception? innerException`, for `raise X(...) from e`.
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
| `RuntimeError` | `InvalidOperationException(message)` |
| `ZeroDivisionError` | `DivideByZeroException`. `Py.TrueDiv`, `Py.FloorDiv`, and `Py.Mod` throw it. |
| `OverflowError` | `OverflowException`. The `PyDateTime` helpers throw it for a date outside year 1 to 9999. |
| pydantic `ValidationError` | `Graphiti.PyCompat.ValidationException`, which derives from `ArgumentException` as pydantic's class derives from `ValueError` |
| `json.JSONDecodeError` | `System.Text.Json.JsonException`. `PyJson.Loads` throws it. In Python it is a `ValueError`, so an `except ValueError` around `json.loads` becomes two `catch` clauses. |
| `SyntaxError` from `ast.literal_eval` | `FormatException`. `PyAst.LiteralEval` throws it. |
| `asyncio.CancelledError` | `OperationCanceledException` |
| `asyncio.TimeoutError`, `TimeoutError` | `TimeoutException` |
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
`OverflowException` does not derive from `ArgumentException`, so a `catch` for a Python `ValueError` does not catch it.
Example: `utils/maintenance/attribute_utils.py:67` calls `int(raw)` inside `except ValueError`. For the environment value `99999999999999999999` Python gets an int, and the port throws out of the function.
The maintainer chose on 2026-11-26 to document this limit and not to change the helpers. Add no `catch` for it in ported code.

Do not add an argument guard that Python does not have. Do not replace an exception with a result type.
`Codec.PythonExceptionName` in the judge host maps each C# type back to its Python name. Part 5 lists the rows that it lacks.

### R5. Logging

Upstream has `logger = logging.getLogger(__name__)` in 34 modules and 130 log calls.

- Each module that logs has `private static ILogger Logger => PortServices.Logger(typeof(<ModuleClass>));`. `PortServices` is the ambient holder of decision D23.
- Each log call becomes a source-generated method: a `private static partial` method with `[LoggerMessage]` in a nested `static partial class Log` at the end of the module class.
- Keep the level: `debug` is `Debug`, `info` is `Information`, `warning` is `Warning`, `error` is `Error`. `logger.exception` and `exc_info=True` pass the exception as the first argument after the logger.
- Keep the message text. Each `{expression}` of an f-string and each `%s` becomes a named placeholder: `f'Saved Node to Graph: {node.uuid}'` gives `"Saved Node to Graph: {Uuid}"`.
- Format a list or a dict argument with `Py.Repr` so that the text is the same as Python's.
- A format spec is applied before the call. `%d` and `%s` take the value. `%r` takes `Py.Repr(value)`. `%.30s` and an f-string spec such as `{x:.0f}` take `Py.Format(value, spec)`. The placeholder then holds the finished text.
- `extra={...}` becomes `using (Logger.BeginScope(dictionary))` around the call, with the same keys.
- If an argument needs work to compute, for example a list of UUIDs or a `Py.Repr` call, guard the call with `if (Logger.IsEnabled(LogLevel.<Level>))`.
- Set no event id. Add no log call that Python does not have, and remove none.
- Security: if the level is above `Debug` and an argument can hold episode text, a prompt, a fact, a model reply, or a credential, port the call and name it in the unit's `notes` in the queue. The lead decides before the unit is `reviewed`. See [SECURITY.md](../docs/SECURITY.md).
- Tracing follows the same security rule for span attributes. Keep each span name and attribute key exactly (GAPS J6).

### R6. The Python value model

Decision D21. Upstream passes open data between model replies, node attributes, prompt contexts, and query parameters.
The port keeps such data as `object?` values from one closed set. `Neo4j.Driver` accepts the same set as query parameters.

Use the value model only where the Python annotation is `Any`, `dict[str, Any]`, `list[Any]`, a bare `dict` or `list`, or where [TYPE_HINTS.md](TYPE_HINTS.md) infers such a type.
Typed data stays typed: `dict[str, list[str]]` is `OrderedDictionary<string, List<string>>`.

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
- `{**a, **b}` is a new dictionary with the entries of `a`, then the entries of `b`. A key of `a` keeps its position and takes the value of `b`.
- `d.get(k)`, `d.get(k, default)`, `d.setdefault`, `d.pop`, and `d.update` become the matching `OrderedDictionary` calls. `d[k]` on a missing key throws `KeyNotFoundException`.
- `copy.deepcopy(x)` is `Py.DeepCopy(x)`. `copy.copy(x)` and `dict(x)` are a new collection with the same items.
- A list or a dict is shared by reference, as in Python. Do not copy one unless Python copies it.

### R7. Pydantic models and validators

Decision D21 keeps the library choice: records with an explicit `Validate()`. `JsonSerializer` does not bind models.

| Pydantic | C# |
| --- | --- |
| `class M(BaseModel)` | `internal sealed record M`. A base model with subclasses is an `abstract record`. |
| A model that defines `__eq__` or `__hash__` (`Node`, `Edge`, and their subclasses) | A `class`, not a record. A record cannot declare `Equals(object)`, and its own `Equals` is false for another runtime type. See R18. |
| A field | A property in the same order. `{ get; set; }`, because pydantic models are mutable. `{ get; init; }` only for `frozen=True`. |
| A required field | `required` |
| `Field(default=x)`, `= x` | Property initializer `= x` |
| `Field(default_factory=list)`, `dict`, `lambda: utc_now()`, `lambda: str(uuid4())` | Property initializer: `= []`, `= new()`, `= DatetimeUtils.UtcNow()`, `= Py.Uuid4()` |
| `Field(description=...)` | A row in the model's schema (below). Keep the text exactly. |
| `Field(ge=1)`, `gt`, `le`, `lt`, `max_length`, `min_length` | A test in `Validate()` that throws `ValidationException` |
| `@field_validator`, `@model_validator(mode='after')` | Code in `Validate()`, in the order of the Python file. The three sites are in the table below. |
| `ConfigDict(validate_assignment=True)` | The `set` accessor of each field that has a validator calls the same validation |
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

1. `internal M Validate()`. It runs the constraints and the validators, and returns `this`. Every construction path calls it.
2. `internal static M ModelValidate(OrderedDictionary<string, object?> data)`. It is the Python constructor for open data. It reads each field with a `Pydantic.*` helper, applies defaults, ignores unknown keys, and ends with `.Validate()`. A missing required field, a `None` for a field that is not optional, or a value that lax mode cannot coerce throws `ValidationException`.
3. `internal OrderedDictionary<string, object?> ModelDump(bool json = false)`. Keys are the Python field names in field order. A nested model is dumped the same way. With `json: true`, each value passes through `Pydantic.JsonValue`, which gives what pydantic's JSON mode gives: an enum becomes its value, and a UTC datetime ends in `Z`, not in `+00:00`.
4. `internal static readonly PydanticSchema ModelJsonSchema`. Build it with the `PydanticSchema` builder in field order, with each `description`, `default`, and `title` that Python has. The judge function `judge:schema_text` pins the text for each response model (GAPS G3).

The `Pydantic.*` helpers apply the coercion of pydantic lax mode at the pinned version. Their tests compare them with values that Python recorded. Do not write a coercion by hand.

Validators at the pinned commit:

| Site | Python | C# |
| --- | --- | --- |
| `nodes.py:107` | `@field_validator('labels')` on `Node`, with `validate_assignment=True` | `Validate()` and the `set` accessor of `Labels` call `Helpers.ValidateNodeLabels` inside `Pydantic.Wrap<NodeLabelValidationException>` |
| `search/search_filters.py:75` | `@field_validator('node_labels')` on `SearchFilters` | `Validate()` calls `Helpers.ValidateNodeLabels(NodeLabels)` inside `Pydantic.Wrap<NodeLabelValidationException>` |
| `ingestion/config.py:77` | `@model_validator(mode='after')` on `ConcurrentIngestionConfig` | `Validate()`, after the field constraints, inside `Pydantic.Wrap` |

Pydantic catches a `ValueError` that a validator raises and raises `ValidationError` in its place. `NodeLabelValidationError` is a `ValueError` in Python, so a bad label gives `ValidationError`, and 16 judge cases expect that.
`Pydantic.Wrap(() => ...)` runs the validator code. It catches `ArgumentException` and throws `ValidationException` with the caught exception inside. `Pydantic.Wrap<TException>(() => ...)` also catches `TException`. `PyCompat` cannot name `NodeLabelValidationException`, which is a core type, so the call site names it. A call to `Helpers.ValidateNodeLabels` outside a model still throws `NodeLabelValidationException`.
A record compares a list or a dict property by reference. If upstream compares two models with `==`, stop and tell the lead.

A response model that code passes as a value (`response_model: type[BaseModel]`) is an `IResponseModel`. The lead writes the interface and one class in `src/Graphiti.Net/Internal/`, namespace `Graphiti.Internal`:

```csharp
internal interface IResponseModel
{
    string PythonName { get; }
    PydanticSchema ModelJsonSchema { get; }
}

internal sealed class BuiltinResponseModel(string pythonName, PydanticSchema modelJsonSchema) : IResponseModel
```

- Each built-in response model has one more member after `ModelJsonSchema`: `internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("<Python class name>", ModelJsonSchema);`.
- Pass `M.ResponseModel` where Python passes the class `M`. It is one object, so `a is b` on two response models is `ReferenceEquals(a, b)`, which `prompts/lib.py:355` needs.
- `response_model.model_json_schema()` is `responseModel.ModelJsonSchema`. `response_model.__name__` is `responseModel.PythonName`.
- The interface has no `ModelValidate`. Code that validates a reply names the model: `M(**data)` is `M.ModelValidate(data)`. A custom type validates through its descriptor (R15).

Other shapes:

| Python | C# |
| --- | --- |
| `@dataclass` | `internal sealed record` with `{ get; set; }` properties. `frozen=True` gives `{ get; init; }`. `field(default_factory=...)` is a property initializer. |
| `TypedDict` | `internal sealed record` with the same field names. If the code reads it by a string key, use `OrderedDictionary<string, T>`. |
| `Enum`, `str` enum | A C# `enum` with the members in the same order, and a static class `<Enum>Values` with `ToValue` and `FromValue` as explicit `switch` expressions. Never use `Enum.ToString()` or `Enum.Parse` for a wire value. |
| `Literal['a', 'b']` | `string`, with a test in `Validate()` |

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

| Python | C# |
| --- | --- |
| `async def f(a, b=1)` | `internal static async Task<T> FAsync(A a, long b = 1, CancellationToken cancellationToken = default)` |
| A call to an async function | Pass `cancellationToken`. Analyzer CA2016 fails the build if you do not. |
| `semaphore_gather(*coroutines, max_coroutines=n)` | `Helpers.SemaphoreGatherAsync([() => F1Async(...), () => F2Async(...)], n, cancellationToken)`. Pass a lambda for each call. A C# async method starts when it is called, so a list of tasks ignores the limit. |
| `a, b = await semaphore_gather(f(), g())` with different result types | The overload that takes two or three lambdas and returns a tuple |
| `asyncio.gather(*coroutines)` | `Asyncio.GatherAsync` with lambdas |
| `asyncio.gather(..., return_exceptions=True)` | `Asyncio.GatherAsync(..., returnExceptions: true)`. It never throws, and each result is a value or an exception. |
| `asyncio.create_task(coro)`, `asyncio.ensure_future(coro)` | `Asyncio.CreateTask(token => ..., out var cancel)`. `task.cancel()` is `cancel.Cancel()`. |
| `asyncio.to_thread(f)` | `Asyncio.ToThreadAsync(f)`. It runs `f` on the thread pool and returns to the scheduler. In ported code it is the one allowed way to leave the scheduler. |
| `threading.Lock` | A `System.Threading.Lock` field with the `lock` statement. Keep it: code inside `to_thread` runs off the scheduler. |
| `asyncio.Semaphore(n)` | `SemaphoreSlim(n, n)` with `WaitAsync(cancellationToken)` and `Release()` |
| `with suppress(asyncio.CancelledError): await task` | `try { await task; } catch (OperationCanceledException) { }` |
| `asyncio.wait_for(x, timeout)` | `Asyncio.WaitForAsync(token => ..., timeout, cancellationToken)`. It throws `TimeoutException` and cancels the call. |
| `asyncio.shield(x)` | `Asyncio.ShieldAsync` |
| `asyncio.sleep(s)` | `Task.Delay(TimeSpan.FromSeconds(s), PortServices.Time, cancellationToken)` |
| `asyncio.Lock` | `SemaphoreSlim(1, 1)` with `WaitAsync(cancellationToken)` and `Release()` in `try` and `finally` |
| `asyncio.Event` | `AsyncEvent` |
| `time.time()` | `Py.TimeSeconds()`, a `double` |
| `time.perf_counter()`, `time.monotonic()` | `PortServices.Time.GetTimestamp()` and `GetElapsedTime` |
| A sync function | A sync method. Do not make it async. |

`Helpers.SemaphoreGatherAsync` is the port of `semaphore_gather` and calls `Asyncio.SemaphoreGatherAsync`. The helper keeps these properties of the Python function:

- It starts the calls in input order and returns the results in input order.
- At most `max_coroutines` calls run at one time. A value of `None` or `0` means `Helpers.SemaphoreLimit`.
- It throws the first failure in time. It does not cancel the other calls.

The order of side effects between concurrent calls is not a contract (GAPS G19).

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
- Build the parameter dictionary in the order of the Python dict, with values from the value model (R6). `created_at` stays a `PyDateTime`. The Neo4j project converts it to the driver's type at the last step.
- `session.execute_write(func, *args, **kwargs)` passes the extra arguments to `func`. Write a lambda that closes over them: `session.ExecuteWriteAsync(tx => Asyncio.OnLoopAsync(() => FuncAsync(tx, a, b)))`.
- Keep the call shape: one `execute_query` call is one call, and the operations of one session or transaction stay in order. The judge captures both shapes.
- A constant in `graph_queries.py` or a `*_db_queries.py` file is a `const string` or a `static readonly string` with the same text (GAPS F12).
- Never add an `ORDER BY`, a `group_id` filter, or a missing space. See R21.
- If you change a query for any reason, the reviewer makes sure that it still filters by `group_id` ([REVIEW.md](../REVIEW.md)).

### R10. Prompts

The judge compares each prompt character for character.

| Python | C# |
| --- | --- |
| `def node(context: dict[str, Any]) -> list[Message]` | `internal static List<Message> Node(OrderedDictionary<string, object?> context)` |
| `PromptFunction`, `ChatPromptFunction`, `PromptVersion` | Delegates with the same names |
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
| `json.dumps(x, ...)` | `PyJson.Dumps(x, ensureAscii, indent, sortKeys, @default)` with the options of the call site. Python's defaults are `ensure_ascii=True` and separators `, ` and `: `. | G2, F3 |
| `json.loads(s)` | `PyJson.Loads(s)`. It returns the value model. | G5 |
| `ast.literal_eval(s)` | `PyAst.LiteralEval(s)` | F5 |
| A `datetime` annotation, typed or open | `PyDateTime`, a struct that holds a date and time to the microsecond and an offset that is null for a naive value. Never `DateTime` or `DateTimeOffset` in ported code. A naive value stays naive, and judge cases pin that, for example `pure.models.EntityNode.created_at_naive_kept_naive`. | G4 |
| `dt.isoformat()` | `dt.IsoFormat()`. No fraction if the microseconds are zero. `+00:00`, never `Z`. | G4 |
| `str(dt)`, `f'{dt}'` | `dt.Str()`, and `Py.Str` calls it. A space in place of `T`. | G4, F2 |
| `a < b`, `a == b`, `a - b` on two datetimes | The operators of `PyDateTime`. A naive value compared with an aware value throws `InvalidCastException`, as Python raises `TypeError`. | G4 |
| `datetime.fromisoformat(s)` | `PyDateTime.FromIsoFormat(s)`, with the rules of Python 3.12 | F8 |
| `datetime.now(timezone.utc)`, `utc_now()` | `DatetimeUtils.UtcNow()`, which calls `PyDateTime.UtcNow(PortServices.Time)` | G4 |
| `dt.replace(tzinfo=timezone.utc)`, `dt.astimezone(timezone.utc)`, `dt.tzinfo is None`, `dt.tzinfo != timezone.utc` | `dt.ReplaceUtc()`, `dt.ToUtc()`, `dt.IsNaive`, `!dt.IsUtc` | G4 |
| `dt.strftime(f)`, `datetime.strptime(s, f)` | `dt.StrFTime(f)`, `PyDateTime.StrPTime(s, f)` | G4 |
| `int(s)`, `float(s)` on text | `Py.Int(s)`, `Py.Float(s)` | |

A `PyDateTime` never holds more than 6 fraction digits. A value from `Neo4j.Driver` or from `TimeProvider` becomes a `PyDateTime` at the point where it enters ported code, and is cut to microseconds there.

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
| `s.split(sep)`, `s.strip(chars)`, `s.lstrip(chars)`, `s.rstrip(chars)` | `PyText.Split(s, sep)`, `PyText.Strip(s, chars)`, `PyText.LStrip(s, chars)`, `PyText.RStrip(s, chars)`. Never `string.Split` or `string.Trim`, which work on UTF-16 units. `Trim` with the units of one emoji cuts half of another emoji. `Split` on a lone surrogate cuts an emoji in two. `Split("")` returns the text, and Python raises `ValueError`. All three were measured in both runtimes on 2026-11-26. |
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
A module-level `re.compile` is a `private static readonly Regex`. A pattern inside a function also becomes a static field.

| Python | C# |
| --- | --- |
| `\s` and `\S`, also inside a class such as `[\s]` | `PyRegex.Space` and `PyRegex.NotSpace`. Python's `\s` also matches U+001C to U+001F (GAPS F17). |
| `\w`, `\W`, `\b` | `PyRegex.Word`, `PyRegex.NotWord`, `PyRegex.Boundary` |
| `\d` | `PyRegex.Digit`. .NET's `\d` misses the decimal digits above U+FFFF. Never set `RegexOptions.ECMAScript`. |
| `re.IGNORECASE` | `RegexOptions.IgnoreCase \| RegexOptions.CultureInvariant`, and write three letters out. Python also matches U+017F for `s`, and U+0130 and U+0131 for `i`. .NET does not. Each `s` that the flag covers becomes `[sſ]`, each `i` becomes `[iİı]`, and a class such as `[a-z]` becomes `[a-zſİı]`. The evaluation tests of the unit hold one case for each of the three characters, recorded from Python. |
| `$` with no `re.MULTILINE` | `$`. Both also match before a final line break. Do not change it to `\z` (GAPS F7). |
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

`re.split` and `Regex.Split` differ for empty matches and for groups. Use `PyRegex.Split`, which follows Python.
GAPS.md G16 counts 19 `re` calls with a wider `grep` pattern. The table above names each call site of the 114 MVP modules.

### R14. Order: sorts, dicts, and sets

| Python | C# |
| --- | --- |
| `sorted(xs)`, `sorted(xs, key=f)` | `xs.Order(comparer)`, or `OrderBy` with a key. Both are stable and give a new sequence. For strings the comparer is `Py.StringComparer`. |
| `xs.sort()`, `xs.sort(key=f)` | `Py.SortInPlace(xs, ...)`. Python sorts the list object itself, and a caller can hold the same list. Never assign a new list. |
| A tuple key, or a sort of tuples | `OrderBy(a).ThenBy(b)` in the order of the tuple items |
| `reverse=True` | `OrderByDescending`, and `ThenByDescending` for each further tuple item. It keeps equal items in their first order, as Python does. Never sort and then reverse. |
| `min(xs, key=f)`, `max(xs, key=f)` | `MinBy`, `MaxBy`. Both return the first of equal items, as Python does. If a key can be `nan` or null, write the loop, because .NET orders those in a different way. |
| `List.Sort`, `Array.Sort` | Not allowed for a ported sort. They are not stable. |
| A `dict` that code iterates, dumps, or passes to a prompt or a query | `OrderedDictionary<TKey, TValue>` |
| A `dict` that code reads only by key | `Dictionary<TKey, TValue>` |
| `dict.fromkeys(xs)` to remove duplicates in order | A loop with a `HashSet<T>` that keeps the first of each item in order. Do not rely on `Distinct()` for order. |
| `defaultdict(list)` | `OrderedDictionary` with `Py.SetDefault` |
| `defaultdict(int)` with `d[k] += n` | `d[k] = Py.Get(d, k, 0) + n` on an `OrderedDictionary` |
| `OrderedDict` with `move_to_end(k)` and `popitem(last=False)` (`ingestion/delta.py:152`) | `OrderedDictionary`: remove the key and add it again, and `RemoveAt(0)` |
| `MappingProxyType(d)` | `d.AsReadOnly()`, a live view that cannot be changed |
| `itertools.combinations(xs, k)`, `itertools.zip_longest(a, b)` | `Py.Combinations(xs, k)` in Python's order, `Py.ZipLongest(a, b)` with null for a missing item |
| A `set` or `frozenset` that code tests only with `in`, or compares, or takes the size of | `HashSet<T>`. For strings pass `StringComparer.Ordinal`. |
| A `set` that code passes to `sorted()` | `HashSet<T>`, then the sort rule above |
| A `set` that code iterates with no sort | `PySet<T>`, which iterates in insertion order. See the table below. |

A Python set has no stable order, so each run of upstream can differ at these sites. The port picks insertion order, which is one of the orders that Python can give.

| Site | Python | Port |
| --- | --- | --- |
| `utils/maintenance/node_operations.py:365`, `combined_extraction.py:204` | `list({'Entity', str(entity_type_name)})` | `Entity` first, then the type name if it differs (decision D18, GAPS F13) |
| `driver/neo4j/operations/entity_node_ops.py:51`, `:78`, `utils/bulk_utils.py:216` | `list(set(node.labels + ['Entity']))` | `Entity` first, then the other labels in their first order, each one time |
| `graphiti.py:2563`, `:2565` | `list(set(resolved.labels) \| set(source.labels))` | `Entity` first if present, then the labels of `resolved` in order, then the new labels of `source` in order |
| `utils/maintenance/dedup_helpers.py:359`, `ingestion/delta.py:91` | `for candidate_id in candidate_ids:` with `score > best_score` | `PySet<string>`. Candidates in the order of the bands and of each bucket. The first best score wins. |
| `utils/maintenance/edge_operations.py:469` | `list(referenced_node_uuids)` as a query parameter | `PySet<string>`, in the order of the edges |
| `utils/bulk_utils.py:792` | `UnionFind(all_uuids)` and a dict over the set | `PySet<string>`, in the order of the pairs |
| `utils/content_chunking.py:745`, `:820` | `for pair in uncovered_pairs:` | `PySet` of pairs, built in `(i, j)` order. Only the sampling path reaches the loop (GAPS F9). |
| `utils/maintenance/attribute_utils.py:179`, `:256` | `for field in dropped:` writes keys into `merged` | `PySet<string>`, in the order of `response`. The key order of the merged attributes reaches prompt text. |

If you find another set that code iterates with no sort, and its order can reach a prompt, a query, a result, or a stored value, stop and tell the lead.

### R15. Duck typing and protocols

No `dynamic` and no reflection, except the one site that the table names. Each `getattr` or `hasattr` site has a typed answer.

| Site | Python | C# |
| --- | --- | --- |
| 20 sites in `node_operations.py`, `edge_operations.py`, `bulk_utils.py`, `graph_reader.py`, `phases.py`, `search.py` | `getattr(clients, 'max_coroutines', None)` | `clients.MaxCoroutines`. It is a declared field of `GraphitiClients`. |
| `search/search.py:115` | `getattr(clients, 'tracer', None)` | `clients.Tracer` |
| `search/search.py:73` | `value.value if hasattr(value, 'value') else value` | Two overloads of `EnumValue`: one for `string`, one for an enum that returns its wire value |
| `graphiti.py:527`, `:535`, `:541`, `:727`, `:2029` | `getattr(self, 'concurrent_ingestion', None)` and the like | Nullable fields of the facade class, read directly |
| `decorators.py:53`, `:54`, `:63`, `:95` | `hasattr(self, 'clients')`, `getattr(driver, '_database', None)` | Typed access inside the explicit wrapper of R16: `self.Clients.Driver`, `driver.Database`, `self.MaxCoroutines` |
| `utils/bulk_utils.py:195` | `hasattr(session, 'query_timeout_ms')` | An interface `IQueryTimeoutSession` with the one property. Test with `is`. |
| `utils/bulk_utils.py:283` | Tests whether the interface class overrides an operation | Reflection is allowed at this one site: compare `GetMethod(name).DeclaringType` with `typeof(GraphOperationsInterface)`. Keep the Python operation names in the result. |
| `utils/maintenance/attribute_utils.py:87`, `:88`, `:98` | `getattr(field_info, 'metadata', [])`, `max_length`, `is_required` | `GraphFieldDescriptor.MaxLength` (a `long?`) and `GraphFieldDescriptor.Required` of the custom type descriptor (below) |
| `namespaces/nodes.py:352`, `namespaces/edges.py:352` | `__getattr__` raises `NotImplementedError` for the names of the operations that a driver lacks | One property for each name that Python tests. Its getter throws `NotImplementedException` with the same message if the driver has no such operations. Other names do not exist in C#. |
| `prompts/lib.py:477` to `:618` (11 sites) | `getattr(library, group_name)`, `getattr(group, method_name)`, `hasattr(library, 'specs')` | `IPromptLibrary` and `IPromptGroup` (below) |
| `llm_client/prompt_config.py:96`, `:108`, `:118`, `:292`, `:312` | `getattr(parent, nested_name)`, `getattr(overrides, group_name)`, `fields(overrides)` | A static table from the Python group name to the nested type and to a getter, in field order |

The prompt library is read by name, so it gets two interfaces:

- `IPromptGroup` has `bool TryGetFunction(string name, out ChatPromptFunction? function)`. The names are the Python function names.
- `IPromptLibrary` has `bool TryGetGroup(string name, out IPromptGroup? group)` and `IReadOnlyDictionary<string, PromptSpec>? Specs`. A `null` value of `Specs` stands for a library with no `specs` attribute.
- `getattr` on a missing name throws `MissingMemberException(name)`. `hasattr` is the `TryGet` call.
- A class with `__getattr__` (`_OverrideGroup`, `_ComposedPromptLibrary`, `_LibraryWithBuiltinSpecs`) implements the interface and forwards in `TryGet`.
- The typed members of the Python classes (`library.dedupe_nodes.node`) stay as properties and methods next to the lookup.

A custom entity or edge type is the internal class `Graphiti.Internal.GraphTypeDescriptor` (decision D26). The lead writes it. The public `GraphTypeDefinition` builds it. It implements `IResponseModel` (R7), because upstream passes a custom type as the response model of the attribute prompt.

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
| `isinstance(x, T)` | `x is T`. For the value model, test the runtime type from the table in R6. Test `bool` before `long`, because a Python `bool` is an `int`. |
| `isinstance(x, (A, B))` | `x is A or B` |
| `callable(x)` | `x is not null` for a delegate type |
| `type(x).__name__`, `cls.__name__`, `__qualname__` in a message or a result | A `PythonName` constant on the type with the Python name. Never `GetType().Name`. |

### R16. Decorators, context managers, generators, and caches

| Python | C# |
| --- | --- |
| `@handle_multiple_group_ids` (4 uses) | Port the function body to `<Name>CoreAsync`. The decorated method calls `Decorators.HandleMultipleGroupIdsAsync(this, groupIds, driver, (ids, d) => <Name>CoreAsync(...))`. The wrapper holds the logic of the Python decorator, also the FalkorDB branches and the merge by result type. |
| `@retry(...)` on `_generate_response_with_retry` | A Polly pipeline from `Microsoft.Extensions.Resilience`: 4 attempts in total, the predicate `is_server_or_retry_error`, and the last exception thrown again. The warning log runs after an attempt only if its number is above 1: after attempts 2, 3, and 4, and not after attempt 1. Keep the message text of upstream. Each Polly callback follows the `OnLoopAsync` rule of R8. A `DelayGenerator` computes the wait with the formula of tenacity's `wait_random_exponential(multiplier=10, min=5, max=120)`. Polly's own jitter is a different formula. The pipeline takes its clock from `PortServices.Time` and its random source from `PortServices.Random` (GAPS J3). |
| `@contextmanager` | A method that returns an `IDisposable`. Code before `yield` runs in the method. Code in `finally` runs in `Dispose`. |
| `@asynccontextmanager` | A method that returns an `IAsyncDisposable`, used with `await using` |
| A context manager that reads the exception, for example to roll back a transaction or to set a span status | `try`, `catch`, and `finally` written out at the call site, or a method that takes the body as a lambda. `Dispose` cannot see the exception. |
| `with self.tracer.start_span(name) as span:` | `using (var span = Tracer.StartSpan(name)) { ... }` with a block, so that the span ends where the Python `with` ends. A `set_status('error')` in an `except` stays in a `catch`. |
| `@lru_cache(maxsize=n)` | `PyLruCache<TKey, TValue>` with the same size, in a static field |
| `@property`, `@staticmethod`, `@classmethod` | A property, a static method, a static method. A classmethod that builds an instance is a static factory. |
| `@abstractmethod` | `abstract` |
| A generator with `yield` | `IEnumerable<T>` with `yield return`. An async generator is `IAsyncEnumerable<T>` with `[EnumeratorCancellation]`. |
| `functools.wraps`, `inspect.signature` | Not needed. The explicit wrapper replaces them. |

### R17. Module state and environment

| Python | C# |
| --- | --- |
| A module-level constant | `const` for a number, a `bool`, or a string. `static readonly` for other types. |
| A module-level value read from the environment at import (11 sites in `helpers.py`, `driver/driver.py`, `embedder/client.py`) | `internal static readonly` field with the same name, read one time with `PyEnv` |
| A read inside a function or a constructor (`attribute_utils.py:63` at each call, `openai_decisions_client.py:68` and `:84` in `__init__`) | A `PyEnv` call at the same place. Do not move it to a static field. GAPS.md G10 counts 7 reads with another `grep` pattern. These 14 are each site. |
| `int(os.getenv('X', 20))`, `float(os.getenv('X', 0.15))` | `PyEnv.Int("X", 20)`, `PyEnv.Float("X", 0.15)`. A value that does not parse throws, as Python does at import. |
| `bool(os.getenv('X', False))` | `PyEnv.Truthy("X")`. Python gives `True` for each value that is not empty, also for `"false"`. |
| `os.environ.get('X', 'default')` | `PyEnv.Str("X", "default")` |
| `load_dotenv()` | Not ported. The library reads no `.env` file. |
| A module-level mutable object, for example a search recipe | A `static readonly` field that holds the same single object. Upstream changes one of them in a call (R21, U8). |
| A module-level cache or a "warned once" set | A `static readonly` field. The exclusive scheduler of R8 protects it. |
| Code that runs on import | A static field initializer. Never a static constructor. |
| `Neo4jDriver.__init__` starts an index build as a background task | Not ported. The .NET driver builds indexes only in `BuildIndicesAndConstraintsAsync` (GAPS G20). Write `// PORT: G20` at that place. |
| An optional import in `try` and `except ImportError` | Port the branch that runs when the import works, if the package is in [DECISIONS.md](DECISIONS.md). If not, stop and tell the lead. |

The ambient holder `Graphiti.Internal.PortServices` (decision D23) has four members: `Logger(Type)`, `Time` (a `TimeProvider`), `Random` (a `PyRandom`), and `ClassifyModelFailure(Exception)` (R4, decision D31).
Read the clock, the random source, and loggers only through it. Never use `DateTime.Now`, `DateTimeOffset.UtcNow`, `Stopwatch`, `Random.Shared`, or `new Random()`.

### R18. Numbers, vectors, hashes, random values, and UUIDs

| Python | C# |
| --- | --- |
| `int` | `long`. Use `ulong` for a MinHash value, which goes up to 2^64 - 1 (GAPS F15). Use `int` only for a .NET API that needs it. |
| `a / b` | `Py.TrueDiv(a, b)`. It returns `double` and throws `DivideByZeroException` for a zero divisor, also for floats. |
| `a // b`, `a % b` | `Py.FloorDiv(a, b)`, `Py.Mod(a, b)` |
| `round(x)`, `round(x, n)` | `Py.Round(x)`, `Py.Round(x, n)` |
| `int(x)` on a float | `Py.Int(x)`. It cuts toward zero and throws for `nan` and `inf`. |
| `math.comb`, `math.log2`, `math.ceil`, `math.floor` | `Py.Comb`, `Math.Log2`, `(long)Math.Ceiling(x)`, `(long)Math.Floor(x)` |
| `sum(xs)`, `sum(1 for ...)` | A loop or `Sum()` over the same order. Float sums keep the Python order. |
| A function that returns the integer `0` in one branch and a float in others | `double` (GAPS F18) |
| `numpy` L2 norm, dot product, and array math (25 calls) | `System.Numerics.Tensors.TensorPrimitives` on `double`. `inf` and `nan` inputs give `NaN` in the same places (GAPS F10). If a judge case differs by more than 1e-6, sum in a plain loop in index order (GAPS G17). |
| `np.where(norm == 0, a, a / norm)` | A test of the norm, then the division. Do not divide first. |
| `hash(self.uuid)` in `__hash__` | `Uuid.GetHashCode(StringComparison.Ordinal)` (GAPS G12) |
| `hash(unit.payload)` in `ingestion/delta.py:163` | The SHA-256 of the bytes as a hex string (GAPS G12) |
| `hashlib.md5(b).hexdigest()` | `Convert.ToHexStringLower(MD5.HashData(b))` (GAPS J4) |
| `blake2b(b, digest_size=8)` | `PyHash.Blake2b(b, 8)`. .NET has no BLAKE2b. |
| `int.from_bytes(b, 'big')` | `BinaryPrimitives.ReadUInt64BigEndian(b)` for 8 bytes |
| `zlib.compress`, `zlib.decompress` | `ZLibStream`. If a judge case compares compressed bytes and fails, stop and tell the lead. |
| `random.*` | `PortServices.Random`, a `PyRandom` (GAPS F9). The random path of `generate_covering_chunks` has no judge case. Its evaluation test uses a fixed seed and the property that each pair is covered. |
| `str(uuid4())`, `uuid4().hex` | `Py.Uuid4()`, `Py.Uuid4Hex()`. Lower case, as Python writes them. |
| `str(uuid5(NAMESPACE_URL, name))` (`ingestion/phases.py:330`) | `Py.Uuid5(Py.NamespaceUrl, name)`. .NET has no version 5 UUID. The value is a stored episode UUID, so the helper is tested against Python values. |
| `a == b` where `a` is a node or an edge | `a.Equals(b)`. `Node` and `Edge` are classes (R7) that override `Equals(object)` with the body of the Python `__eq__`, and nothing more. No reference test: `e == e` is `False` for an edge in Python (R21, U12). A node equals an edge with the same UUID, and two judge cases pin that. |
| `x in list`, `list.index(x)`, `list.remove(x)`, `list.count(x)` for nodes or edges | `Py.Contains`, `Py.IndexOf`, `Py.Remove`, `Py.Count`. A Python container tests identity first and `==` second, and the helpers do the same. Never `List.Contains` for these types. |
| A set or a dict with nodes or edges as keys | Pass `PyEqualityComparer<T>.Instance`, which tests identity first |

### R19. Import cycles and assemblies

- C# has no import order, so a cycle inside one assembly is legal (GAPS G11).
- Port each cycle group of [queue.json](queue.json) (`"cycle": true`) in one unit of work. The MVP has two groups: group 10 with 18 modules around the driver contract, and group 85 with `graphiti`, `ingestion.phases`, and `ingestion.scheduler`.
- A lazy import inside a function, used to break a cycle, becomes a normal reference.
- The driver contract of group 10 lives in the core project. It never names a `Neo4j.Driver` type. Upstream core code uses the `neo4j` package in two places.
- `neo4j_time.DateTime` in `helpers.parse_db_date`: the Neo4j project converts a driver date to `PyDateTime` before the value enters the core.
- `httpx.AsyncClient` in `llm_client/openai_decisions_client.py` is an `HttpClient` that the constructor takes, as Python takes `http_client` (decision D32). No other MVP module uses `httpx` for a request. The request `http_client.post(url, headers=..., json=..., timeout=...)` follows these rules:
  - The default client of the constructor is `new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) }`, because the default timeout of `HttpClient` is 100 seconds and upstream's is 120.
  - The body is the UTF-8 bytes of `PyJson.Dumps(body, ensureAscii: false, compact: true, allowNan: false)`. httpx 0.28.1 writes `json=` with `ensure_ascii=False`, the separators `,` and `:`, and `allow_nan=False`.
  - Send the body as a `ByteArrayContent` with `Headers.ContentType = new MediaTypeHeaderValue("application/json")`. `Content-Type` is a content header, and `StringContent` adds a `charset` that Python does not send. `Authorization` is a request header.
  - Wrap the send in `Asyncio.WaitForAsync` with `timeout_seconds`. R4 gives the `catch` clause for `httpx.TimeoutException`.
  - `response.json()` is `PyJson.Loads` of the body text. `response.text` is the body text. `response.status_code` is `(long)response.StatusCode`.
- `cross_encoder/openai_reranker_client.py` stays a ported unit (decision D30). Its `self.client` is the lead seam `Graphiti.Internal.LogProbabilityChat`.
  - The constructor is `OpenAIRerankerClient(LLMConfig? config = null, LogProbabilityChat? client = null)`. The branches that build `AsyncOpenAI` or unwrap an `OpenAIClient` are not ported. A null client throws by the default-object rule of R1.
  - `self.client.chat.completions.create(model=..., messages=..., temperature=0, max_tokens=1, logit_bias={...}, logprobs=True, top_logprobs=2)` is `Client.CreateAsync(model, messages, temperature: 0, maxTokens: 1, logitBias, logprobs: true, topLogprobs: 2, cancellationToken)`. `model` is `Config.Model`, and null where Python falls back to `DEFAULT_MODEL` (the design page lists this difference).
  - The call returns `List<TopLogprob>?`, which stands for `response.choices[0].logprobs.content[0].top_logprobs`. Null stands for the two `is not None` tests of upstream, and the port writes `?? []`. A `TopLogprob` has `Token` and `Logprob`.
  - `np.exp(x)` is `Math.Exp(x)`. `except openai.RateLimitError` follows R4.
- `from neo4j import unit_of_work` in `utils/bulk_utils.py:192`: the core needs a way to ask the driver session for a write with a timeout. That is a new member of the driver contract, which is an ask-first item. The unit is `blocked` until the maintainer decides (open item O6).
- The pilot (plan 0003, task 6) ports `search.search_filters` alone, although it is in group 10. It adds skeleton members by R24 for each name that it uses from the other 17 modules. This is the one exception to the rule above.

### R20. Type hints

- Use the Python annotation. Part 3 gives the type table.
- For a function with no annotation (127 of 1,178), use the signature that [TYPE_HINTS.md](TYPE_HINTS.md) gives (GAPS G22).
- If TYPE_HINTS.md gives `Any` or `Unknown`, read the call sites and choose the real type. If the data is open, use the value model of R6.
- Never change the upstream source to add a hint.

### R21. Upstream defects

The port reproduces upstream at the pinned commit, with its defects. The golden files record them. Never fix one.
Write `// PORT: upstream defect U<n>` at the place of each defect, so that a reviewer does not report it and nobody repairs it.

| Defect | Rule for the port |
| --- | --- |
| U1 | Keep the batched delete inside the transaction. The call fails with the driver's `DatabaseException`. |
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
| F22 | Keep the `expired_at` behavior for a fact that arrives with an end date |
| F23 | Keep the base path of decision D17. The schema text stays at the end of the prompt. |

The four lookup methods that apply no group filter (`node_distance_reranker`, `episode_mentions_reranker`, `get_mentioned_nodes`, `get_communities_by_nodes`) stay as they are.

### R22. Judge bindings and wrong-type cases

- Each ported unit adds its bindings to `tests/Graphiti.Net.JudgeHost/Bindings.cs`. A binding decodes the arguments, calls the product code, and encodes the result. It does nothing else.
- A `{"$ref": "fixtures:Name"}` argument is a custom type. `tests/Graphiti.Net.JudgeHost/Fixtures.cs` holds one `GraphTypeDescriptor` for each of the five classes of `port/judge/host-python/fixtures.py`, built through the public `GraphTypeDefinition`. The lead writes that file.
- A `$model` argument is decoded with `ModelValidate` (R7). A `$enum` argument is decoded with `FromValue`. A `$ref` to a constant or a function is decoded to the ported member.
- A model in a result is encoded as `$type` with the Python class name and the Python field names. Use `ModelDump` if the model has it.
- A function that only changes its arguments returns nothing, so no pure case observes it (GAPS F20). Bind it only if a case names it. The flow layer covers it.
- `driver/neo4j/operations/search_ops.py` is reached only by the direct flow scenarios (GAPS F21). Its unit is not `parity_pass` before those scenarios pass.
- The order of keys in a dict is a contract only where it reaches prompt text or Cypher text (GAPS J15). R14 still applies to each dict that code iterates.

Wrong-type cases, decision D20 (GAPS F11):

- A case passes an argument that the C# parameter type cannot hold, for example `lucene_sanitize(42)`. The binding cannot make the call.
- The host then reports `JudgeTypeRejected`. The runner accepts that result only if the golden result is `TypeError` or `AttributeError`.
- Give each parameter the type that its Python annotation and its call sites give. Never widen a type to `object` to make a wrong-type case pass. Never add a guard for one.
- If Python handles `None` for a parameter (the annotation has `| None`, or the code tests for it), the C# type is nullable and the case runs.
- `GraphProvider` and `EpisodeType` are plain `Enum` classes. A member does not equal its value text, so a case that passes the text for an enum parameter is a type rejection. Never decode such an argument with `FromValue`. Only a `$enum` tag gives an enum value.
- The reviewer found 23 more cases with a golden `TypeError` or `AttributeError` whose id does not hold `wrong_type`. The rule is the same for them.
- Part 5 lists the wrong-type cases that this rule does not settle.

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
- The pilot runs before layer 0. A pilot unit that uses a name from a module that is not ported adds that one member as a skeleton member, in the target file of its module by R1. The lead adds those files to the unit's file list. Layer 0 later completes the files.
- A skeleton member never throws `NotImplementedException`. Ported code catches that type to fall back from a driver operation (71 sites), and such a `catch` must not hide a member that is not ported yet.
- Not allowed in product code: `dynamic`, the null-forgiving `!` operator, `#pragma warning disable`, `[SuppressMessage]`, an empty `catch` block that Python does not have, `async void`, `.Result`, and `.Wait()`.
- A Python `except X: pass` is a `catch` block with the comment `// PORT: Python passes`.
- If the same mistake shows up in two units, the lead changes this file and the affected units are ported again. Nobody patches output by hand.

### R25. Tests

- Port the upstream pytest file that covers a module into `tests/Port.Evaluation`, with the same test names. These tests are temporary (decision D6).
- A pytest file that mocks an internal class is ported as it is. This is the one place where a fake between internal classes is allowed ([TESTING.md](../docs/TESTING.md)).
- Do not drop a test because it is hard to port. Mark it `// TODO(port)` and set the unit to `blocked`.
- Write no new unit test that restates the implementation. A gap in coverage gets a judge case, a property test, or an end-to-end test.
- The table in Part 3 maps pytest constructs to xUnit.

## Part 2. `PyCompat` helpers

`src/Graphiti.Net.PyCompat`, namespace `Graphiti.PyCompat`. Plan 0003, task 3 builds the helpers that the queue needs, in the order that units need them.
Each helper has property tests against values that Python recorded. An implementer calls a helper and never writes its logic again at a call site.
If a unit needs a helper that does not exist, stop and tell the lead.

| Class | Members | Matches Python |
| --- | --- | --- |
| `Py` | `Repr`, `Str`, `Format`, `Truthy`, `Len`, `Get`, `SetDefault`, `Unpack2`, `DeepCopy`, `EnumValue` | `repr`, `str`, format specs, `bool(x)`, `len`, `dict.get`, `dict.setdefault`, tuple unpacking, `copy.deepcopy`, `enum.value` |
| `Py` | `As*` | A typed read from the value model, with Python's error for a wrong type |
| `Py` | `TrueDiv`, `FloorDiv`, `Mod`, `Round`, `Int`, `Float`, `Comb` | `/`, `//`, `%`, `round`, `int()`, `float()`, `math.comb` |
| `Py` | `At`, `Slice`, `CompareStrings`, `StringComparer` | Indexing and slicing by code point with negative indexes, string order by code point |
| `Py` | `Contains`, `IndexOf`, `Remove`, `Count`, `SortInPlace`, `Combinations`, `ZipLongest` | `in`, `list.index`, `list.remove`, `list.count` with the identity test first, `list.sort`, `itertools.combinations`, `itertools.zip_longest` |
| `Py` | `Uuid4`, `Uuid4Hex`, `Uuid5`, `NamespaceUrl`, `TimeSeconds` | `str(uuid4())`, `uuid4().hex`, `str(uuid5(...))`, `uuid.NAMESPACE_URL`, `time.time()` |
| `PyText` | `Lower`, `Upper`, `Title`, `Capitalize`, `IsAlnum`, `IsAlpha`, `IsDigit`, `IsSpace`, `IsUpper`, `IsLower`, `Split`, `RSplit`, `SplitLines`, `Strip`, `LStrip`, `RStrip`, `Replace`, `Find`, `RFind`, `Index`, `CodePoints`, `Ord`, `Chr`, `EncodeUtf8`, `DecodeUtf8`, `DropLoneSurrogates` | The `str` methods with full Unicode case mapping, Python's whitespace set, and positions in code points. A lone surrogate stays as it is. The tables come from the pinned Python build. |
| `PyJson` | `Dumps`, `Loads` | `json.dumps` with `ensure_ascii`, `indent`, `sort_keys`, `default`, the compact separators `,` and `:`, `allow_nan`, Python float text, `NaN` and `Infinity` tokens, and keys that are not strings. `json.loads` into the value model. |
| `PyAst` | `LiteralEval` | `ast.literal_eval` for strings, numbers, `True`, `False`, `None`, lists, tuples, and dicts |
| `PyDateTime` | A struct. `IsNaive`, `IsUtc`, `IsoFormat`, `Str`, `ReplaceUtc`, `ToUtc`, `StrFTime`, the comparison operators, and the static `FromIsoFormat`, `StrPTime`, `UtcNow` | A `datetime` of Python 3.12, aware or naive, to the microsecond |
| `PyRegex` | `Space`, `NotSpace`, `Word`, `NotWord`, `Boundary`, `Digit`, `Split` | The character classes of `re` for `str` patterns, and `re.split` |
| `PyTuple`, `PyDict`, `PySet<T>`, `PyEqualityComparer<T>` | Types | A tuple, a dict with keys of any type in insertion order, a set that iterates in insertion order, a comparer that tests identity first |
| `PyLruCache<TKey, TValue>` | Type | `functools.lru_cache` |
| `PyRandom` | Type | The `random` functions that upstream calls, with a seed for tests |
| `PyEnv` | `Str`, `Int`, `Float`, `Truthy` | `os.getenv` with the conversions of R17 |
| `PyHash` | `Blake2b` | `hashlib.blake2b` with a digest size |
| `Pydantic` | Field readers for each field type, `Wrap`, `Wrap<TException>`, `PydanticField`, `ValidationException`, `PydanticSchema`, `JsonValue`, `DumpJson`, `LoadJson` | Lax-mode coercion, the wrapping of a validator's `ValueError`, `ValidationError`, the text of `json.dumps(model_json_schema())`, and pydantic's JSON mode |
| `Asyncio` | `Run`, `OnLoopAsync`, `GatherAsync`, `SemaphoreGatherAsync`, `CreateTask`, `WaitForAsync`, `ShieldAsync`, `ToThreadAsync`, `CallHostAsync`, `IsCancellation`, `AsyncEvent` | The asyncio calls of R8 on the exclusive scheduler. `CallHostAsync` has no Python form: it is the hop to a host service (decision D33). |

There is no general regex helper. Translate each pattern by hand (R13).

Each helper throws the C# type that R4 gives for the Python error, because ported `catch` clauses depend on it:

| Helper | Throws |
| --- | --- |
| `PyDateTime.FromIsoFormat`, `PyDateTime.StrPTime`, `Py.Int`, `Py.Float` on text that does not parse | `ArgumentException` (`ValueError`) |
| `PyJson.Dumps` for a type that JSON cannot hold, for example a datetime or a set with no `default` | `InvalidCastException` (`TypeError`) |
| `PyJson.Loads` | `JsonException` (`JSONDecodeError`) |
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
| `round(2.5)` is `2` (rounds half to even) | `(int)2.5` cuts off and `2.5.ToString("F0")` gives `3` | Call `Py.Round`. Do not round by a cast or by formatting. |
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
| A string can hold a lone surrogate | So can a `string`, but a high and a low surrogate side by side read as one character | The judge protocol carries lone surrogates. Use `PyText.DropLoneSurrogates` where Python cleans the text. |

### Types

| Python | C# |
| --- | --- |
| `Optional[T]`, `T \| None` | `T?` with nullable reference types on |
| `X \| Y` of different shapes | A sealed abstract record with one derived record for each case |
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
| `try` / `except A as e` / `else` / `finally` | `try` / `catch (A e)` / code after the try guarded by a flag / `finally` |
| `match x:` with `case` | `switch` with the cases in the same order. `case _:` is `default:`. |
| `for ... else`, `while ... else` | A flag that the loop sets when it breaks, and an `if` after the loop |
| List, dict, and set comprehensions | LINQ, ended with `ToList()`, `ToDictionary()`, or `ToHashSet()` so that they run one time. For a dict whose order is read, fill an `OrderedDictionary` in a loop. |
| A generator expression passed to `sum`, `any`, `all`, `min`, `max` | LINQ with no `ToList()` |
| `for i, x in enumerate(xs)` | `foreach (var (i, x) in xs.Index())` |
| `zip(a, b)` | `a.Zip(b)`. Both stop at the shorter input. |
| `zip(a, b, strict=True)` | Compare the counts first and throw `ArgumentException`, then `Zip` |
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
| `monkeypatch`, `mock.patch` | An injected interface with a fake. Use `PortServices.Time` for clocks and a stub `HttpMessageHandler` for HTTP. |
| `pytest.raises(X, match=...)` | `Assert.Throws<X>`, then an assertion on the message |
| `caplog` | A fake `ILogger` set on `PortServices` that collects entries |

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

## Part 5. Open items

These items need the lead or the maintainer. An implementer that meets one stops.

| # | Item | Owner | Needed before |
| --- | --- | --- | --- |
| O1 | The runner does not yet accept `JudgeTypeRejected` (decision D20). The change touches `port/judge/run.py` after gate 2, so it goes in its own pull request. | Maintainer approves the pull request | The pilot (plan 0003, task 6) |
| O2 | `Codec.PythonExceptionName` lacks rows for `ValidationException`, `MissingMemberException`, `DivideByZeroException`, `OverflowException`, `JsonException`, `FormatException`, `InvalidOperationException`, `TimeoutException`, the `Neo4j.Driver` exception types, and each ported exception class of R4. Its default branch returns the C# type name. The row for `ValidationException` and the rows for other types that derive from `ArgumentException` must stand above the `ArgumentException` arm, which matches first today. `tests/Graphiti.Net.JudgeHost/Core/` changes in its own pull request. | Lead | The pilot |
| O3 | Closed on 2026-11-25. [The public surface design](../docs/design-docs/public-surface-design.md) and decisions D24 to D35 answer it: R1, R2, R3, R4, R8, R15, and R19 hold the rules. | | |
| O4 | 23 cases with `wrong_type` in the id have a golden result that is not `TypeError` or `AttributeError`. The table below gives the proposed answer for each group. | Maintainer, by name at gate 4 | Gate 4 |
| O5 | `.claude/settings.json` denies each edit under `port/judge/`, but the runner tells a session to remove a passing case from `port/judge/expected-failures.txt`. | Maintainer | The pilot |
| O6 | `utils/bulk_utils.py:192` calls `neo4j.unit_of_work` from core code to set a transaction timeout. The port needs a new member on the driver session contract for a write with a timeout. That is an interface change, so it is an ask-first item (R19). | Maintainer | Layer 7 of plan 0004 |
| O8 | The rule on `using` directives in R1 depends on the implementer, and a wrong placement does not always fail the build. One `.editorconfig` entry for all C# files that makes `csharp_using_directive_placement = inside_namespace` an error (IDE0065) turns the rule into a build failure. Nobody tested that IDE0065 fails the build for a file-scoped namespace. The existing files of the judge host and the end-to-end tests have their directives at the top and change in the same pull request. | Lead | The pilot |
| O7 | Decision D22 holds only if each lambda that a library calls enters the scheduler again (R8). The rule depends on the implementer and on the test in `GatherAsync`. The pilot must show that this is enough. | Lead, after the pilot | Gate 3 |

Wrong-type cases of O4:

| Cases | Python result | Proposed answer |
| --- | --- | --- |
| `pure.dedupe.name_entropy.wrong_type_none`, `pure.text.get_default_group_id.wrong_type_none`, `pure.chunking._chunk_json_array.wrong_type_none`, `pure.chunking.should_chunk.wrong_type_episode_none` | A normal result for `None` | Make the parameter nullable if the Python code handles `None` by its own logic. If it works only by accident, the case stays open. |
| `pure.queries.get_range_indices.wrong_type`, `pure.queries.get_entity_edge_save_query.wrong_type`, `pure.text.get_default_group_id.wrong_type_str`, `pure.chunking._estimate_high_density.wrong_type_episode_str`, `pure.chunking.should_chunk.wrong_type_episode_str` | A normal result for text in place of an enum member. The enums are plain `Enum` classes, so the text equals no member and the default branch runs. For `get_default_group_id('falkordb')` Python gives `''`, and the member gives `'_'`. | The C# type cannot hold the value. Open: the maintainer accepts each difference by name, or extends decision D20. |
| `pure.filters.search_filters.construct_wrong_type_labels`, `pure.dedupe.ConcurrentIngestionConfig.state_store_wrong_type` | `ValidationError` | `ModelValidate` throws `ValidationException`. The case runs. |
| `pure.text.validate_group_id.wrong_type_zero`, `.wrong_type_false`, `pure.text.validate_group_ids.wrong_type_str`, `.wrong_type_str_invalid`, `pure.text.validate_node_labels.wrong_type_str`, `pure.text.validate_excluded_entity_types.wrong_type_str`, `pure.datetime.parse_db_date.wrong_type_int`, `pure.chunking.chunk_json_content.wrong_type_size_str` | A normal result or a domain error for a value of the wrong type | The C# type cannot hold the value. Open: the maintainer accepts each difference by name, or extends decision D20. |
| `pure.text.normalize_l2.wrong_type_str_elems`, `.wrong_type_scalar`, `pure.text._build_directed_uuid_map.wrong_type_short_pair`, `pure.text.compress_uuid_map.wrong_type_short_pair` | `ValueError`, `AxisError`, `IndexError` | The C# type cannot hold the value. Open, as above. |

## Rule changes

| Date | Change | Units to port again |
| --- | --- | --- |
| 2026-11-25 | First version, from the skill's `references/rules.md` and GAPS.md. Decisions D20 to D23. | None. No unit is ported. |
| 2026-11-25 | After the adversarial review (plan 0003, results of task 1): `OnLoopAsync` for library callbacks, `Node` and `Edge` as classes with identity-first container helpers, `Pydantic.Wrap` for validators, the `PyDateTime` struct for each `datetime`, lone surrogates, Python escapes and inner trailing spaces in string literals, `re.IGNORECASE` and `\d`, one more set site, the retry log condition, the `except Exception` filter, call-time environment reads, and rules for 14 constructs that had none. Plain enums are not decoded from their value text. New open items O6 and O7. | None. No unit is ported. |
| 2026-11-25 | After the public surface design (plan 0003, task 2, decisions D24 to D35): ported code moves to `src/Graphiti.Net/Core/` and the namespace `Graphiti.Core`, the class `Graphiti` is `GraphitiCore`, the exception classes of the two error files are public in `Graphiti`, four provider error rows, `Asyncio.CallHostAsync` for host services, the custom type descriptor and `IResponseModel`, `PortServices.ClassifyModelFailure`, and `HttpClient` for the decisions client. Open item O3 is closed. | None. No unit is ported. |
| 2026-11-25 | After the adversarial review of the public surface design (plan 0003, results of task 2): the `using` placement covers each project and `global::Neo4j.Driver`, the default-object branches of `Graphiti.__init__`, XML comments on public exception members, the inner-exception constructor, the timeout and transport rows of R4, the server error wrap of the chat adapter, `Pydantic.Wrap<TException>`, `ModelFields` as an ordered dictionary, the exact `IResponseModel`, `Asyncio.Run` returns a task, the HTTP rules of the decisions client, the reranker unit on the seam `LogProbabilityChat`, and judge fixtures as descriptors. | None. No unit is ported. |
| 2026-11-26 | After the integrated review of the pull request stack. R12 sends `split(sep)` and `strip(chars)` to the `PyText` members. R12 has two rows for `replace`, and Part 2 lists `PyText.Replace`. R1 names the `PyCompat` project in the `using` rule. R4 and Part 2 have rows for `UnicodeDecodeError` and `DecodeUtf8`. R4 states the limit of `OverflowException` for an integer above 64 bits. | None. No unit is ported. |
