# Rulebook design

Status: the maintainer decided D20 to D23 on 2026-11-25. Gate 3 approves the rulebook as a whole.
[port/RULES.md](../../port/RULES.md) is the contract. This page gives the reasons for the rules that are not obvious.

## Requirements

| Requirement | Source |
| --- | --- |
| The .NET build gives the same prompts, queries, and results as Python at the pinned commit. | The judge, 1,999 cases |
| One Python module becomes one C# file with the same names in the same order. | AGENTS.md, invariant 4 |
| An implementer on Sonnet 5.5 can follow the rules with no design choice of its own. | Plan 0004 |
| A mistake that the judge cannot see has no place to hide. | [TESTING.md](../TESTING.md) |
| Nothing ported becomes public before the public surface design. | Decision D4 |

## Decisions

### D20. Wrong-type judge cases

68 pure cases have `wrong_type` in their id and pass an argument of the wrong type, for example `lucene_sanitize(42)`. Python raises `TypeError` or `AttributeError` at run time. The adversarial review found 23 more such cases with other ids.
C# rejects the call at compile time, so the .NET host cannot make it.

| Option | For | Against |
| --- | --- | --- |
| The host reports `JudgeTypeRejected`, and the runner accepts it only for a golden `TypeError` or `AttributeError`. Chosen. | Product signatures stay typed. A real `TypeError` from product code must still match. A case with a normal golden result still fails if the type is too narrow. | One runner change after gate 2. Both Python classes count as one. |
| A list of approved case ids | Each difference is visible by name. | Manual work for 45 cases, and the same runner change. |
| Loose parameter types and guards in product code | No judge change. | Internal signatures lose their types. The guards are code that Python does not have. |

45 of the 68 cases have a golden `TypeError` or `AttributeError`. The other 23 are open item O4 of the rulebook.
`GraphProvider` and `EpisodeType` are plain `Enum` classes, so a case that passes the value text for an enum is a type rejection too.

### D21. The Python value model

Upstream moves open dicts from model replies into models, node attributes, prompt text, and query parameters.
Prompt text needs Python `repr` and `json.dumps` output, and a context can hold a datetime or a tuple.

| Option | For | Against |
| --- | --- | --- |
| `object?` values from a closed set of types, handled only by `PyCompat`. Chosen. | One model from the reply to the prompt and to the query. `Neo4j.Driver` takes the same types as parameters. Lists and dicts share by reference, as in Python. | `object?` gives up compile-time types for open data. The rule keeps it to places where Python says `Any`. |
| `JsonElement` and `JsonNode` with `JsonSerializer` converters | No change to DECISIONS.md. | A JSON node cannot hold a datetime or a tuple. Each boundary needs a conversion. Lax coercion still needs custom converters. |

Models stay records with an explicit `Validate()`. `ModelValidate` is the Python constructor for open data, and it is the one place where lax coercion runs.
Each model writes `ModelValidate`, `ModelDump`, and `ModelJsonSchema` by hand in field order. A shared field table was the other choice.
The hand-written form is longer, but a reviewer can compare it with the Python class line by line, and it needs no reflection.

### D22. The exclusive scheduler

asyncio runs all code on one thread. Upstream appends to shared lists and writes shared dicts from concurrent calls with no lock.
An `await` in .NET resumes on a thread-pool thread, so the same code is a data race.

| Option | For | Against |
| --- | --- | --- |
| One process-wide exclusive `TaskScheduler` for all ported code. Chosen. | The port interleaves only at an `await`, as Python does. No lock is added, so each unit stays equal to its source. An `await` in ported code captures a scheduler that is not the default one. | CPU work runs on one thread at a time, as in Python. `Task.Run`, `Task.WhenAll`, and `ConfigureAwait(false)` are not allowed in ported code. A library does not keep the scheduler for a lambda that it calls. |
| Thread pool, with a lock at each shared structure | More parallel CPU work. | A missed site is a race that the judge cannot see. Each unit differs from its source. |

I/O stays concurrent, because the driver and the HTTP client do their own work on the thread pool. Only the continuation of ported code returns to the scheduler.

The adversarial review measured the weak point on 2026-11-25. A plain `await` and `Task.Delay` return to the scheduler. A retry attempt of Polly 8.6.6 after the first one, and the work function of `Neo4j.Driver` 6.5.1, run on the thread pool.
Upstream runs ported logic inside `session.execute_write`, so that code left the scheduler. Rule R8 now makes each lambda that a library calls start with `Asyncio.OnLoopAsync`, and `GatherAsync` throws when its caller is off the scheduler.
This keeps D22, but the guarantee now depends on a rule at each library boundary. Open item O7 asks the pilot to show that the rule is enough.
A host callback, for example `before_commit_write`, also runs on the scheduler. The public surface design must say so.

### D23. The ambient service holder

Upstream modules are static: a module-level logger, `utc_now()`, and `random`. Ported modules are static classes.

| Option | For | Against |
| --- | --- | --- |
| One internal static holder, `PortServices`, for `ILoggerFactory`, `TimeProvider`, and the random source. Chosen. | Signatures stay equal to Python. A test replaces the clock and the random source in one place. | The values are process-wide. Two `Graphiti` instances in one process share them. |
| An extra parameter on each method that logs or reads the clock | No process-wide state. | Most modules get different signatures, and each call site changes. |

### Lead choices in the rulebook

| Choice | Reason |
| --- | --- |
| Every ported member is `internal`. | Nothing becomes public by accident before plan 0003, task 2. |
| Query and prompt text is a raw string literal with the closing delimiter at column 0, and edge whitespace in ordinary strings. | A closing delimiter at column 0 removes no indent. An editor trims spaces at a line end, so those spaces must be visible in code. |
| A set that code iterates becomes a set with insertion order. | Python gives no stable order, so no order is wrong. Insertion order is one that Python can give, and it is the same in each run. |
| A skeleton member throws `NotSupportedException`. | 71 `catch (NotImplementedException)` sites fall back from a driver operation. Such a site must not hide a member that is not ported. |
| Python `/` is `Py.TrueDiv`. | A float division by zero raises in Python and gives infinity in C#. Four `rrf` cases pin the error. |
| Pydantic's `ValidationError` derives from `ArgumentException`. | In Python it derives from `ValueError`, and the port keeps each hierarchy. |
| `Node` and `Edge` are classes, and every `datetime` is a `PyDateTime` struct. | A record cannot hold the Python `__eq__`: a node equals an edge with the same UUID, and an edge does not equal itself. `DateTimeOffset` cannot hold a naive value, and judge cases keep naive values naive. Both came from the adversarial review. |

## What to look at again

- After the pilot: the hand-written model members of D21. If they repeat a mistake, a field table replaces them.
- After M1: the exclusive scheduler and the ambient holder. Port phase 9 can replace both with injection and with locks where they are needed.
- If the public surface design makes a type public, its exceptions and its callbacks need the rules of D22 and D23 in the user documents.
