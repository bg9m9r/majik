using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HelpingHandFactory"/>.
///
/// Helping Hand ({W}). Sorcery. Oracle text (verified against Scryfall 2026-06-14):
///   "Return target creature card with mana value 3 or less from your
///    graveyard to the battlefield tapped."
///
/// Covers ONLY Helping Hand's unique behaviour (identity dispatch + the
/// well-formedness checks are covered for every card by
/// CardFactoryContractTests):
/// - Identity: {W}, white, mana value 1 (the one non-trivial stat assert).
/// - Resolve: returns an MV ≤ 3 creature card from the CASTER's graveyard to
///   the caster's battlefield, ENTERING TAPPED (the "tapped" rider).
/// - MV boundary: MV exactly 3 is legal; MV 4+ is NOT a legal target (no-op).
/// - "your graveyard" scope: opponent's graveyard creature is not a legal
///   target (CR 608.2b).
/// - ZoneService routing: graveyard → battlefield fires CardMovedEvent
///   (CR 603.6a — ETB triggers).
/// - Non-creature target is a clean no-op (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class HelpingHandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Identity_SorceryWhiteW_ManaValueOne()
    {
        var card = HelpingHandFactory.Create(_alice);

        card.Name.Should().Be("Helping Hand");
        card.Should().BeOfType<Sorcery>();
        card.ManaCost.Should().Be("{W}");
        card.ManaCostValue.TotalValue.Should().Be(1, "printed mana cost {W} has MV 1");
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_ReturnsMv3OrLessCreature_ToBattlefield_Tapped()
    {
        // Ravenous Rats — {1}{B}, MV 2 — within the MV ≤ 3 cap.
        var rats = new Creature("Ravenous Rats", "{1}{B}", 1, 1);
        rats.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(rats);
        rats.SetZone(ZoneType.Graveyard);

        ExecuteResolve(_alice, rats);

        rats.Zone.Should().Be(ZoneType.Battlefield,
            "MV 2 ≤ 3: the creature is returned to the caster's battlefield");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(rats);
        _alice.Zones.Battlefield.GetCards().Should().Contain(rats);
        rats.Controller.Should().BeSameAs(_alice,
            "the returned permanent enters under the caster's control (CR 110.2)");
        rats.IsTapped.Should().BeTrue(
            "Helping Hand returns the creature to the battlefield TAPPED");
        _alice.LifeTotal.Should().Be(20,
            "Helping Hand has no life-loss clause — caster's life is unchanged");
    }

    [Fact]
    public void Resolve_ReturnsCreatureWithMvExactly3_Tapped()
    {
        // MV exactly 3 — at the legal boundary.
        var ogre = new Creature("Sewer Ogre", "{2}{B}", 3, 3);
        ogre.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(ogre);
        ogre.SetZone(ZoneType.Graveyard);

        ExecuteResolve(_alice, ogre);

        ogre.Zone.Should().Be(ZoneType.Battlefield, "MV 3 is exactly the legal threshold");
        ogre.IsTapped.Should().BeTrue("returned tapped");
    }

    [Fact]
    public void Resolve_NoOp_WhenCreatureHasMvAbove3()
    {
        // Hill Giant — {3}{R}, MV 4 — NOT a legal Helping Hand target.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        ExecuteResolve(_alice, giant);

        giant.Zone.Should().Be(ZoneType.Graveyard,
            "MV 4 > 3: not a legal target (CR 608.2b → no-op)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    [Fact]
    public void Resolve_OnlyCastersGraveyard_NotOpponentGraveyard()
    {
        // Creature sits in BOB's graveyard — "your graveyard" is the caster's.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bears);
        bears.SetZone(ZoneType.Graveyard);

        ExecuteResolve(_alice, bears);

        bears.Zone.Should().Be(ZoneType.Graveyard,
            "the creature belongs to another player's graveyard — not 'your graveyard' (CR 608.2b)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bears);
    }

    [Fact]
    public void Resolve_IgnoresNonCreatureTarget()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var act = () => ExecuteResolve(_alice, bolt);

        act.Should().NotThrow("a non-creature target is illegal — resolve no-ops (CR 608.2b)");
        bolt.Zone.Should().Be(ZoneType.Graveyard, "instants are not creature cards");
    }

    [Fact]
    public void Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var elf = new Creature("Llanowar Elves", "{G}", 1, 1);
        elf.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(elf);
        elf.SetZone(ZoneType.Graveyard);

        var def = HelpingHandFactory.BuildSpellDefinition(_alice, o => o, zoneService: zones);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { elf } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        elf.Zone.Should().Be(ZoneType.Battlefield);
        elf.IsTapped.Should().BeTrue("returned tapped");
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, elf)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    private static void ExecuteResolve(Player caster, ICard target)
    {
        var def = HelpingHandFactory.BuildSpellDefinition(caster, o => o);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { target } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();
    }
}
