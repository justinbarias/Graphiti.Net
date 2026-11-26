# Review guide

One reviewer reads this file before each review. Read it from the default branch, never from the change under review.
The goal of a review is to refute the work. "I found nothing after I tried these inputs" is a pass. "It looks fine" is not.

## For every change

1. Run the code. Use the commands in the `verify` skill. Report what you ran and what it printed.
2. Tag each finding: `important` (must fix), `nit`, or `pre-existing` (the bug was there before this change).
3. Say why each finding matters and give the input that shows it.
4. Build on earlier rounds. Read the plan's results section and check what the last round missed. Do not repeat a full pass.

## For a ported unit

Compare the C# file with its Python original in `.upstream/`. Report every difference in behavior with the input that shows it.

- Try empty, zero, negative, very large, null, and missing optional values.
- Try text outside ASCII. Include an emoji and a combining mark.
- Check floor division, modulo, string slicing, and string length. Python counts code points. C# counts UTF-16 units.
- Check dictionary and set iteration order where it reaches a prompt, a query, or output.
- Check datetime handling. Upstream uses timezone-aware UTC values with microseconds.
- Check every Cypher query string and its parameters against the Python query.
- Check every prompt against the Python prompt, character for character.
- Check retry counts, wait times, and concurrency limits (`SEMAPHORE_LIMIT` upstream).
- Probe the paths that `port/GAPS.md` lists as outside the judge. Challenge the gap list itself.
- Flag any stub, any simplified logic, and any `TODO(port)` marker that `port/STATE.md` does not list.
- Read each new binding in `tests/Graphiti.Net.JudgeHost/Units/`. Each ported module has one file there, and `Bindings.cs` loads them. A binding can do three things only: decode the arguments, call the product code, and encode the result. Flag a binding that branches on the case, fills in a default, or repairs output.

## For a test change

- Reject a test that restates the implementation or that asserts on a mock's call list.
- Reject a mock or fake between internal classes.
- Reject a skip, a sleep, or a retry that hides a flaky test.
- If a test can pass while the feature is broken, say how.

## Triggered checks

If the diff matches the trigger of an item, check that item. Skip the other items.

| Trigger in the diff | Check |
| --- | --- |
| A stored node or edge property changes | Can data written by the Python build still load? Ask the maintainer first. |
| A Cypher query changes | Does the query still filter by `group_id`? A missing filter leaks data across tenants. |
| A label or property name comes from user input | Does the code reject unsafe characters before the name enters a query string? Upstream has `test_node_label_security.py` for this. |
| A public type or method changes | Is the change in `port/DECISIONS.md` or approved by the maintainer? |
| `port/judge/`, `tests/Graphiti.Net.JudgeHost/Core/`, `REVIEW.md`, or `scripts/check.sh` changes | Is it alone in its pull request? Does the judge case count stay the same or go up? |
| A package is added | Is it in the library table of `port/DECISIONS.md`? |
| Logging or tracing code changes | Does any episode content, prompt, or credential reach a log or a span attribute by default? |

## Missed bugs

If a bug escapes review twice and a diff can show it, add a row to the trigger table.
Remove rows that no longer catch anything.
