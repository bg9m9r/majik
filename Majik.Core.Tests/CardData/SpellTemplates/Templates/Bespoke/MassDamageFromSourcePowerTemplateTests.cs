using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class MassDamageFromSourcePowerTemplateTests
{
    private static SpellBindContext Ctx(string text, Player? caster = null) =>
        new(new CardEntity { Name = "X", OracleText = text },
            caster ?? new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: null);

    [Theory]
    [InlineData(
        "Target creature you control deals damage equal to its power to each other creature and each opponent.")]
    [InlineData(
        "Target creature you control deals damage equal to its power to each other creature.")]
    // Waltz of Rage — leading clause matches; the rider after the period is
    // ignored by the regex anchor.
    [InlineData(
        "Target creature you control deals damage equal to its power to each other creature. " +
        "Until end of turn, whenever a creature you control dies, exile the top card of your library. " +
        "You may play it until the end of your next turn.")]
    public void Binds_OnFamilyOracle(string oracle)
    {
        new MassDamageFromSourcePowerTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Single-target asymmetric fight — out of family.
    [InlineData(
        "Target creature you control deals damage equal to its power to target creature you don't control.")]
    // Standard fight — bilateral.
    [InlineData("Target creature you control fights target creature you don't control.")]
    // Wrong source filter.
    [InlineData(
        "Target creature an opponent controls deals damage equal to its power to each other creature.")]
    [InlineData("Each creature deals damage equal to its power to each other creature.")]
    public void DoesNotBind_OutOfFamily(string oracle)
    {
        new MassDamageFromSourcePowerTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Fact]
    public void TargetRequest_OneTarget_CreatureYouControl()
    {
        var def = new MassDamageFromSourcePowerTemplate().TryBind(Ctx(
            "Target creature you control deals damage equal to its power to each other creature and each opponent."));
        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Intent_IsWrathAndBurn()
    {
        var intent = new MassDamageFromSourcePowerTemplate().Intent;
        intent.HasAny(BotIntent.Wrath).Should().BeTrue();
        intent.HasAny(BotIntent.Burn).Should().BeTrue();
    }

    [Fact]
    public void Priority_Is_70()
    {
        new MassDamageFromSourcePowerTemplate().Priority.Should().Be(70);
    }

    [Fact]
    public void Effect_DamageHitsEveryOtherCreature_AcrossPlayers()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);

        var source = new Creature("Source", "3R", 5, 5)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        var ally = new Creature("Ally", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        var foe = new Creature("Foe", "1G", 4, 4)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
        };
        alice.Zones.Battlefield.AddCard(source);
        alice.Zones.Battlefield.AddCard(ally);
        bob.Zones.Battlefield.AddCard(foe);

        var def = new MassDamageFromSourcePowerTemplate().TryBind(Ctx(
            "Target creature you control deals damage equal to its power to each other creature.",
            alice));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { source } },
            Mana: new ManaPayment(Array.Empty<ICard>()),
            AllPlayers: new[] { alice, bob });

        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // Source untouched.
        source.Damage.Should().Be(0);
        // Both other creatures took source.Power damage.
        ally.Damage.Should().Be(5);
        foe.Damage.Should().Be(5);
    }

    [Fact]
    public void Effect_EachOpponentRider_DealsToNonCasterPlayers()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);
        var carol = new Player("C", 20);

        var source = new Creature("Source", "3R", 4, 4)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        alice.Zones.Battlefield.AddCard(source);

        var def = new MassDamageFromSourcePowerTemplate().TryBind(Ctx(
            "Target creature you control deals damage equal to its power to each other creature and each opponent.",
            alice));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { source } },
            Mana: new ManaPayment(Array.Empty<ICard>()),
            AllPlayers: new[] { alice, bob, carol });

        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.LifeTotal.Should().Be(20);
        bob.LifeTotal.Should().Be(16);
        carol.LifeTotal.Should().Be(16);
    }

    [Fact]
    public void Effect_NoEachOpponent_DoesNotDamageOpponents()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);

        var source = new Creature("Source", "3R", 3, 3)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        alice.Zones.Battlefield.AddCard(source);

        var def = new MassDamageFromSourcePowerTemplate().TryBind(Ctx(
            "Target creature you control deals damage equal to its power to each other creature.",
            alice));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { source } },
            Mana: new ManaPayment(Array.Empty<ICard>()),
            AllPlayers: new[] { alice, bob });

        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.LifeTotal.Should().Be(20);
        bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void OracleSpellBinder_RegistersTemplate()
    {
        Majik.Core.CardData.OracleSpellBinder.Registry.OrderedTemplates
            .Should().Contain(t => t.Name == "MassDamageFromSourcePower");
    }
}
