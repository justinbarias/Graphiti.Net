# Patches for the judge

Sessions cannot edit `port/judge/` after gate 2. A change there is a patch file in this folder.
The lead writes the patch on a copy outside `port/judge/`. The maintainer reads it and applies it (decision D38).

| Patch | Purpose | State |
| --- | --- | --- |
| `judge-readme-units.patch` | The judge README names the folder `tests/Graphiti.Net.JudgeHost/Units/` as the place of each binding. One line of text changes, and no code. | Not applied |

To apply a patch, run this command on a branch from `main`:

```bash
git apply port/patches/judge-readme-units.patch
```

A patch for the judge goes in its own pull request. After the merge, the maintainer or the lead deletes the patch file and its row.
