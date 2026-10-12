using System.Text.Json.Nodes;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ApplicationEnvironmentPatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileBoundsUseUtf8BytesRatherThanCharacterCount(bool existing)
    {
        var value = new string('鲸', 30_000);
        var source = existing ? "KEY=" + value + "\n" : "";
        var patch = new JsonObject { ["set"] = new JsonObject { ["KEY"] = existing ? "small" : value } };
        Assert.Throws<InvalidOperationException>(() => ApplicationEnvironmentPatch.Apply(source, patch));
    }

    [Fact]
    public void NamesExcludeValuesAndCommentsAndUnderstandQuotedMultilineEntries()
    {
        const string source = "# no value disclosure\nexport PASSWORD='must-not-leak'\nCERT='line1\nline2'\nEMPTY=\n";
        Assert.Equal(["CERT", "EMPTY", "PASSWORD"], ApplicationEnvironmentPatch.Names(source));
        Assert.Equal(source, ApplicationEnvironmentPatch.Apply(source, null));
    }

    [Fact]
    public void SetAndRemovePreserveUnrelatedEntriesAndLiteralSyntax()
    {
        const string source = "# keep comment\nOLD='line1\nline2'\nKEEP=$EXISTING\nCHANGE=before\n";
        var patch = JsonNode.Parse("{\"set\":{\"CHANGE\":\"a'b $LITERAL\",\"EMPTY\":\"\"},\"remove\":[\"OLD\"]}")!.AsObject();
        var changed = ApplicationEnvironmentPatch.Apply(source, patch);
        Assert.Contains("# keep comment\n", changed);
        Assert.Contains("KEEP=$EXISTING\n", changed);
        Assert.DoesNotContain("line2", changed);
        Assert.Contains("CHANGE=\"a'b \\$LITERAL\"\n", changed);
        Assert.Contains("EMPTY=\"\"\n", changed);
        Assert.Equal(["CHANGE", "EMPTY", "KEEP"], ApplicationEnvironmentPatch.Names(changed));
    }

    [Theory]
    [InlineData("KEY=1\nKEY=2\n")]
    [InlineData("BAD-NAME=x")]
    [InlineData("KEY='not closed")]
    [InlineData("KEY='value' trailing")]
    [InlineData("not an entry")]
    public void AmbiguousExistingSourceIsNeverRewritten(string source) =>
        Assert.Throws<InvalidOperationException>(() => ApplicationEnvironmentPatch.Apply(source,
            JsonNode.Parse("{\"set\":{\"NEW\":\"value\"}}")!.AsObject()));

    [Theory]
    [InlineData("{\"set\":{\"KEY\":\"value\"},\"remove\":[\"KEY\"]}")]
    [InlineData("{\"set\":{\"BAD-NAME\":\"value\"}}")]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("{\"set\":[]}")]
    [InlineData("{\"remove\":{}}")]
    [InlineData("{\"set\":{\"NAME\\n\":\"value\"}}")]
    public void InvalidOrConflictingPatchFailsWithoutReturningValues(string json) =>
        Assert.Throws<InvalidOperationException>(() => ApplicationEnvironmentPatch.Apply("KEEP=x\n", JsonNode.Parse(json)!.AsObject()));

    [Theory]
    [InlineData("ends\\")]
    [InlineData("a\\'b")]
    [InlineData("a\"b")]
    [InlineData("$BASE ${BASE} $$")]
    [InlineData("a\nb")]
    public void EncodedLiteralsRemainWellFormedAndExposeOnlyTheirNames(string value)
    {
        var output = ApplicationEnvironmentPatch.Apply("", new JsonObject { ["set"] = new JsonObject { ["KEY"] = value } });
        Assert.Equal(["KEY"], ApplicationEnvironmentPatch.Names(output));
    }
}
