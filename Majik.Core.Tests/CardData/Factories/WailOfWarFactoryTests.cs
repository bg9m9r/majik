using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WailOfWarFactory"/> (Modern Horizons 3, {2}{B}).
///
/// CR 700.2d — modal "Choose one —" instant with 2 modes:
///   Mode 0: Creatures target opponent controls get -1/-1 until end of turn.
///   Mode 1: Return up to two target creature cards from your graveyard to
///           your hand.
/// </summary>
[Trait("Color", "B")]
public class WailOfWarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // A live continuous-effects service so PumpUntilEndOfTurnEffect can register.
    private static ContinuousEffectsService NewEffects() => new();

    // -----------------------------------------------------------------------
    // Identity (non-vanilla cost — single identity assert)
    // -----------------------------------------------------------------------

    [Fact]
    public void WailOfWar_Create_HasInstantShape_Black()
    {
        var card = WailOfWarFactory.Create(_alice);

        card.Name.Should().Be("Wail of War");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{2}{B} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WailOfWar_BuildDefinition_ExposesTwoModes_AndTargetRequests()
    {
        var def = WailOfWarFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(2);
        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[WailOfWarFactory.ModeMinusOne].MaxTargets.Should().Be(1);
        def.TargetRequests[WailOfWarFactory.ModeReturn].MaxTargets.Should().Be(2);
        def.TargetRequests[WailOfWarFactory.ModeMinusOne].MinTargets.Should().Be(0);
        def.TargetRequests[WailOfWarFactory.ModeReturn].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — creatures target opponent controls get -1/-1 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void WailOfWar_Mode0_GivesTargetOpponentsCreaturesMinusOneMinusOne()
    {
        var effects = NewEffects();

        // Bob (the targeted opponent) controls two creatures.
        var bobCreature1 = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = effects };
        bobCreature1.SetZone(ZoneType.Battlefield);
        var bobCreature2 = new Creature("Centaur Courser", "{2}{G}", 3, 3)
        { Owner = _bob, Controller = _bob, ActiveEffects = effects };
        bobCreature2.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobCreature1);
        _bob.Zones.Battlefield.AddCard(bobCreature2);

        // Alice (the caster) controls a creature that must NOT be affected.
        var aliceCreature = new Creature("Llanowar Elves", "{G}", 1, 1)
        { Owner = _alice, Controller = _alice, ActiveEffects = effects };
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var def = WailOfWarFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { _bob },   // mode 0 — target opponent
            Array.Empty<object>(),   // mode 1 (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: WailOfWarFactory.ModeMinusOne,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var built = def.EffectFactory(chosen);
        built.Should().HaveCount(1);
        foreach (var e in built) e.Execute();

        bobCreature1.Power.Should().Be(1, because: "-1/-1 applies to the target opponent's creatures");
        bobCreature1.Toughness.Should().Be(1);
        bobCreature2.Power.Should().Be(2);
        bobCreature2.Toughness.Should().Be(2);

        // CR 109.5 — only the TARGET opponent's creatures are affected; the
        // caster's own creature is untouched.
        aliceCreature.Power.Should().Be(1, because: "the caster's own creatures are not affected");
        aliceCreature.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — return up to two creature cards from your graveyard to hand
    // -----------------------------------------------------------------------

    [Fact]
    public void WailOfWar_Mode1_ReturnsTwoCreatureCardsFromGraveyardToHand()
    {
        var deadBeast1 = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        deadBeast1.SetZone(ZoneType.Graveyard);
        var deadBeast2 = new Creature("Centaur Courser", "{2}{G}", 3, 3) { Owner = _alice };
        deadBeast2.SetZone(ZoneType.Graveyard);
        // A noncreature card in the graveyard must not be returnable.
        var deadBolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        deadBolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(deadBeast1);
        _alice.Zones.Graveyard.AddCard(deadBeast2);
        _alice.Zones.Graveyard.AddCard(deadBolt);

        var def = WailOfWarFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),                       // mode 0 (unused)
            new object[] { deadBeast1, deadBeast2 },     // mode 1 — two creature cards
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: WailOfWarFactory.ModeReturn,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var built = def.EffectFactory(chosen);
        built.Should().HaveCount(1);
        foreach (var e in built) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { deadBeast1, deadBeast2 },
            because: "both targeted creature cards return from graveyard to hand");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(deadBeast1);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(deadBeast2);
        _alice.Zones.Graveyard.GetCards().Should().Contain(deadBolt,
            because: "the noncreature card was never a legal target");
    }

    [Fact]
    public void WailOfWar_Mode1_WithZeroTargets_IsACleanNoOp()
    {
        var def = WailOfWarFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),   // mode 1 — "up to two" → zero is legal
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: WailOfWarFactory.ModeReturn,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var built = def.EffectFactory(chosen);
        built.Should().HaveCount(1);
        var act = () => { foreach (var e in built) e.Execute(); };
        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
