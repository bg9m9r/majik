using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class UntapUpToTwoAndPumpTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: null);

    [Theory]
    [InlineData("Untap up to two target creatures. They each get +2/+2 until end of turn.")]
    [InlineData("Untap up to two target creatures. They each get +1/+1 until end of turn.")]
    [InlineData("Untap up to two target creatures. They each get +3/+3 until end of turn.")]
    public void Binds_OnFamilyOracle(string oracle)
    {
        new UntapUpToTwoAndPumpTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Single-target untap+pump — out of family.
    [InlineData("Untap target creature. It gets +2/+2 until end of turn.")]
    // No pump rider.
    [InlineData("Untap up to two target creatures.")]
    // No untap — plain pump.
    [InlineData("Target creature gets +2/+2 until end of turn.")]
    public void DoesNotBind_OutOfFamily(string oracle)
    {
        new UntapUpToTwoAndPumpTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Fact]
    public void TargetRequest_AcceptsZeroToTwoCreatures()
    {
        var def = new UntapUpToTwoAndPumpTemplate().TryBind(Ctx(
            "Untap up to two target creatures. They each get +2/+2 until end of turn."));
        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(2);
    }

    [Fact]
    public void Intent_IsBuffAndCombatTrick()
    {
        var intent = new UntapUpToTwoAndPumpTemplate().Intent;
        intent.HasAny(BotIntent.Buff).Should().BeTrue();
        intent.HasAny(BotIntent.CombatTrick).Should().BeTrue();
    }

    [Fact]
    public void Priority_Is_70()
    {
        new UntapUpToTwoAndPumpTemplate().Priority.Should().Be(70);
    }

    [Fact]
    public void Effect_UntapsBothTargets_AndAppliesPump()
    {
        var caster = new Player("A", 20);
        var effects = new ContinuousEffectsService();

        var c1 = new Creature("Bear1", "1G", 2, 2)
        {
            Owner = caster, Controller = caster, Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        var c2 = new Creature("Bear2", "1G", 1, 1)
        {
            Owner = caster, Controller = caster, Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        c1.Tap();
        c2.Tap();

        var def = new UntapUpToTwoAndPumpTemplate().TryBind(Ctx(
            "Untap up to two target creatures. They each get +2/+2 until end of turn."));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { c1, c2 } },
            Mana: new ManaPayment(Array.Empty<ICard>()));

        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        c1.IsTapped.Should().BeFalse();
        c2.IsTapped.Should().BeFalse();
        c1.Power.Should().Be(4);
        c1.Toughness.Should().Be(4);
        c2.Power.Should().Be(3);
        c2.Toughness.Should().Be(3);
    }

    [Fact]
    public void Effect_NoTargetsChosen_Resolves_NoOp()
    {
        var def = new UntapUpToTwoAndPumpTemplate().TryBind(Ctx(
            "Untap up to two target creatures. They each get +2/+2 until end of turn."));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { Array.Empty<object>() },
            Mana: new ManaPayment(Array.Empty<ICard>()));

        var resolved = def!.EffectFactory(chosen);
        var act = () => { foreach (var e in resolved) e.Execute(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void OracleSpellBinder_RegistersTemplate()
    {
        Majik.Core.CardData.OracleSpellBinder.Registry.OrderedTemplates
            .Should().Contain(t => t.Name == "UntapUpToTwoAndPump");
    }
}
