# Upstream sync design

Status: draft for review. The maintainer answered seven questions on 2026-11-28. No decision row is in `port/DECISIONS.md` yet.
[port/UPSTREAM.md](../../port/UPSTREAM.md) is the runbook. This page gives the reasons, the decisions, and the proposed text for other files.

## Requirements

| Requirement | Source |
| --- | --- |
| The workflow covers a patch, a minor, and a major version change of upstream. | The task |
| `main` stays green during a sync. | The proposal of the lead |
| A session never edits the judge to make a check pass. | AGENTS.md, invariant 2 |
| A change cannot modify the check that judges it. | AGENTS.md, invariant 9 |
| The commands decide that a unit is done. | AGENTS.md, invariant 3 |

## What was measured on 2026-11-28

| Fact | Evidence |
| --- | --- |
| The pinned commit `a61333e` is one commit after the tag `v0.32.0`. That commit changes `mcp_server/uv.lock` only. | `git log v0.32.0..a61333e` |
| One release exists after the pin: `v0.32.1`, 2026-11-24. It changes the version number in `pyproject.toml` and `uv.lock`, and one signature file. | `upstream_diff.py v0.32.1`: class A, 3 files |
| Upstream `main` is 15 commits after `v0.32.1`. One commit, `c685bab`, changes `search/search_utils.py` and `driver/neo4j/operations/search_ops.py`. It fixes defect U4 of GAPS.md. | `upstream_diff.py main`: class B, 2 units, U rows U4, U9, and U15 |
| The pure judge layer against that commit: 18 of 1,965 cases fail, all for `fulltext_query` and `_build_neo4j_fulltext_query`. | `JUDGE_UPSTREAM_DIR=<folder> run.py --build python --layer pure` |
| A test of the tool on the last minor move, `v0.31.0` to `v0.32.0`: class D, minor path, 122 files, 20 units to port again, 24 upstream tests, 15 locked packages. | `upstream_diff.py --base v0.31.0 v0.32.0` |
| The tag `v0.31.0` is on a commit whose `pyproject.toml` says `0.32.0`. The tool thus takes each version from the release tags. | `git show v0.31.0:pyproject.toml` |

## Changes to the proposal of the lead

The lead proposed four points: a runbook in its own file, diff classes A to D from a tool, three paths, and two steps.
All four stay. The repository showed nine places where the proposal needed a change.

| # | Finding | Change |
| --- | --- | --- |
| 1 | The runner fails a case that is bound and gives a wrong result (`run.py`, the `bound_wrong` rule). An id in `expected-failures.txt` does not change that, so an added id does not keep `main` green. | A second list, `stale-after-pin.txt`, with a runner patch. `expected-failures.txt` still only gets shorter. |
| 2 | A session cannot write `port/judge/`. If the Python source changed, the judge README does not let golden files change. The proposal did not say who records. | The lead records in a pin pull request, with a closed list of paths. |
| 3 | The class from the text of a diff can be too low. A class B change can give a different prompt at run time. Python code can build a stored property with no `SET` in the diff. | The class is a forecast. Step 1 measures it, and the measured class replaces a lower forecast. |
| 4 | `JUDGE_UPSTREAM_DIR` points the Python host at another source folder. | Step 0 has a measurement that runs before the pin moves and writes no file. |
| 5 | A change of `uv.lock` can change golden output with no change in `graphiti_core`, for example a new pydantic schema text. | A class A sync also runs the judge against the new Python build. A changed golden row means that the class is not A. |
| 6 | `scripts/fetch-upstream.sh` deletes `.upstream/graphiti` with its virtual environment. | Step 1 builds the environment again with `port/tools/upstream-tests.sh`. |
| 7 | The recorder scripts in `port/tools/` store Python values at the pin. The tests of a stale unit compare with those values. | Step 2 runs the recorder of the unit again. |
| 8 | Upstream tests change with the code. `tests/Port.Evaluation` holds ports of them until M3, and some judge cases were mined from them. | The report counts the upstream tests that changed. |
| 9 | Eleven modules are replaced by the model adapter. A change in one of them has no judge signal (decision D17). | The tool gives class A and tells the lead to read the diff. |

