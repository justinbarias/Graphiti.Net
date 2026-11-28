# Upstream releases

This runbook holds the work of the lead for each new release of upstream Graphiti.
A sync is one move of [UPSTREAM_PIN](UPSTREAM_PIN) to a newer release, with the port work that the move causes.
[The design document](../docs/design-docs/upstream-sync-design.md) gives the reasons and the maintainer's decisions of 2026-11-28.
This file does not say how a Python pattern becomes C#. [RULES.md](RULES.md) holds that.

Status: draft for review. The section "Before the first sync" lists what is not built.

## When a sync starts

All four conditions must hold:

- M1 is published. The pin does not move during plans 0004 and 0005.
- No unit in [queue.json](queue.json) has the status `in_progress`.
- The stale list is empty. Only one sync runs at a time.
- The target is a release tag of the form `vX.Y.Z`. A prerelease tag, an `mcp-v` tag, and a commit with no tag are never a target.

The target is the latest release.
If the release list shows a class D step before the latest release, the target is the first release with a class D step.

## Step 0. Measure the diff

Run the tool from the repository root. It needs `git` and the network.

```bash
python3 -B port/tools/upstream_diff.py --list
```

```bash
python3 -B port/tools/upstream_diff.py v0.32.1
```

The first command prints each release after the pin, with the class of each step and the target of the next sync.
The second command prints the class of each changed file, the units to port again, and the U rows to examine.
The tool keeps the upstream history in `~/.cache/graphiti-net/upstream-history.git`. It never reads or writes `.upstream/`.
It takes each version from the release tags, not from `pyproject.toml`: upstream tagged `v0.31.0` on a commit whose `pyproject.toml` said `0.32.0`.

This is the output for `v0.32.1` on 2026-11-28:

```text
upstream diff: a61333e (0.32.0) to v0.32.1 b22ae9e (0.32.1)
version change: patch
files changed: 3 (A 3, B 0, C 0, D 0)

A  M  pyproject.toml  unit: -  reason: no dependency changed
A  M  signatures/version1/cla.json  unit: -  reason: outside graphiti_core
A  M  uv.lock  unit: -  reason: no locked package changed

units to port again, in queue order: none
U rows to check again: none
diff class: A
path: patch
```

The rules of the tool have their own test. It builds a small repository and must print `self-test: 86 of 86 checks passed`.

```bash
python3 -B port/tools/upstream_diff.py --self-test
```

### Diff classes

The class of a diff is the highest class of its files.

| Class | The diff holds | The tool gives this class to | Work |
| --- | --- | --- | --- |
| A | No code change in a ported module | A file outside `graphiti_core`. A module whose queue note starts with `out-of-mvp:`, `replaced:`, or `not-ported:`. A ported module whose syntax tree did not change. | Step 1 only. |
| B | A code change in a ported module | Each other change in a ported module. | Step 1, then step 2 for each stale unit. |
| C | A change of prompt text or of a model call | A file in `prompts/`. The file `llm_client/client.py`. A call of `generate_response` with a changed argument. A changed line with `response_model`, `prompt_library`, or `model_json_schema`. | The recorded replies can be stale. A new recording costs model calls and needs the maintainer's approval (gate S2). |
| D | An ask-first change | See the next table. | The maintainer decides each item by name before any code (gate S1). |

The tool gives class D to these changes:

| Change | Rule of the tool |
| --- | --- |
| A module was added | A new `.py` file in `graphiti_core`, also in a driver outside the MVP. It needs a queue row and a scope decision. |
| A ported module was removed, renamed, or moved out of the package | The git status `D` or `R` of a module in MVP scope. |
| A public member of the `Graphiti` class | The name or the signature of a public method or of `__init__` changed in `graphiti.py`. |
| A stored field | A class field, its type, or its default changed in `nodes.py` or `edges.py`. |
| A public type | A class, a class field, an enum member, or a constant name changed in `graphiti.py`, `search_config.py`, `search_config_recipes.py`, or `search_filters.py`. |
| Stored queries | A code change in `graph_queries.py`, `node_db_queries.py`, `edge_db_queries.py`, or `migrations/`. |
| An index or a write | A changed line with `CREATE` or `DROP` of an index, a constraint, or a table, or with an index name constant. A changed line with `MERGE`, `SET`, `REMOVE`, or `CREATE (` in capital letters. |
| A dependency | `dependencies` or `requires-python` changed in `pyproject.toml`. |

