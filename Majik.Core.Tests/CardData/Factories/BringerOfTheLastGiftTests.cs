using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Bringer of the Last Gift (Modern Horizons 3, {6}{B}{B},
/// Creature — Vampire Demon 6/6 — "Flying. When this creature enters, if
/// you cast it, each player sacrifices all other creatures they control.
/// Then each player returns all creature cards from their graveyard that
/// weren't put there this way to the battlefield.").
///
/// Covers ONLY the card's unique behaviour (the ETB body + Flying + the
/// "if you cast it" gate) plus a single identity assert. NamedCardFactory
/// dispatch + well-formedness are covered globally by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "B")]
public class BringerOfTheLastGiftTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BringerOfTheLastGiftTests()
    {
        _zones = new ZoneService(eventBus: _bus);
    }

    private static Creature GraveyardCreature(string name, Player owner)
    {
        var c = new Creature(name, "2", 2, 2) { Owner = owner, Zone = ZoneType.Graveyard };
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    private static Creature BattlefieldCreature(string name, Player controller)
    {
        var c = new Creature(name, "2", 2, 2) { Owner = controller, Zone = ZoneType.Battlefield };
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats / subtypes / keyword)
    // -----------------------------------------------------------------------

    [Fact]
    public void Bringer_Identity()
    {
        var c = BringerOfTheLastGiftFactory.Create(_alice);

        c.Name.Should().Be("Bringer of the Last Gift");
        c.ManaCost.Should().Be("{6}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Demon).Should().BeTrue();

        // Flying (CR 702.9) — stamped from the JSON keywords array.
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
    }

    [Fact]
    public void Bringer_PrintsOneEtbTriggeredAbility()
    {
        var c = BringerOfTheLastGiftFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Bringer prints one triggered ability — the ETB sac+return.");
    }

    // -----------------------------------------------------------------------
    // ETB body — each player sacs all OTHER creatures, then returns creatures
    // from their graveyard that weren't put there this way.
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_BothPlayers_SacOtherCreatures_AndReturnPreSacGraveyard()
    {
        // Alice: Bringer + 1 other creature on battlefield, 1 creature card
        // in graveyard. Bob: 2 creatures on battlefield, 1 in graveyard.
        var bringer = BattlefieldCreature("Bringer", _alice); // stand-in board presence
        var aliceOther = BattlefieldCreature("Alice-Other", _alice);
        var aliceYard = GraveyardCreature("Alice-Yard", _alice);

        var bobOtherA = BattlefieldCreature("Bob-Other-A", _bob);
        var bobOtherB = BattlefieldCreature("Bob-Other-B", _bob);
        var bobYard = GraveyardCreature("Bob-Yard", _bob);

        BringerOfTheLastGiftFactory
            .BuildEtbEffect(bringer, new[] { _alice, _bob }, _zones)
            .Execute();

        // "all OTHER creatures" — Bringer survives, the rest are sac'd.
        bringer.Zone.Should().Be(ZoneType.Battlefield);
        aliceOther.Zone.Should().Be(ZoneType.Graveyard);
        bobOtherA.Zone.Should().Be(ZoneType.Graveyard);
        bobOtherB.Zone.Should().Be(ZoneType.Graveyard);

        // Pre-sac graveyard creatures returned to the battlefield under their
        // owner's control ("that weren't put there this way").
        aliceYard.Zone.Should().Be(ZoneType.Battlefield);
        aliceYard.Controller.Should().BeSameAs(_alice);
        bobYard.Zone.Should().Be(ZoneType.Battlefield);
        bobYard.Controller.Should().BeSameAs(_bob);

        // The just-sacrificed creatures stay in the graveyard — they were
        // "put there this way" and must NOT be returned.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(aliceOther);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(new ICard[] { bobOtherA, bobOtherB });
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceOther);
        _bob.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bobOtherA, bobOtherB });
    }

    [Fact]
    public void Etb_OnlyCreatureCardsReturned_NoncreatureGraveyardCardsStay()
    {
        var bringer = BattlefieldCreature("Bringer", _alice);
        var yardCreature = GraveyardCreature("Yard-Creature", _alice);
        var yardSpell = new Sorcery("Random Sorcery", "{B}")
        {
            Owner = _alice,
            Zone = ZoneType.Graveyard,
        };
        _alice.Zones.Graveyard.AddCard(yardSpell);

        BringerOfTheLastGiftFactory
            .BuildEtbEffect(bringer, new[] { _alice }, _zones)
            .Execute();

        yardCreature.Zone.Should().Be(ZoneType.Battlefield);
        yardSpell.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(yardSpell);
    }

    [Fact]
    public void Etb_PublishesMoveEvents_ForSacAndReturn()
    {
        var bringer = BattlefieldCreature("Bringer", _alice);
        var other = BattlefieldCreature("Other", _alice);
        var yard = GraveyardCreature("Yard", _alice);

        var moves = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(moves.Add);

        BringerOfTheLastGiftFactory
            .BuildEtbEffect(bringer, new[] { _alice }, _zones)
            .Execute();

        // Sacrifice (battlefield→graveyard) for the other creature.
        moves.Where(e => ReferenceEquals(e.Card, other))
            .Select(e => (e.FromZone, e.ToZone))
            .Should().Equal((ZoneType.Battlefield, ZoneType.Graveyard));

        // Return (graveyard→battlefield) for the graveyard creature.
        moves.Where(e => ReferenceEquals(e.Card, yard))
            .Select(e => (e.FromZone, e.ToZone))
            .Should().Equal((ZoneType.Graveyard, ZoneType.Battlefield));
    }

    [Fact]
    public void Etb_NoCreaturesAnywhere_IsNoOp()
    {
        var bringer = BattlefieldCreature("Bringer", _alice);

        var act = () => BringerOfTheLastGiftFactory
            .BuildEtbEffect(bringer, new[] { _alice, _bob }, _zones)
            .Execute();

        act.Should().NotThrow();
        bringer.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // "if you cast it" gate (CR 603.7e) — only fires off a cast.
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TriggerEffect_NoOps_WhenNotCast()
    {
        // Bringer entered the battlefield via a non-cast path (WasCast=false,
        // the default). The wired ETB trigger's effect must short-circuit.
        var bringer = BringerOfTheLastGiftFactory.Create(_alice);
        bringer.SetController(_alice);
        bringer.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bringer);

        var aliceOther = BattlefieldCreature("Alice-Other", _alice);
        var aliceYard = GraveyardCreature("Alice-Yard", _alice);

        var etb = bringer.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // No cast → no sac, no return.
        aliceOther.Zone.Should().Be(ZoneType.Battlefield);
        aliceYard.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Etb_TriggerEffect_Fires_WhenCast()
    {
        // Bringer was cast (WasCast=true). The wired ETB trigger's effect
        // applies — but with no live GameContext it falls back to the
        // controller alone, which is enough to observe the gate opening.
        var bringer = BringerOfTheLastGiftFactory.Create(_alice);
        bringer.SetController(_alice);
        bringer.SetZone(ZoneType.Battlefield);
        bringer.SetWasCast(true);
        _alice.Zones.Battlefield.AddCard(bringer);

        var aliceOther = BattlefieldCreature("Alice-Other", _alice);
        var aliceYard = GraveyardCreature("Alice-Yard", _alice);

        var etb = bringer.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // Gate open: Alice's other creature sac'd, graveyard creature returned.
        bringer.Zone.Should().Be(ZoneType.Battlefield);
        aliceOther.Zone.Should().Be(ZoneType.Graveyard);
        aliceYard.Zone.Should().Be(ZoneType.Battlefield);
        aliceYard.Controller.Should().BeSameAs(_alice);
    }
}