## Decisions of the maintainer

Each question went to the maintainer through `AskUserQuestion` on 2026-11-28.

| # | Question | Answer | Options that were not chosen |
| --- | --- | --- | --- |
| S1 | How does a sync keep `main` green? | The stale list with a runner patch. | A sync branch that merges at the end. Removal of the binding of each stale function. |
| S2 | Who records the golden files after a pin move? | The lead, in a pin pull request with a closed list of paths. | The maintainer records. A patch file of the golden files. |
| S3 | How does the package version follow upstream? | Graphiti.Net keeps its own SemVer and names the upstream version in three places. | The same major and minor number as upstream. The upstream version with a fourth part. |
| S4 | Which release does one sync target? | The latest release. If a release before it has a class D step, the first such release. | One minor version at a time. Always the latest release. |
| S5 | Which U rows does the lead examine at each move? | The rows whose files changed, at each move. All rows at a minor or major move. | All rows at each move. Only the rows whose files changed. |
| S6 | Does a weekly CI job compare the upstream tags with the pin? | Yes. It opens or updates one issue. | A job summary with no issue. No CI job. |
| S7 | When can a pin move start? | Not before M1 is published. | Class A between two layers, and the other classes after M1. Each class at each time. |

Answer S7 is stricter than the recommended option. `v0.32.1` is class A, and it still waits for M1.

## Proposed text

The lead enters this text after the maintainer approves the draft. The lead gives each decision row the next free number.
The ids `S1` to `S7` in this page then become those numbers.

### Rows for `port/DECISIONS.md`

| Id | Decision |
| --- | --- |
| S7 | 2026-11-28. The work for a new upstream release follows [UPSTREAM.md](../../port/UPSTREAM.md). The version number sets the path (patch, minor, or major), and the measured diff class (A to D) can raise it. The pin does not move before M1 is published. |
| S1 | 2026-11-28. A sync keeps `main` green with a second list, `port/judge/stale-after-pin.txt`. A case in it can be bound and give a wrong result. The lead adds an id only in a pin pull request, with its unit and the target version. The lead removes the id in the pull request that makes the case pass, and that pull request lists each id. A sync adds no id to `expected-failures.txt`. No package is published while the list has an id. The runner change is a patch file that the maintainer applies. This amends D38. |
| S2 | 2026-11-28. In a pin pull request, the lead records the golden files and the replies again with `run.py --build python --record`. The lead changes `port/UPSTREAM_PIN`, `port/judge/golden/`, `port/judge/replies/`, `port/judge/stale-after-pin.txt`, and port documents in it, and removes a passing id from `expected-failures.txt`. Each change to `cases/`, `run.py`, `host-python/`, or `count.txt` stays a patch file. If such a patch passes only at the new pin, the maintainer applies it on the branch of the pin pull request. A recording with model calls needs the maintainer's approval before the first call. This amends D11, D38, and the rule for changes after gate 2. |
| S3 | 2026-11-28. Graphiti.Net keeps its own SemVer. Each release names its upstream version in the assembly metadata value `UpstreamVersion`, in the package release notes, and in a table in the README. The release has the size of the sync path: patch, minor, or major. A major path sync gives a minor release while the package is at 0.x. This extends D15. |
| S4 | 2026-11-28. One sync targets the latest upstream release. If a release before it has a class D step, the sync targets the first such release. A prerelease tag and a commit with no tag are never a target. |
| S5 | 2026-11-28. At each sync, the lead examines each U row of GAPS.md whose file changed. At a minor or major move, a reader examines all rows. A defect that upstream fixed leaves the port: its row moves to the table `Fixed upstream`, its unit is ported again, and rule R21 loses it. |
| S6 | 2026-11-28. A weekly CI job runs `port/tools/upstream_diff.py --list`. If a release after the pin exists, the job opens or updates one issue. Only that job has the permission `issues: write`. It changes no file and starts no sync. |

One row for the gate log:

| Gate | Date | Result |
| --- | --- | --- |
| Upstream sync workflow | 2026-11-28 | The maintainer chose S1 to S7 and read the draft of [UPSTREAM.md](../../port/UPSTREAM.md). |

### Invariants 2 and 9 of `AGENTS.md`

The new text of invariant 2:

```text
2. Never edit `port/judge/`, its golden files, or `.upstream/` to make a check pass. If a check looks wrong, stop and tell the maintainer. The lead has two exceptions. The lead removes the id of a case that now passes from `port/judge/expected-failures.txt` or from `port/judge/stale-after-pin.txt` (decisions D38 and S1). In a pin pull request, the lead records the golden files again and adds the stale ids to `port/judge/stale-after-pin.txt` (decision S2, port/UPSTREAM.md).
```

The new text of invariant 9:

```text
9. A change cannot modify the check that judges it. `REVIEW.md`, `scripts/check.sh`, and `port/judge/` change in their own pull requests. One exception: a pin pull request records the golden files again and holds no product code, so no code is judged by the files that it changes (decision S2). The removal of a passing id from `expected-failures.txt` or from `stale-after-pin.txt` goes in the pull request that makes its case pass (decisions D38 and S1).
```

Both invariants keep one line each, so `AGENTS.md` stays under its limit of 100 lines.

### The judge README

This text goes into the runner patch, because sessions cannot edit `port/judge/README.md`.

A new row for the table of files:

```text
| `stale-after-pin.txt` | The .NET cases that a pin move made wrong. Empty outside a sync. Each line is `<case id> <unit> <target version>`. |
```

A new paragraph at the end of "Bindings in the .NET host":

```text
A case in `stale-after-pin.txt` can be bound and give a wrong result, from a pin move until its unit is ported again (port/UPSTREAM.md).
If such a case passes, the runner fails until its line is removed. The runner also fails for an id there that no case has, and for an id that is also in one of the other two lists.
```

A new paragraph at the end of "Rule for changes after gate 2":

```text
A pin move is the one case where golden output changes for an old case. In a pin pull request, the lead records all golden files again from the new pinned source (port/UPSTREAM.md, step 1). A case or `count.txt` changes in that pull request only through a patch that the maintainer applied on its branch.
```

### Rule R21 of `port/RULES.md`

Only the lead changes the rulebook. This sentence goes after the first paragraph of R21:

```text
R21 lists a defect only while the pinned upstream has it. If a sync moves a U row of GAPS.md to `Fixed upstream`, remove the defect from this table and from each rule that names it, in the pull request that ports its unit again (port/UPSTREAM.md).
```

### `.claude/settings.json`

No change is needed. The deny list names each judge file, and it does not name `stale-after-pin.txt`.
The deny rules stop the `Edit` and `Write` tools. The recording writes the golden files through `run.py`, which is a `Bash` command.

## The runner patch

The patch is `port/patches/judge-stale-after-pin.patch`. The maintainer did not apply it.
It changes 39 lines of `run.py` and 9 lines of the judge README. `git apply --check` passes on `main` at `727116a`, also together with `judge-readme-units.patch`.

This is the second version of the patch. Pull request 70 (decision D55) changed the runner one minute before the first version merged, and the first version did not apply after that.
The runner now has three lists: `expected-failures.txt` (empty since gate 4), `accepted-differences.txt` (45 cases), and the stale list.

| Behavior | Expected result |
| --- | --- |
| The runner reads `port/judge/stale-after-pin.txt`. The first word of a line is a case id. A line that starts with `#` is a comment. | A missing file is an empty list. |
| A stale case fails in the .NET build, bound or not. | The run passes. The line `judge: stale after a pin move=<count>` gives the number of listed ids in the run. |
| A stale case passes in the .NET build. | `FAIL <id> passes now. Remove it from stale-after-pin.txt.` |
| An id is also in `expected-failures.txt` or in `accepted-differences.txt`. | `FAIL <id> is in stale-after-pin.txt and in another list. Keep it in one list only.` |
| No case has the id. | `FAIL <id> is in stale-after-pin.txt, and no case has this id.` |
| A stale case answers `JudgeTypeRejected`. | `FAIL <id> is in stale-after-pin.txt and answers JudgeTypeRejected. A port cannot change that answer. The maintainer decides the case by name.` |
| The file is not UTF-8 text. | `FAIL stale-after-pin.txt is not UTF-8 text.` A byte order mark at the start is not an error. |
| The Python build | The runner does not read the list. |
| A run with `--filter` or `--layer` | The runner uses only the listed ids that the run selects. |