### What the tool cannot see

The class is a forecast from the text of the diff. Step 1 measures the real class.
If the measured class is higher, it replaces the forecast. Only the maintainer lowers a class.

| Change | What shows it in step 1 | Class |
| --- | --- | --- |
| A class B change gives a different prompt at run time. For example, search rows reach the candidate list of a prompt. | `--show-missing` prints a request with no stored reply. | C |
| A response model outside `prompts/` changes, or a locked package changes the schema text. | A golden `llm` capture changes, and a reply is missing. | C |
| Python code builds a stored property with no `SET` in the changed lines. | A golden `queries` capture or a `graph` dump has a new property, label, or index. | D |
| A module that the model adapter replaces changes its behavior. | Nothing. The judge follows the base path (decision D17). | The lead reads the diff. |
| A type with a public .NET form changes in another module, for example a token usage type or a prompt type. | Nothing, unless a judge case reads the member. | The lead reads the diff against [the public surface design](../docs/design-docs/public-surface-design.md). |
| A query writes with a keyword in small letters. | A golden `queries` capture changes. | D |
| A U row names no file. | The report lists such rows by id. | The lead examines them by hand. |

### A measurement before the pin moves

This run writes no file and does not move the pin. Each id that it prints is a changed case: Python gives a new result for it.
If the report says that the Python environment changed, skip it. The run uses the virtual environment of the old pin.

1. Make an empty folder, for example `/tmp/upstream-target`.
2. Extract the target source into it:

   ```bash
   git --git-dir ~/.cache/graphiti-net/upstream-history.git archive v0.32.1 graphiti_core | tar -x -C /tmp/upstream-target
   ```

3. Run the judge against that folder:

   ```bash
   JUDGE_UPSTREAM_DIR=/tmp/upstream-target python3 port/judge/run.py --build python --list-failures
   ```

4. If another session uses the database, add `--layer pure`. The flow layer needs the database for itself.

## The three paths

The version number sets the path. The measured class can raise it, because upstream is at 0.x and a patch release can change a prompt or a stored property.

| | Patch path | Minor path | Major path |
| --- | --- | --- | --- |
| Taken when | The patch number changed, and the class is A or B. | The minor number changed. Or the patch number changed, and the class is C or D. | The major number changed. Or the maintainer rules that a class D item breaks the public .NET surface or stored data. |
| Plan | None. One row in the sync log. | One execution plan in `docs/exec-plans/active/`. | A new port phase with its own execution plan. |
| Gates | None. Each pull request needs the maintainer's approval, as each change does. | S1 (scope), then S2 (recording). | S1, S2, and S3 (data). |
| U rows | The rows that the tool lists. | All rows. | All rows. |
| Package release | Patch, for class B only | Minor | Major. Minor while the package is at 0.x. |

### Patch path

1. Do step 0. If the class is C or D, take the minor path.
2. Do step 1. If the recording shows a missing reply or a stored-shape change, stop and take the minor path.
3. Do step 2 for each stale unit, in queue order. A class A sync has no stale unit.
4. Examine the U rows that the tool lists.
5. Write the close date into the sync log row.

### Minor path

1. Write the execution plan `docs/exec-plans/active/<number>-sync-<version>.md` from the diff report. [docs/PLANS.md](../docs/PLANS.md) gives the form.
2. List in the plan: the units that changed, were added, or were removed, in queue order. Add each class D item, the U rows, and the upstream tests that changed.
3. Pass gate S1 before any code.
4. Do step 1. Pass gate S2 before the first model call.
5. Write judge cases for new behavior as a patch file. [The judge README](judge/README.md) gives the rule for added cases.
6. Do step 2 for each stale unit and for each new unit, in queue order.
7. Examine all U rows.
8. Run `port/tools/break_tests.py`, `port/tools/upstream-tests.sh`, and `port/tools/infer_type_hints.py`. Record the results in the plan.
9. Run one integrated adversarial review over the full diff of the sync.
10. Close the sync log row, and move the plan to `completed`.

Gate S1, scope. The maintainer decides each class D item by name. The document changes before the code.

| Class D item | Document that changes first |
| --- | --- |
| A new, removed, or renamed module | [queue.json](queue.json) and the scope table of [DECISIONS.md](DECISIONS.md) |
| A dependency | The library table of [DECISIONS.md](DECISIONS.md) |
| A stored field, an index, or a stored query | [db-schema.md](../docs/generated/db-schema.md) and a decision row |
| A public member of the `Graphiti` class | [The public surface design](../docs/design-docs/public-surface-design.md) |
| A Python construct with no rule | [GAPS.md](GAPS.md), then [RULES.md](RULES.md). Only the lead changes the rulebook. |

