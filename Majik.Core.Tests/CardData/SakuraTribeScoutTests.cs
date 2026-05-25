using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sakura-Tribe Scout (Champions of Kamigawa, {G}).
/// Creature — Snake Scout 1/1. "{T}: You may put a land card from your
/// hand onto the battlefield."
///
/// Covers:
///   - Identity (name, type Creature, P/T 1/1, Snake + Scout subtypes,
///     mana cost, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Activated ability shape: single ICost (tap-self) + one effect.
///   - Resolution with a land in hand puts the land onto the battlefield
///     and removes it from hand (deterministic no-agent fallback).
///   - Resolution with no land in hand is a clean no-op.
///   - Resolution with mixed hand picks the first land (deterministic).
///   - Resolution routes through ZoneService when supplied: published
///     <see cref="CardMovedEvent"/> Hand → Battlefield is observed by a
///     subscriber (this is the Amulet Titan engine bridge — Amulet of
///     Vigor / bounce-land ETB triggers fire off this event).
/// </summary>
public class SakuraTribeScoutTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SakuraTribeScout_Identity()
    {
        var c = SakuraTribeScoutFactory.Create(_alice);

        c.Name.Should().Be("Sakura-Tribe Scout");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SakuraTribeScout_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sakura-Tribe Scout", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sakura-Tribe Scout");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void SakuraTribeScout_HasSingleActivatedAbility_TapCost()
    {
        var c = SakuraTribeScoutFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().ContainSingle();
        // Single printed cost: {T}.
        activated[0].Costs.Should().ContainSingle();
        activated[0].Effects.Should().ContainSingle();
    }

    [Fact]
    public void Activate_LandInHand_NoZoneService_MovesLandToBattlefield()
    {
        // Hand: 1 Forest.
        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var scout = SakuraTribeScoutFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(scout);
        scout.SetZone(ZoneType.Battlefield);

        // Fire the activated ability's effect directly (cost-payment +
        // stack mechanics tested elsewhere — this is the resolution body).
        var activated = scout.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        // Forest moved hand → battlefield.
        _alice.Zones.Hand.GetCards().Should().NotContain(forest);
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Activate_NoLandInHand_IsCleanNoOp()
    {
        // Hand: only non-land cards.
        var bolt = new Sorcery("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bolt);
        bolt.SetZone(ZoneType.Hand);

        var scout = SakuraTribeScoutFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(scout);
        scout.SetZone(ZoneType.Battlefield);

        var activated = scout.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        // Bolt still in hand; battlefield only has the scout.
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Battlefield.GetCards().OfType<Land>().Should().BeEmpty();
    }

    [Fact]
    public void Activate_MixedHand_PicksFirstLand_Deterministic()
    {
        // Hand: Sorcery, then Forest, then Mountain. v1 first-land fallback
        // picks Forest (first land in iteration order).
        var bolt = new Sorcery("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bolt);
        bolt.SetZone(ZoneType.Hand);

        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var mountain = MakeBasicLand("Mountain", _alice, CardSubtype.Mountain);
        _alice.Zones.Hand.AddCard(mountain);
        mountain.SetZone(ZoneType.Hand);

        var scout = SakuraTribeScoutFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(scout);
        scout.SetZone(ZoneType.Battlefield);

        var activated = scout.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        // Forest moved; mountain still in hand.
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Hand.GetCards().Should().Contain(mountain);
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Hand.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void Activate_WithZoneService_PublishesCardMovedEvent()
    {
        // Amulet Titan bridge: the move must publish CardMovedEvent so
        // ETB triggers + replacements on the played land fire. Subscribe
        // a probe and assert the event fires with the right zones.
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        CardMovedEvent? observed = null;
        bus.Subscribe<CardMovedEvent>(e =>
        {
            // Filter for the Forest move (the scout itself isn't moved).
            if (e.Card.Name == "Forest") observed = e;
        });

        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var scout = SakuraTribeScoutFactory.Create(_alice, zones);
        _alice.Zones.Battlefield.AddCard(scout);
        scout.SetZone(ZoneType.Battlefield);

        var activated = scout.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        observed.Should().NotBeNull("the Forest's hand→battlefield move must publish CardMovedEvent");
        observed!.FromZone.Should().Be(ZoneType.Hand);
        observed.ToZone.Should().Be(ZoneType.Battlefield);
        forest.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }
}
