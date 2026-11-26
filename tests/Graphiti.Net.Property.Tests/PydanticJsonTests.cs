namespace Graphiti.PyCompat.Tests.Models;

using System.Text.Json;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.Models.PydanticValues;
using DecoderFallbackException = System.Text.DecoderFallbackException;
using EncoderFallbackException = System.Text.EncoderFallbackException;

public class PydanticJsonTests
{
    [Fact]
    public void Schema_text_matches_each_judge_golden_text()
    {
        // The judge function judge:schema_text is json.dumps(M.model_json_schema()) (port/judge/host-python/host.py).
        var judge = JudgeFolder();
        var golden = File.ReadAllLines(Path.Combine(judge, "golden", "pure", "schemas.jsonl"))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToDictionary(e => e.GetProperty("id").GetString() ?? string.Empty, e => e.GetProperty("result").GetProperty("ok").GetString());
        var cases = File.ReadAllLines(Path.Combine(judge, "cases", "pure", "schemas.jsonl")).Select(line => JsonDocument.Parse(line).RootElement).ToList();
        Assert.Equal(49, cases.Count);
        foreach (var c in cases)
        {
            Assert.Equal("judge:schema_text", c.GetProperty("fn").GetString());
            var path = c.GetProperty("args")[0].GetString() ?? string.Empty;
            var schema = UpstreamSchemas.ByName[path[(path.IndexOf(':', StringComparison.Ordinal) + 1)..]];
            var id = c.GetProperty("id").GetString() ?? string.Empty;
            Assert.True(golden[id] == PyJson.Dumps(schema.ToDict()), $"{id}\nexpected {golden[id]}\nactual   {PyJson.Dumps(schema.ToDict())}");
        }
    }

    [Fact]
    public void Schema_text_matches_pydantic_for_shapes_that_upstream_lacks()
    {
        // The test models AC, Ab, and SchemaShapes of generate_pydantic_values.py.
        var ac = new PydanticSchema("AC", "Two nested models whose names sort apart by code point (AC before Ab) and by case (Ab before AC).")
            .Default("x", PydanticType.Int, 1L);
        var ab = new PydanticSchema("Ab").Required("y", PydanticType.Str);
        var shapes = new PydanticSchema("SchemaShapes", "Shapes that no upstream model has: a default dict with keys out of order, and $defs order by code point.")
            .Required("b", PydanticType.Model(ab))
            .Default("a", PydanticType.Optional(PydanticType.Model(ac)), null)
            .Default(
                "d",
                PydanticType.Dict(PydanticType.Any),
                new OrderedDictionary<string, object?> { ["z"] = 1L, ["a"] = new List<object?> { 2L, new OrderedDictionary<string, object?> { ["y"] = 3L, ["b"] = 4L } } })
            .Default("e", PydanticType.Enum<EdgeReranker>(), EdgeReranker.Mmr);
        var recorded = Root.GetProperty("schemas");
        Assert.Equal(recorded.GetProperty("SchemaShapes").GetString(), PyJson.Dumps(shapes.ToDict()));
        Assert.Equal(recorded.GetProperty("AC").GetString(), PyJson.Dumps(ac.ToDict()));
    }

    [Fact]
    public void DumpJson_matches_dump_json()
    {
        ForEach(List("dump_json"), c =>
        {
            var input = new Decoder().Decode(c.GetProperty("v"));
            if (c.TryGetProperty("e", out var e))
            {
                var expected = e.GetString() == "UnicodeEncodeError" ? typeof(EncoderFallbackException) : typeof(DecoderFallbackException);
                var ex = Record.Exception(() => Pydantic.DumpJson(input));
                Assert.IsType(expected, ex, exactMatch: true);
                if (expected == typeof(EncoderFallbackException))
                {
                    Assert.Equal(Text(c.GetProperty("msg")), ex?.Message);
                }
            }
            else
            {
                Assert.Equal(c.GetProperty("r").GetString(), Pydantic.DumpJson(input));
            }
        });
    }

    [Fact]
    public void LoadJson_matches_model_validate_json()
    {
        ForEach(List("load_json"), c =>
        {
            Func<OrderedDictionary<string, object?>> load = c.TryGetProperty("bytes", out var bytes)
                ? () => Pydantic.LoadJson(bytes.EnumerateArray().Select(b => (byte)b.GetInt32()).ToArray())
                : () => Pydantic.LoadJson(Text(c.GetProperty("text")));
            var context = c.ToString()[..Math.Min(c.ToString().Length, 120)];
            if (c.TryGetProperty("errors", out var errors))
            {
                var ex = Record.Exception(() => load());
                var vex = ex as ValidationException ?? throw new Xunit.Sdk.XunitException($"{context}: expected ValidationException, got {ex?.GetType().Name ?? "no exception"}");
                Assert.Equal(errors.EnumerateArray().Select(e => e[1].GetString()), vex.Errors.Select(e => e.Type));
                Assert.All(vex.Errors, e => Assert.Empty(e.Loc));
            }
            else
            {
                AssertValue(new Decoder().Decode(c.GetProperty("r")), load(), context);
            }
        });
    }

    [Fact]
    public void DumpJson_then_LoadJson_gives_ModelValidate_the_same_model()
    {
        // ChangeSet.to_bytes and from_bytes (ingestion/change_set.py:122 to :129) in the port's form.
        foreach (var c in List("models").Where(c => c.TryGetProperty("dump", out _)))
        {
            var model = c.GetProperty("model").GetString() ?? string.Empty;
            var m = TestModels.Validate(model, new Decoder().DecodeDict(c.GetProperty("data")));
            var text = Pydantic.DumpJson(m.ModelDump(json: true));
            var again = TestModels.Validate(model, Pydantic.LoadJson(text));
            Assert.Equal(text, Pydantic.DumpJson(again.ModelDump(json: true)));
        }
    }

    private static string JudgeFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var judge = Path.Combine(dir.FullName, "port", "judge");
            if (Directory.Exists(judge))
            {
                return judge;
            }
        }

        throw new DirectoryNotFoundException("port/judge above " + AppContext.BaseDirectory);
    }
}
