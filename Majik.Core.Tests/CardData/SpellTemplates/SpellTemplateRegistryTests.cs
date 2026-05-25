using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Majik.Core.Game;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

public class SpellTemplateRegistryTests
{
    private static SpellBindContext Ctx(string oracleText)
    {
        var caster = new Player("Alice", 20);
        var entity = new CardEntity { Name = "Test", OracleText = oracleText };
        return new SpellBindContext(entity, caster, _ => _, null, null);
    }

    private sealed class FakeTemplate : ISpellTemplate
    {
        public int Priority { get; init; }
        public string Name { get; init; } = "";
        public Func<SpellBindContext, SpellDefinition?> Match { get; init; } = _ => null;
        public SpellDefinition? TryBind(SpellBindContext ctx) => Match(ctx);
    }

    [Fact]
    public void TryBind_ReturnsNull_WhenNoTemplateMatches()
    {
        var reg = new SpellTemplateRegistry(Array.Empty<ISpellTemplate>());

        reg.TryBind(Ctx("anything")).Should().BeNull();
    }

    [Fact]
    public void TryBind_PicksHighestPriorityMatch_NotDeclarationOrder()
    {
        var hit = new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: Array.Empty<Majik.Core.Players.Agents.TargetRequest>(),
            EffectFactory: _ => Array.Empty<Majik.Core.Abilities.IEffect>());

        var low = new FakeTemplate { Name = "low", Priority = 10, Match = _ => hit };
        var high = new FakeTemplate { Name = "high", Priority = 100, Match = _ => hit };

        // Registered low-first to prove priority overrides declaration order.
        var reg = new SpellTemplateRegistry(new ISpellTemplate[] { low, high });

        reg.TryBind(Ctx("text")).Should().BeSameAs(hit);
        reg.OrderedTemplates.Should().StartWith(high);
    }
}
