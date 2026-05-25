using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Tokens;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Tokens;

public class CreateCopyTokenTemplateTests
{
    private static SpellBindContext Ctx(string oracle, Player caster)
        => new(new CardEntity { Name = "X", OracleText = oracle },
            caster, _ => _, Effects: null, Stack: null);

    [Theory]
    [InlineData("Create a token that's a copy of target creature you control.")]
    [InlineData("Create a token that's a copy of target creature.")]
    [InlineData("Create a token that's a copy of target creature an opponent controls.")]
    public void Matches_SingleTargetCreatureCopy(string oracle)
    {
        new CreateCopyTokenTemplate().TryBind(Ctx(oracle, new Player("A", 20)))
            .Should().NotBeNull();
    }

    [Fact]
    public void Rehydrate_SpawnsCopyTokenUnderCasterControl()
    {
        var caster = new Player("Caster", 20);
        var source = new Creature("Grizzly Bears", "", 2, 2);
        source.SetOwner(new Player("Other", 20));
        source.AddAbility(new KeywordAbility("flying", source, source.Controller!));

        var spell = new CreateCopyTokenTemplate().TryBind(new SpellBindContext(
            new CardEntity { Name = "X",
                OracleText = "Create a token that's a copy of target creature." },
            caster, _ => source, Effects: null, Stack: null));
        spell.Should().NotBeNull();

        var p = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { source } },
            Mana: ManaPayment.Empty);
        foreach (var fx in spell!.EffectFactory(p)) fx.Execute();

        var tokens = caster.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        tokens.Should().ContainSingle(t => t.IsToken);
        var copy = tokens.Single(t => t.IsToken);
        copy.Name.Should().Be("Grizzly Bears");
        copy.BasePower.Should().Be(2);
        copy.BaseToughness.Should().Be(2);
        copy.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword.ToLowerInvariant())
            .Should().Contain("flying");
        copy.Controller.Should().Be(caster);
    }

    [Fact]
    public void Rehydrate_NonCreatureTarget_NoOp()
    {
        // Resolver returns a non-Creature (e.g. a token planeswalker by
        // accident, or wrong-target). The stub no-ops cleanly rather than
        // crashing — keeps the rest of a composed spell intact.
        var caster = new Player("Caster", 20);
        var spell = new CreateCopyTokenTemplate().TryBind(new SpellBindContext(
            new CardEntity { Name = "X",
                OracleText = "Create a token that's a copy of target creature." },
            caster, _ => "not-a-creature", Effects: null, Stack: null));
        spell.Should().NotBeNull();

        var p = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { new object() } },
            Mana: ManaPayment.Empty);
        foreach (var fx in spell!.EffectFactory(p)) fx.Execute();

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
