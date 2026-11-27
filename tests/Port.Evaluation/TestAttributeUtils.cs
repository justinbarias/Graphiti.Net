// Ported from tests/utils/maintenance/test_attribute_utils.py (port/RULES.md, R25).
// A pydantic model class of the test is the custom type descriptor that the port receives (R15). caplog is a capture of
// the logger factory, and monkeypatch.setenv is PyEnv.Use.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class TestAttributeUtils
{
    // class _Person(BaseModel): phones, industry, description (max_length=2000), aliases
    private static readonly GraphTypeDescriptor Person = new(
        "_Person",
        null,
        [
            new GraphFieldDescriptor("phones", GraphFieldKind.String, false, "Phone numbers, comma-separated, with type tag where known", false, null),
            new GraphFieldDescriptor("industry", GraphFieldKind.String, false, "Industry sector", false, null),
            new GraphFieldDescriptor("description", GraphFieldKind.String, false, "Long-form description", false, 2000),
            new GraphFieldDescriptor("aliases", GraphFieldKind.String, true, "Alternate names", false, null),
        ]);

    private static readonly long DefaultAttributeMaxLength = AttributeUtils.DefaultAttributeMaxLength;

    private static readonly long ListTotalLengthMultiplier = AttributeUtils.ListTotalLengthMultiplier;

    [Fact]
    public void test_short_string_passes_through()
    {
        var response = Dict(("phones", "415-555-0142"), ("industry", "SaaS"));
        var (kept, dropped) = AttributeUtils.CapStringAttributes(response, Person);
        Assert.Equal(PyJson.Dumps(response), PyJson.Dumps(kept));
        Assert.Empty(dropped);
    }

    [Fact]
    public void test_overlong_string_dropped()
    {
        var rant = new string('x', (int)(DefaultAttributeMaxLength * 10));
        var response = Dict(("phones", rant), ("industry", "SaaS"));
        var (kept, dropped) = AttributeUtils.CapStringAttributes(response, Person);
        Assert.False(kept.ContainsKey("phones"), "over-cap field must be dropped, not truncated");
        Assert.Equal("SaaS", kept["industry"]);
        Assert.Equal(["phones"], dropped);
    }

    [Fact]
    public void test_explicit_max_length_honored()
    {
        var longDesc = new string('a', 1500);
        var (kept, dropped) = AttributeUtils.CapStringAttributes(Dict(("description", longDesc)), Person);
        Assert.Equal(longDesc, kept["description"]);
        Assert.Empty(dropped);
    }

    [Fact]
    public void test_explicit_max_length_enforced_when_exceeded()
    {
        var (kept, dropped) = AttributeUtils.CapStringAttributes(Dict(("description", new string('a', 2500))), Person);
        Assert.False(kept.ContainsKey("description"));
        Assert.Equal(["description"], dropped);
    }

    [Fact]
    public void test_non_string_fields_pass_through()
    {
        // class Mixed(BaseModel): amount: float | None = None; notes: str | None = None
        var mixed = new GraphTypeDescriptor(
            "Mixed",
            null,
            [
                new GraphFieldDescriptor("amount", GraphFieldKind.Number, false, null, false, null),
                new GraphFieldDescriptor("notes", GraphFieldKind.String, false, null, false, null),
            ]);

        var response = Dict(("amount", 42.5), ("notes", "short note"));
        var (kept, dropped) = AttributeUtils.CapStringAttributes(response, mixed);
        Assert.Equal(PyJson.Dumps(response), PyJson.Dumps(kept));
        Assert.Empty(dropped);
    }

    [Fact]
    public void test_null_field_passes_through_cap()
    {
        var (kept, dropped) = AttributeUtils.CapStringAttributes(Dict(("industry", null)), Person);
        Assert.Equal(PyJson.Dumps(Dict(("industry", null))), PyJson.Dumps(kept));
        Assert.Empty(dropped);
    }

    [Fact]
    public void test_list_with_one_overlong_element_drops_field()
    {
        var bleed = new string('x', (int)(DefaultAttributeMaxLength + 1));
        var response = Dict(("aliases", new List<object?> { "Sammy", bleed, "Sam R." }));
        var (kept, dropped) = AttributeUtils.CapStringAttributes(response, Person);
        Assert.False(kept.ContainsKey("aliases"));
        Assert.Equal(["aliases"], dropped);
    }

    /// <summary>Many just-under-cap elements collectively exceed max_len * multiplier.</summary>
    [Fact]
    public void test_list_aggregate_length_caught()
    {
        var item = new string('x', (int)(DefaultAttributeMaxLength - 1));
        var response = Dict(("aliases", Enumerable.Repeat((object?)item, (int)(ListTotalLengthMultiplier + 2)).ToList()));
        var (kept, dropped) = AttributeUtils.CapStringAttributes(response, Person);
        Assert.False(kept.ContainsKey("aliases"));
        Assert.Equal(["aliases"], dropped);
    }

    [Fact]
    public void test_list_short_passes_through()
    {
        var response = Dict(("aliases", new List<object?> { "Sammy", "Sam R.", "S. Rivera" }));
        var (kept, dropped) = AttributeUtils.CapStringAttributes(response, Person);
        Assert.Equal(PyJson.Dumps(response), PyJson.Dumps(kept));
        Assert.Empty(dropped);
    }

    /// <summary>
    /// A required string field with an over-cap value cannot be dropped without
    /// failing Pydantic validation; the helper retains the bleed and logs louder.
    /// </summary>
    [Fact]
    public void test_required_field_overcap_kept_with_warning()
    {
        // class StrictPerson(BaseModel): full_name: str = Field(..., description='Full legal name')
        var strictPerson = new GraphTypeDescriptor(
            "StrictPerson",
            null,
            [new GraphFieldDescriptor("full_name", GraphFieldKind.String, false, "Full legal name", true, null)]);

        var bleed = new string('x', (int)(DefaultAttributeMaxLength + 50));
        var caplog = new MaintenanceLogCapture();
        OrderedDictionary<string, object?> kept;
        PySet<string> dropped;
        using (PortServices.Use(loggerFactory: caplog))
        {
            (kept, dropped) = AttributeUtils.CapStringAttributes(
                Dict(("full_name", bleed)),
                strictPerson,
                promptName: "extract_nodes.extract_attributes",
                entityUuid: "abc-123",
                groupId: "tenant-x");
        }

        // Bleed is retained, NOT dropped — Pydantic validation would otherwise fail.
        Assert.Equal(PyJson.Dumps(Dict(("full_name", bleed))), PyJson.Dumps(kept));
        Assert.Empty(dropped);
        var matching = caplog.Lines.Where(rec => rec.Message.Contains("attribute_length_cap_skipped_required", StringComparison.Ordinal)).ToList();
        Assert.True(matching.Count > 0, "expected a warning about the required-field carve-out");
        Assert.All(matching, rec => Assert.Equal("WARNING", rec.Level));
    }

    [Fact]
    public void test_logs_info_on_drop_with_uuid_and_group_id_no_pii()
    {
        var response = Dict(("phones", new string('x', (int)(DefaultAttributeMaxLength + 1))));
        var caplog = new MaintenanceLogCapture();
        using (PortServices.Use(loggerFactory: caplog))
        {
            AttributeUtils.CapStringAttributes(
                response,
                Person,
                promptName: "extract_nodes.extract_attributes",
                entityUuid: "abc-123-uuid",
                groupId: "tenant-x");
        }

        var matching = caplog.Lines.Where(rec => rec.Message.Contains("attribute_length_cap_exceeded", StringComparison.Ordinal)).ToList();
        Assert.True(matching.Count > 0, "expected an info-level log on cap drop");
        Assert.All(matching, rec => Assert.Equal("INFO", rec.Level));
        Assert.Contains(
            matching,
            rec => rec.Message.Contains("phones", StringComparison.Ordinal)
                && rec.Message.Contains("abc-123-uuid", StringComparison.Ordinal)
                && rec.Message.Contains("tenant-x", StringComparison.Ordinal)
                && rec.Message.Contains("reason=per_item", StringComparison.Ordinal));
        Assert.DoesNotContain(matching, rec => rec.Message.Contains("Sam Rivera", StringComparison.Ordinal));
    }

    /// <summary>
    /// When list-aggregate fires, length= must be the total (not max element)
    /// and cap= must be the aggregate cap, so logs show the breach
    /// directly instead of the misleading length=under-cap cap=per-item view.
    /// </summary>
    [Fact]
    public void test_log_aggregate_trigger_reports_aggregate_length_and_cap()
    {
        var item = new string('x', (int)(DefaultAttributeMaxLength - 1));
        var items = Enumerable.Repeat((object?)item, (int)(ListTotalLengthMultiplier + 2)).ToList();
        var expectedTotal = items.Sum(i => ((string)i!).Length);
        var expectedCap = DefaultAttributeMaxLength * ListTotalLengthMultiplier;
        var caplog = new MaintenanceLogCapture();
        using (PortServices.Use(loggerFactory: caplog))
        {
            AttributeUtils.CapStringAttributes(
                Dict(("aliases", items)),
                Person,
                promptName: "extract_nodes.extract_attributes",
                entityUuid: "node-uuid",
                groupId: "tenant-x");
        }

        var matching = caplog.Lines.Where(rec => rec.Message.Contains("attribute_length_cap_exceeded", StringComparison.Ordinal)).ToList();
        Assert.Single(matching);
        var msg = matching[0].Message;
        Assert.Contains("reason=aggregate", msg, StringComparison.Ordinal);
        Assert.Contains($"length={expectedTotal}", msg, StringComparison.Ordinal);
        Assert.Contains($"cap={expectedCap}", msg, StringComparison.Ordinal);

        // Sanity: aggregate length must exceed the aggregate cap (otherwise the test
        // is wrong, not the helper).
        Assert.True(expectedTotal > expectedCap);
    }

    [Fact]
    public void test_env_var_overrides_default()
    {
        using (SetEnv("50"))
        {
            var longValue = new string('x', 100);
            var (kept, dropped) = AttributeUtils.CapStringAttributes(Dict(("phones", longValue)), Person);
            Assert.False(kept.ContainsKey("phones"));
            Assert.Equal(["phones"], dropped);
        }
    }

    [Fact]
    public void test_env_var_invalid_falls_back_to_default()
    {
        using (SetEnv("not-a-number"))
        {
            var shortValue = new string('x', 100);
            var caplog = new MaintenanceLogCapture();
            OrderedDictionary<string, object?> kept;
            PySet<string> dropped;
            using (PortServices.Use(loggerFactory: caplog))
            {
                (kept, dropped) = AttributeUtils.CapStringAttributes(Dict(("phones", shortValue)), Person);
            }

            Assert.Equal(PyJson.Dumps(Dict(("phones", shortValue))), PyJson.Dumps(kept));
            Assert.Empty(dropped);
        }
    }

    [Fact]
    public void test_env_var_explicit_max_length_still_wins()
    {
        using (SetEnv("50"))
        {
            var longDesc = new string('a', 1500);
            var (kept, dropped) = AttributeUtils.CapStringAttributes(Dict(("description", longDesc)), Person);
            Assert.Equal(PyJson.Dumps(Dict(("description", longDesc))), PyJson.Dumps(kept));
            Assert.Empty(dropped);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("   ")]
    [InlineData("")]
    public void test_env_var_non_positive_or_blank_falls_back(string badValue)
    {
        using (SetEnv(badValue))
        {
            var (kept, dropped) = AttributeUtils.CapStringAttributes(Dict(("phones", new string('x', 100))), Person);
            Assert.Equal(PyJson.Dumps(Dict(("phones", new string('x', 100)))), PyJson.Dumps(kept));
            Assert.Empty(dropped);
        }
    }

    [Fact]
    public void test_invalid_env_warning_dedups_across_calls()
    {
        // PORT: Python replaces _warned_invalid_env with an empty set (monkeypatch.setattr). The name has no PyPatch<T>
        // (R17), so the test uses a value that no other test of the process passes: the set holds it after the first call.
        using (SetEnv("garbage"))
        {
            var caplog = new MaintenanceLogCapture();
            using (PortServices.Use(loggerFactory: caplog))
            {
                for (var i = 0; i < 5; i++)
                {
                    AttributeUtils.CapStringAttributes(Dict(("phones", new string('x', 50))), Person);
                }
            }

            var invalidWarnings = caplog.Lines
                .Where(rec => rec.Level == "WARNING" && rec.Message.Contains("GRAPHITI_ATTRIBUTE_MAX_LENGTH", StringComparison.Ordinal))
                .ToList();
            Assert.Single(invalidWarnings);
        }
    }

    // --- apply_capped_attributes (unified node/edge merge helper) -----------------

    [Fact]
    public void test_apply_overlay_keeps_prior_for_omitted_and_dropped_fields()
    {
        var prior = Dict(("phones", "415-555-0142"), ("industry", "Software"));
        var bleed = new string('x', (int)(DefaultAttributeMaxLength + 1));
        var llm = Dict(("phones", bleed)); // industry omitted; phones over-cap
        var (merged, dropped) = AttributeUtils.ApplyCappedAttributes(llm, Person, prior, mergeMode: "overlay");
        Assert.Equal("415-555-0142", merged["phones"]); // cap-dropped; prior preserved
        Assert.Equal("Software", merged["industry"]); // LLM-omitted; prior preserved
        Assert.Equal(["phones"], dropped);
    }

    [Fact]
    public void test_apply_overlay_legitimate_update_passes_through()
    {
        var prior = Dict(("phones", "415-555-0142"), ("industry", "Software"));
        var llm = Dict(("industry", "SaaS"));
        var (merged, dropped) = AttributeUtils.ApplyCappedAttributes(llm, Person, prior, mergeMode: "overlay");
        Assert.Equal(PyJson.Dumps(Dict(("phones", "415-555-0142"), ("industry", "SaaS"))), PyJson.Dumps(merged));
        Assert.Empty(dropped);
    }

    [Fact]
    public void test_apply_overlay_preserves_prior_non_null_for_extracted_nulls()
    {
        var prior = Dict(("phones", "415-555-0142"));
        var llm = Dict(("phones", null), ("industry", null));
        var (merged, dropped) = AttributeUtils.ApplyCappedAttributes(llm, Person, prior, mergeMode: "overlay");
        Assert.Equal(PyJson.Dumps(Dict(("phones", "415-555-0142"), ("industry", null))), PyJson.Dumps(merged));
        Assert.Empty(dropped);
    }

    [Fact]
    public void test_apply_replace_clears_omitted_but_preserves_dropped()
    {
        var prior = Dict(("phones", "415-555-0142"), ("industry", "Software"));
        var bleed = new string('x', (int)(DefaultAttributeMaxLength + 1));
        var llm = Dict(("phones", bleed)); // industry omitted; phones over-cap
        var (merged, dropped) = AttributeUtils.ApplyCappedAttributes(llm, Person, prior, mergeMode: "replace");
        Assert.Equal("415-555-0142", merged["phones"]); // cap-dropped; prior restored
        Assert.False(merged.ContainsKey("industry")); // LLM-omitted; cleared
        Assert.Equal(["phones"], dropped);
    }

    [Fact]
    public void test_apply_replace_legitimate_update_replaces_prior_and_clears_omitted()
    {
        var prior = Dict(("phones", "415-555-0142"), ("industry", "Software"));
        var llm = Dict(("industry", "SaaS"));
        var (merged, dropped) = AttributeUtils.ApplyCappedAttributes(llm, Person, prior, mergeMode: "replace");

        // phones was omitted by the LLM → cleared per replace semantics.
        Assert.Equal(PyJson.Dumps(Dict(("industry", "SaaS"))), PyJson.Dumps(merged));
        Assert.Empty(dropped);
    }

    [Fact]
    public void test_apply_replace_preserves_prior_non_null_for_extracted_nulls()
    {
        var prior = Dict(("phones", "415-555-0142"), ("description", "prior"));
        var llm = Dict(("phones", null), ("industry", null));
        var (merged, dropped) = AttributeUtils.ApplyCappedAttributes(llm, Person, prior, mergeMode: "replace");
        Assert.Equal(PyJson.Dumps(Dict(("phones", "415-555-0142"), ("industry", null))), PyJson.Dumps(merged));
        Assert.Empty(dropped);
    }

    private static IDisposable SetEnv(string value) => PyEnv.Use(name => name == "GRAPHITI_ATTRIBUTE_MAX_LENGTH" ? value : null);

    private static OrderedDictionary<string, object?> Dict(params (string Key, object? Value)[] entries)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in entries)
        {
            result[key] = value;
        }

        return result;
    }
}
