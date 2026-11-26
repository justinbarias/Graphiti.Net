// tests/llm_client/test_cache.py (port/RULES.md, R25) is not ported yet.
//
// TODO(port): no rule for the SQLite cache. Port.Evaluation has no SQLite package, and LLMCache (Cache.cs) keeps its
// TODO(port) markers. When the lead adds a package, port these 10 tests of class TestLLMCache with the fixture `cache`
// (a cache in a temporary directory, closed after each test):
//   test_get_missing_key_returns_none, test_set_and_get, test_set_overwrites_existing, test_multiple_keys,
//   test_complex_nested_value, test_non_serializable_value_is_skipped, test_corrupted_entry_returns_none,
//   test_creates_directory, test_persistence_across_instances, test_close_and_del.
// test_close_and_del calls __del__ by hand: __del__ has no member (R16), so only the second call of close() stays.
namespace Graphiti.Port.Evaluation;
