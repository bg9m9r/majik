using FluentAssertions;
using Majik.Core.CardData;
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
/// Unit tests for <see cref="ScorchingDragonfireFactory"/> (Adventures in the
/// Forgotten Realms, {1}{R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Scorching Dragonfire deals 3 damage to target creature or planeswalker.
///    If that creature or planeswalker would die this turn, exile it instead."
///
/// The 3-damage-to-creature-or-planeswalker target shape mirrors
/// <see cref="MagmaticSinkholeFactory"/>; the exile-instead rider mirrors
/// <see cref="PillarOfFlameFactory"/> / <see cref="SpikefieldHazardFactory"/>.
/// </summary>
[Trait("Color", "R")]
public class ScorchingDragonfireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private ChosenSpellParams Chosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    private Creature CreatureOnBattlefield(Player owner, int power, int tough)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_InstantAt1R_Red()
    {
        var card = ScorchingDragonfireFactory.Create(_alice);

        card.Name.Should().Be("Scorching Dragonfire");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ScorchingDragonfire()
    {
        var card = NamedCardFactory.Create("Scorching Dragonfire", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Scorching Dragonfire");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_SingleCreatureOrPlaneswalkerTargetRequest()
    {
        var def = ScorchingDragonfireFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature or planeswalker");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve — 3 damage to target creature or planeswalker.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsThreeDamageToCreature()
    {
        var bear = CreatureOnBattlefield(_bob, 2, 4);

        var def = ScorchingDragonfireFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Damage.Should().Be(3,
            because: "Scorching Dragonfire deals 3 damage to the target creature");
    }

    [Fact]
    public void Resolve_RemovesThreeLoyaltyFromPlaneswalker()
    {
        var pw = new Planeswalker("Test Walker", "{2}{R}", startingLoyalty: 5)
        { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = ScorchingDragonfireFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(pw))) e.Execute();

        pw.Loyalty.Should().Be(2,
            because: "3 damage to a planeswalker removes 3 loyalty (CR 306.7)");
    }

    [Fact]
    public void Resolve_NoOp_OnNonCreatureNonPlaneswalkerTarget()
    {
        // CR 608.2b — a player is not a legal target; no damage.
        var def = ScorchingDragonfireFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            because: "Scorching Dragonfire damages only creatures/planeswalkers, not players");
    }

    // -----------------------------------------------------------------------
    // Exile-instead rider (CR 700.3 / CR 514.2).
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DamagedCreatureDeath_RewrittenToExile()
    {
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);

        var def = ScorchingDragonfireFactory.BuildSpellDefinition(o => o, replacements: bus);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        var dying = new ZoneMoveIntent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        var result = bus.Apply(dying);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            because: "a creature dealt damage by Scorching Dragonfire that would die is exiled instead");
    }

    [Fact]
    public void Resolve_UntargetedCreatureDeath_NotRewritten()
    {
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);
        var other = CreatureOnBattlefield(_alice, 1, 1);

        var def = ScorchingDragonfireFactory.BuildSpellDefinition(o => o, replacements: bus);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        // CR 700.3 — a different creature dying is unaffected: its death stays a
        // graveyard move.
        var dying = new ZoneMoveIntent(other, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(dying)!.ToZone.Should().Be(ZoneType.Graveyard);
    }
}