Gate S2, recording. Before the first model call, the lead tells the maintainer the number of requests with no stored reply and the model.
The maintainer approves the cost and supplies the key.
After the recording, the maintainer reads the summary: the golden rows that changed, the replies that were added and removed, and each `judge: review` line.

### Major path

Do the minor path, with these additions:

- Run the scope phase and the gaps phase of the port again on the diff. Update the scope table, [GAPS.md](GAPS.md), and [TYPE_HINTS.md](TYPE_HINTS.md).
- Compare [the public surface design](../docs/design-docs/public-surface-design.md) with the new upstream class. The maintainer approves the changed design before code.
- Pass gate S3 before the release.

Gate S3, data. The plan answers one question: does data that the old version wrote still load?
The evidence is an end-to-end test. It loads a graph that the old build wrote, and it searches that graph with the new build (GAPS J11).
If upstream ships a migration in `graphiti_core/migrations/`, the plan says how a .NET host runs it.

## Step 1. The pin pull request

Step 1 moves the pin and records the golden files again from the new Python build. It changes no product code.
After step 1, `main` is green: each case that the port now fails is in the stale list.

1. Make the branch `sync/<version>-pin` from `main`.
2. Write the new `commit` and `version` into [UPSTREAM_PIN](UPSTREAM_PIN). If upstream changed `requires-python`, change `python` also.
3. Run `scripts/fetch-upstream.sh`. It deletes `.upstream/graphiti` with its virtual environment.
4. Run `port/tools/upstream-tests.sh` to build the virtual environment again. On the minor path, record its result in the plan.
5. Run `python3 port/judge/run.py --build python --list-failures`. Keep the list: these are the changed cases.
6. If a case fails because upstream removed or renamed its function, write the case change as a patch file. The section "Judge changes in a sync" says where it goes.
7. Run `python3 port/judge/run.py --build python --show-missing`. If a request has no reply, the class is C: pass gate S2, then record the replies as [the judge README](judge/README.md) says.
8. Record: `python3 port/judge/run.py --build python --record`. Read each `judge: review` line.
9. Run `python3 port/judge/run.py --build python` two times. Both runs must pass.
10. Read `git diff --stat port/judge/golden`. A new property, label, or index in a `queries` capture or a `graph` dump is class D: stop and pass gate S1.
11. Build the .NET solution, then run `python3 port/judge/run.py --build dotnet --list-failures`.
12. If the runner prints `passes now. Remove it from expected-failures.txt` for an id, remove that id (decision D38).
13. Write each id that fails and is not in `expected-failures.txt` into `port/judge/stale-after-pin.txt`, with its unit and the target version. These are the stale cases.
14. Set each stale unit to `todo` in [queue.json](queue.json), and update [STATE.md](STATE.md) and the sync log.
15. Run `scripts/check.sh --test`. It must print `check: passed`.

The pin pull request can change these paths, and no other path:
`port/UPSTREAM_PIN`, `port/judge/golden/`, `port/judge/replies/`, `port/judge/stale-after-pin.txt`, `port/queue.json`, `port/STATE.md`, `port/UPSTREAM.md`, and the execution plan.
In `port/judge/expected-failures.txt`, it can only remove an id that passes now. It adds no id there.
The lead never changes `port/judge/cases/`, `port/judge/count.txt`, `port/judge/run.py`, or `port/judge/host-python/` in it.
Its description lists each stale id with its unit, the number of golden rows that changed, and the replies that were added and removed.

If a class A sync changes a golden row, the class is not A. Stop and tell the maintainer.

### Judge changes in a sync

A change to `cases/`, `count.txt`, `run.py`, or `host-python/` is a patch file that the lead writes and the maintainer applies (decision D38).
The place of the patch depends on the pin that it works with:

| The change | Where the maintainer applies it |
| --- | --- |
| It passes the Python build at the old pin, for example a new case for a function that both versions have. | In its own pull request, before the pin pull request. |
| It passes only at the new pin, for example a case for a function that upstream removed or renamed. | On the branch of the pin pull request, before the recording. The pull request names the patch. |
| A new case for new behavior, on the minor path | In its own pull request, after the pin pull request. Its ids enter `expected-failures.txt`, as [the judge README](judge/README.md) says. |

