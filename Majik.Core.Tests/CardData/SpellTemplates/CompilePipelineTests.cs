using System.Text.Json;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Counter;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// Pins the Phase-2 ExtractParams + Rehydrate contract: a template that
/// opts into the split must produce a JSON-serializable parameter dict
/// whose round-trip through string-string storage is enough to rebuild
/// the same SpellDefinition the live TryBind would produce.
///
/// Pilot coverage = Counter family (4 templates) — other families opt
/// in over follow-up PRs.
/// </summary>
public class CompilePipelineTests
{
    private static SpellBindContext Ctx(string oracleText) =>
        new(new CardEntity { Name = "X", OracleText = oracleText },
            new Player("A", 20), _ => _, null, null);

    [Theory]
    [InlineData("Counter target spell.")]
    [InlineData("Counter target creature spell.")]
    [InlineData("Counter target noncreature spell.")]
    [InlineData("Counter target spell unless its controller pays {3}.")]
    public void DefaultTryBind_RoutesThroughExtractAndRehydrate(string oracleText)
    {
        var templates = AllCounterTemplates();
        var ctx = Ctx(oracleText);

        var matched = templates
            .OrderByDescending(t => t.Priority)
            .First(t => t.TryBind(ctx) is not null);

        // Same template's TryExtractParams must succeed for the same text.
        var @params = matched.TryExtractParams(oracleText);
        @params.Should().NotBeNull("the template's regex match must agree with its TryExtractParams");

        // Rehydrating with those params produces a non-null SpellDefinition.
        var rehydrated = matched.Rehydrate(@params!, ctx);
        rehydrated.Should().NotBeNull();
    }

    [Fact]
    public void TryExtractParams_OutputIsJsonSerializable_AndRoundTripsToSameRehydration()
    {
        var template = new CounterUnlessPayTemplate();
        var oracleText = "Counter target spell unless its controller pays {3}.";

        var @params = template.TryExtractParams(oracleText);
        @params.Should().NotBeNull();
        @params!["n"].Should().Be("3");

        // Round-trip through JSON to verify the dict is stable-string-serializable.
        var json = JsonSerializer.Serialize(@params);
        var restored = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        restored.Should().NotBeNull();
        restored!["n"].Should().Be("3");

        // Rehydrating from the restored params produces a non-null SpellDefinition.
        var ctx = Ctx(oracleText);
        template.Rehydrate(restored, ctx).Should().NotBeNull();
    }

    [Fact]
    public void TryExtractParams_ReturnsNull_OnMismatchedText()
    {
        new CounterTargetSpellTemplate().TryExtractParams("Draw a card.").Should().BeNull();
        new CounterUnlessPayTemplate().TryExtractParams("Counter target spell.").Should().BeNull();
    }

    [Fact]
    public void Rehydrate_OnTemplateThatDidNotOptIn_ThrowsNotSupported()
    {
        // Sanity guard: the default Rehydrate throws so a mis-wired compile
        // pipeline is loud rather than silently producing null SpellDefinitions.
        ISpellTemplate stub = new FakeOptedOutTemplate();
        Action call = () => stub.Rehydrate(
            new Dictionary<string, string>(),
            Ctx("anything"));
        call.Should().Throw<NotSupportedException>()
            .WithMessage("*does not implement Rehydrate*");
    }

    private static IEnumerable<ISpellTemplate> AllCounterTemplates() =>
        new ISpellTemplate[]
        {
            new CounterUnlessPayTemplate(),
            new CounterNoncreatureTemplate(),
            new CounterCreatureTemplate(),
            new CounterTargetSpellTemplate(),
        };

    private sealed class FakeOptedOutTemplate : ISpellTemplate
    {
        public int Priority => 0;
        public string Name => "FakeOptedOut";
        public Majik.Core.Game.SpellDefinition? TryBind(SpellBindContext ctx) => null;
        // Inherits default TryExtractParams (null) and Rehydrate (throws).
    }
}