Two checks are about the list itself: an id in two lists, and an id that no case has. They read the whole lists, also in a run with `--filter` or `--layer`.
The pass lines of the runner stay as they are. The count of the stale list is on a line of its own.

The runs of 2026-11-28 used a copy of `port/judge/` outside the repository, with the patch applied and the .NET host of `main` at `727116a`.
Each run was `run.py --build dotnet --layer pure`, except where the row says more. The bound case is `pure.text.lucene_sanitize.plain`.

| Run | Result |
| --- | --- |
| No list file, and a list with a comment and blank lines only | `expected failures=0 unexpected=0 fixed=0 bound_wrong=0`, `stale after a pin move=0`, passed |
| Control: a wrong golden row for the bound case, and no list | `FAIL pure.text.lucene_sanitize.plain (not in expected-failures.txt)`, failed, exit code 1 |
| The same wrong golden row, and the case in the list two times, one line with a Windows line end | `unexpected=0`, `stale after a pin move=1`, passed |
| The bound case in the list, with its correct golden row | `FAIL pure.text.lucene_sanitize.plain passes now. Remove it from stale-after-pin.txt.`, failed |
| An id of `accepted-differences.txt` in the list | `FAIL pure.chunking._chunk_json_array.wrong_type_none is in stale-after-pin.txt and in another list. Keep it in one list only.`, failed |
| The bound case in the list and in `expected-failures.txt`, with the wrong golden row | The same message, and `is bound and wrong`, failed |
| The id `pure.no.such.case` in the list | `FAIL pure.no.such.case is in stale-after-pin.txt, and no case has this id.`, failed |
| `--build python --layer pure` with three bad lines in the list | `cases=1965 passed=1965 failed=0`, passed |
| The wrong golden row and the listed case, with `--filter` on another case | `stale after a pin move=0`, passed |
| The same, with `--filter` on the listed case | `stale after a pin move=1`, passed |
| The same, with `--list-failures` | The output holds the listed id. |
| A golden `TypeError` changed to a value for `pure.chunking.estimate_tokens.wrong_type_none`, and no list | `FAIL ... (not in expected-failures.txt)`, failed |
| The same case in the list | `FAIL ... is in stale-after-pin.txt and answers JudgeTypeRejected. ...`, failed |
| The wrong golden row and the listed case, in a file with a byte order mark | `stale after a pin move=1`, passed |
| A list file that is not UTF-8 | `FAIL stale-after-pin.txt is not UTF-8 text.`, failed |

## The weekly job

The job is `.github/workflows/upstream-release.yml`. It goes in its own pull request, because it is CI configuration.
`actionlint` and `shellcheck` report nothing. The two shell steps ran on a local machine on 2026-11-28 with a new clone of upstream.
The first step wrote `newer=1` and `target=v0.32.1`. With a cache folder that cannot be made, the tool ended with exit code 2, and the step failed with no output value.
The issue search ran against the repository and found no open issue. The two `gh` commands that write an issue did not run.

The tool ends with exit code 2 for each error, also for a Python exception. Exit code 1 means only that a release after the pin exists.

| Item | Value |
| --- | --- |
| Trigger | `schedule`, one time each week, and `workflow_dispatch` |
| Steps | Check out the repository. Run `python3 -B port/tools/upstream_diff.py --list --exit-code`, then the report for the target that the list names. |
| Permissions | `contents: read` for the workflow. `issues: write` for this job only. |
| Output | Exit code 1 means that a release after the pin exists. The job then opens the issue `Upstream release after the pin`, or it replaces the text of the open issue. |
| Limits | The job changes no file, opens no pull request, and uses no model key. |