## Step 2. One pull request for each stale unit

1. Take the stale units in queue order.
2. Port the unit again with the normal unit loop: the rulebook, two reviews (decision D12), and the statuses of [queue.json](queue.json).
3. If the unit has a recorder script in `port/tools/`, run it again. Commit the new values with the unit.
4. Remove from `stale-after-pin.txt` each id that passes now. List each removed id in the pull request.
5. Run `scripts/check.sh --test`.

When the stale list is empty and `scripts/check.sh --test` passes on `main`, the sync is closed.

## The stale list

`port/judge/stale-after-pin.txt` holds the cases that a pin move made wrong. `expected-failures.txt` holds the cases that the port did not reach yet, and a sync never adds an id to it.

| Rule | Text |
| --- | --- |
| Line format | `<case id> <unit> <target version>`. A line that starts with `#` is a comment. |
| Who adds an id | The lead, and only in a pin pull request. |
| Who removes an id | The lead, in the pull request that makes the case pass. |
| What the runner allows | A listed case can be bound and give a wrong result. A listed case that passes fails the run until its line is removed. |
| Release | No package is published while the list has an id. |

## Upstream defects

[GAPS.md](GAPS.md) lists the upstream defects that the port reproduces (the U rows). A defect that upstream fixed leaves the port.

1. At each sync, examine each U row that the tool lists. On the minor path and the major path, a reader examines all rows against the new source.
2. If the defect is still there, keep the row. Update its line numbers.
3. If upstream fixed it, move the row to a table `Fixed upstream` in [GAPS.md](GAPS.md), with the release and the commit.
4. The new golden files pin the fixed behavior, and step 2 ports the unit again without the defect.
5. Tell the lead to remove the defect from rule R21 of [RULES.md](RULES.md).
6. If the recording shows a new defect, add a U row with the case that pins it.

Known on 2026-11-28: upstream `main` fixes U4 in commit `c685bab`. No release holds that commit yet.
The measurement of step 0 against that commit gives 18 changed cases of 1,965 pure cases, all for `fulltext_query` and `_build_neo4j_fulltext_query`.

## Package version

Graphiti.Net keeps its own SemVer. Its version is not the upstream version.
Each release names its upstream version in three places: the assembly metadata value `UpstreamVersion`, the package release notes, and a table in the README.

| Sync | Graphiti.Net release |
| --- | --- |
| Class A | No release is needed. The next release names the new upstream version. |
| Patch path, class B | A patch release |
| Minor path | A minor release |
| Major path | A major release. A minor release while the package is at 0.x. |
| A change that only .NET needs | SemVer decides, as for each package. |

## Weekly check

A scheduled CI job runs `python3 -B port/tools/upstream_diff.py --list --exit-code` one time each week.
Exit code 1 means that a release after the pin exists. The job then opens the issue `Upstream release after the pin` with the report, or it replaces the text of the open issue.
Each other exit code above 0 is an error. The job fails and opens no issue.
The job reads the repository and writes issues. It changes no file and starts no sync.

## Before the first sync

These items are decided, and they are not in effect. Each one goes in its own pull request.

| Item | Owner | State |
| --- | --- | --- |
| The decision rows and the new text of invariants 2 and 9 in `AGENTS.md`. [The design document](../docs/design-docs/upstream-sync-design.md) holds the proposed text. | Lead, with the maintainer's approval | Proposed |
| The runner patch for `stale-after-pin.txt`, with the new text of the judge README | Lead writes it, the maintainer applies it | Written: `port/patches/judge-stale-after-pin.patch`. Not applied. |
| The weekly CI job | Lead | Not written. It is the next pull request of this stack. |
| The `UpstreamVersion` metadata and the README table | Plan 0005, the packaging task | Not written |
| The rule R21 text for a fixed defect | Lead | Proposed |

## Sync log

One row for each sync. The lead adds the row in the pin pull request and closes it after the last unit.

| Opened | From | To | Version change | Forecast class | Measured class | Path | Plan | Stale units | U rows that changed | Replies recorded | Closed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | | | | |

No sync ran yet.

Releases that the port did not take yet:

| Release | Date | Class from the pin | Seen on | Note |
| --- | --- | --- | --- | --- |
| `v0.32.1` | 2026-11-24 | A | 2026-11-28 | Only the version number changed. Waits for M1. |
