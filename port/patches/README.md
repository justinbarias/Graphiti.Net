# Patches for the judge

Sessions cannot edit `port/judge/` after gate 2. A change there is a patch file in this folder.
The lead writes the patch on a copy outside `port/judge/`. The maintainer reads it and applies it (decision D38).

| Patch | Purpose | State |
| --- | --- | --- |
| `judge-readme-units.patch` | The judge README names the folder `tests/Graphiti.Net.JudgeHost/Units/` as the place of each binding. One line of text changes, and no code. | Not applied |
| `judge-unordered-params.patch` | A step of a flow case can declare `unordered_params`. The runner then compares a list parameter of a query without its order. Step 5 of `flow.recorded.lifecycle` declares `uuids`, and its golden row is recorded again from Python. Before the change, the Python build failed this case in 3 of 8 runs: `remove_episode` deletes two edges in the order of their random UUIDs. After it, the Python build passed 10 of 10 runs, and each of the 34 flow cases passes. The maintainer chose this form on 2026-11-28. | Not applied |

To apply a patch, run this command on a branch from `main`:

```bash
git apply port/patches/judge-readme-units.patch
git apply port/patches/judge-unordered-params.patch
```

A patch for the judge goes in its own pull request. After the merge, the maintainer or the lead deletes the patch file and its row.
