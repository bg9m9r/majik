using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ScrapworkMuttFactory"/> — Scrapwork Mutt
/// ({2}, Artifact Creature — Dog 2/1, The Brothers' War).
///
/// Oracle text (Scryfall verified):
///   "When this creature enters, you may discard a card. If you do, draw a card.
///    Unearth {1}{R} ({1}{R}: Return this card from your graveyard to the
///    battlefield. It gains haste. Exile it at the beginning of the next end
///    step or if it would leave the battlefield. Unearth only as a sorcery.)"
///
/// Covers:
/// - Identity (name, Artifact + Creature types, Dog subtype, P/T 2/1, {2}).
/// - NamedCardFactory dispatch.
/// - ETB loot: agent-less default loots (discard last card, draw one).
/// - ETB loot: empty hand → no discard, no draw.
/// - ETB loot: agent declines the "may" → no discard, no draw.
/// - Unearth {1}{R}: sorcery-speed activated ability with a {1}{R} mana cost.
/// - Unearth resolve: returns card from graveyard → battlefield, grants haste
///   (clears summoning sickness) (CR 702.84).
/// </summary>
public class ScrapworkMuttFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_ArtifactCreatureDog_TwoOne_CostTwo()
    {
        var c = ScrapworkMuttFactory.Create(_alice);

        c.Name.Should().Be("Scrapwork Mutt");
        c.Should().BeOfType<Creature>();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "CR 301.1 / 302.1 — Scrapwork Mutt is an Artifact Creature");
        c.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.ManaCost.Should().Be("{2}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Scrapwork Mutt", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Scrapwork Mutt");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        c.ManaCost.Should().Be("{2}");
    }

    // -----------------------------------------------------------------------
    // ETB loot — CR 603.1 / CR 117.x / CR 701.16 / CR 121.1
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbLoot_AgentLess_DefaultsToLoot_DiscardsLastCard_DrawsOne()
    {
        var mutt = ScrapworkMuttFactory.Create(_alice);

        var inHand = new Instant("Lightning Bolt", "{R}");
        inHand.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        var topOfLibrary = new Instant("Dark Ritual", "{B}");
        topOfLibrary.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topOfLibrary);
        topOfLibrary.SetZone(ZoneType.Library);

        ExecuteEtb(mutt);

        inHand.Zone.Should().Be(ZoneType.Graveyard, "the looted card is discarded");
        _alice.Zones.Graveyard.GetCards().Should().Contain(inHand);
        topOfLibrary.Zone.Should().Be(ZoneType.Hand,
            "CR 121.1 — 'If you do, draw a card' draws the top of library");
        _alice.Zones.Hand.GetCards().Should().Contain(topOfLibrary);
    }

    [Fact]
    public void EtbLoot_EmptyHand_NoDiscard_NoDraw()
    {
        var mutt = ScrapworkMuttFactory.Create(_alice);

        var topOfLibrary = new Instant("Dark Ritual", "{B}");
        topOfLibrary.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topOfLibrary);
        topOfLibrary.SetZone(ZoneType.Library);

        ExecuteEtb(mutt);

        topOfLibrary.Zone.Should().Be(ZoneType.Library,
            "empty hand → 'you may discard' can't happen → 'If you do' draw does not fire");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void EtbLoot_AgentDeclines_NoDiscard_NoDraw()
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the "you may discard"

        var mutt = ScrapworkMuttFactory.Create(
            _alice, zoneService: null, triggers: null, agent: agent);

        var inHand = new Instant("Lightning Bolt", "{R}");
        inHand.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        var topOfLibrary = new Instant("Dark Ritual", "{B}");
        topOfLibrary.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topOfLibrary);
        topOfLibrary.SetZone(ZoneType.Library);

        ExecuteEtb(mutt);

        inHand.Zone.Should().Be(ZoneType.Hand, "the controller declined the 'may' (CR 117.x)");
        topOfLibrary.Zone.Should().Be(ZoneType.Library, "no discard → no draw");
    }

    // -----------------------------------------------------------------------
    // Unearth {1}{R} — CR 702.84
    // -----------------------------------------------------------------------

    [Fact]
    public void Unearth_HasSorcerySpeedActivatedAbility_WithOneRedOneGenericCost()
    {
        var mutt = ScrapworkMuttFactory.Create(_alice);
        var unearth = mutt.Abilities.OfType<ActivatedAbility>().Single();

        unearth.IsSorcerySpeed.Should().BeTrue("CR 702.84a — Unearth only as a sorcery");
        var mana = unearth.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1, "Unearth {1}{R} — one generic");
        mana.Red.Should().Be(1, "Unearth {1}{R} — one red");
        unearth.Source.Should().BeSameAs(mutt);
        unearth.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Unearth_Resolve_ReturnsFromGraveyard_GrantsHaste()
    {
        var bus = new EventBus();
        var zoneService = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mutt = ScrapworkMuttFactory.Create(_alice, zoneService, triggers, agent: null);
        _alice.Zones.Graveyard.AddCard(mutt);
        mutt.SetZone(ZoneType.Graveyard);

        var unearth = mutt.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in unearth.Effects) effect.Execute();

        mutt.Zone.Should().Be(ZoneType.Battlefield,
            "CR 702.84a — unearth returns the card from graveyard to battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(mutt);
        mutt.HasSummoningSickness.Should().BeFalse(
            "CR 702.84a / CR 702.10b — it gains haste, so it can attack the turn it returns");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ExecuteEtb(Creature mutt)
    {
        var etb = mutt.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();
    }
}