## Checks not run

- The flow layer did not run against a newer upstream source. Another session used the database.
- No sync ran. Steps 1 and 2 of the runbook come from the judge README and the unit loop, not from a run.
- The rules of the tool for class C and class D were tested on the tool's own repository and on one real minor move. No reviewer compared its output with a hand classification.
- One adversarial review ran (the next section). No second review ran on the fixes.
- A second review read the second version of the runner patch (the next section). No review ran on its fixes.
- The runner patch ran on the pure layer only. No run used the flow layer or the full case set.
- The workflow did not run on GitHub. The schedule, the permissions, and the two `gh` commands that write an issue are not tested.

## Adversarial review

One Opus 5.5 subagent at high effort, 2026-11-28, with a brief to refute the tool, the runner patch, the two documents, and the workflow.
It reported 9 important findings and 3 smaller ones. It reproduced 7 of the important findings by a run.

| Finding | State |
| --- | --- |
| I1. If the fetch of a file failed, both sides read as empty, and the file got class A. | Fixed. The tool reads the tree first, and a failed fetch ends the run with exit code 2. |
| I2. Only the new path set the class. A module that moved out of `graphiti_core`, and a path that git writes in quotes, got class A. | Fixed. The tool reads `--name-status -z`, and the old path also decides. |
| I3. The version came from `pyproject.toml`, which differs from the tag for `v0.31.0`. The release list left out `v0.32.0` from that base. | Fixed. The release tags give each version. |
| I4. The report step of the workflow hid a failed tool behind a pipe. | Fixed. Each step runs in `bash` with `pipefail`. |
| I5. A public type outside the methods of `Graphiti` got class B. | Fixed for `graphiti.py` and the three search modules. The runbook lists the other public types as a limit. |
| I6. U6 names a folder, so the tool never listed it. | Fixed. A pattern can be a folder, and the report lists each U row that names no file. |
| I7. The proposed invariant 9 had no exception for the pin pull request, and S2 did not name D38. | Fixed in the proposed text. |
| I8. A case for a function that upstream removed had no pull request to go to. | Fixed. The runbook section "Judge changes in a sync" gives the place of each patch. |
| I9. A listed case that passes after the recording made `main` red. The pin pull request had no way to change `expected-failures.txt`. | Fixed. The pin pull request removes such an id (D38). |
| N1. A changed keyword line of a call that spans lines got class B. | Fixed. The tool compares the syntax tree of each model call. |
| N2. The write rule missed `SET n:Label`, `REMOVE`, `CREATE (`, and index name constants. | Fixed, except a keyword in small letters, which the runbook lists as a limit. |
| N3. Six smaller points. They are the default of a stored field, missing self-test cases, and two gaps in the README text. The last two are the word "stale" for two things and the release row of class A. | Fixed. The self-test has 86 checks. |

### Second review, of the second version of the runner patch

One Opus 5.5 subagent at high effort, 2026-11-28. It reported 2 important findings and 4 smaller ones. It reproduced 3 of the 6 by a run.

| Finding | State |
| --- | --- |
| I1. A case with a golden `TypeError` that becomes a value after a pin move still answers `JudgeTypeRejected`. In the stale list it passed, and no port can remove it. | Fixed. The runner fails for a stale case that answers `JudgeTypeRejected`, and step 15 of the runbook sends the case to the maintainer. |
| I2. Step 12 of the runbook read a message that `--list-failures` does not print. | Fixed. Step 11 runs with no option, and step 14 runs with `--list-failures`. |
| N1. An accepted case that passes by the type error rule after a pin move is not reported. | Fixed in the runbook. Step 13 reads the line `judge: accepted differences=N of M`. |
| N2. A list file that is not UTF-8 stopped the runner with a traceback, and a byte order mark became part of an id. | Fixed. |
| N3. The status board counts a stale case as a case that passes. | Open. The runbook lists it in "Before the first sync". |
| N4. The README gave a line format that the runner does not enforce. | Fixed. The README says that the runner reads the first word only. |
