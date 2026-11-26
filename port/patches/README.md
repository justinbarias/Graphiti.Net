# Patches for the judge

Sessions cannot edit `port/judge/` after gate 2. A change there is a patch file in this folder.
The lead writes the patch on a copy outside `port/judge/`. The maintainer reads it and applies it (decision D38).

| Patch | Purpose | State |
| --- | --- | --- |
| `judge-type-rejected.patch` | The runner accepts `JudgeTypeRejected` (decisions D20 and D40). Plan 0003, task P8. | Not applied |

To apply a patch, run this command on a branch from `main`:

```bash
git apply port/patches/judge-type-rejected.patch
```

A patch for the judge goes in its own pull request. After the merge, the maintainer or the lead deletes the patch file and its row.
