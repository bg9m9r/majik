using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="TheStoneBrainFactory"/> — Legendary Artifact {2}
/// (Kaldheim).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{2}, {T}, Exile The Stone Brain: Choose a card name. Search target
///    opponent's graveyard, hand, and library for up to four cards with that
///    name and exile them. That player shuffles, then draws a card for each
///    card exiled from their hand this way. Activate only as a sorcery."
///
/// Covers:
/// - Identity (Legendary Artifact, {2}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: single sorcery-speed <see cref="ActivatedAbility"/> with
///   {2} mana + {T} tap costs (exile-self handled in the resolve effect, same
///   posture as Renegade Map's sacrifice — the generic exile-as-cost payment
///   isn't a primitive yet).
/// - Resolution: exiles up to four copies of the chosen name from the target
///   opponent's graveyard, hand, and library; the opponent shuffles and draws
///   one card per card exiled from their HAND this way.
/// - Resolution caps the sweep at four cards (CR — "up to four").
/// - Resolution exiles The Stone Brain itself as a cost.
/// </summary>
public class TheStoneBrainTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Instant Bolt(Player owner, ZoneType zone)
    {
        var c = new Instant("Lightning Bolt", "{R}") { Owner = owner, Controller = owner };
        c.SetZone(zone);
        return c;
    }

    [Fact]
    public void TheStoneBrain_IsLegendaryArtifact_WithTwoManaCost()
    {
        var brain = TheStoneBrainFactory.Create(_alice);

        brain.HasType(CardType.Artifact).Should().BeTrue();
        brain.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        brain.Name.Should().Be("The Stone Brain");
        brain.ManaCost.Should().Be("{2}");
        brain.Owner.Should().BeSameAs(_alice);
        brain.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TheStoneBrain()
    {
        var card = NamedCardFactory.Create("The Stone Brain", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("The Stone Brain");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void TheStoneBrain_HasSingleActivatedAbility_NoManaAbilities()
    {
        var brain = TheStoneBrainFactory.Create(_alice);

        brain.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        brain.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Ability_IsSorcerySpeed_HasManaAndTapCosts()
    {
        var brain = TheStoneBrainFactory.Create(_alice);
        var ab = brain.Abilities.OfType<ActivatedAbility>().Single();

        ab.IsSorcerySpeed.Should().BeTrue(
            "the printed cost ends with \"Activate only as a sorcery\" (CR 117.1a / 307.5)");
        ab.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed cost includes a {2} mana pip");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap,
            "the ability costs {T}");
    }

    [Fact]
    public void Resolve_ExilesAllCopiesAcrossZones_OpponentDrawsForHandExiles_AndShuffles()
    {
        // Bob owns four Lightning Bolts: one in graveyard, one in hand, two in
        // library, plus a decoy Counterspell. Stone Brain names "Lightning
        // Bolt"; all four Bolts are exiled, Bob shuffles, and Bob draws one
        // card for the single HAND Bolt exiled (CR 120 — that player draws).
        var graveBolt = Bolt(_bob, ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(graveBolt);

        var handBolt = Bolt(_bob, ZoneType.Hand);
        _bob.Zones.Hand.AddCard(handBolt);

        var libBolt1 = Bolt(_bob, ZoneType.Library);
        _bob.Zones.Library.AddCard(libBolt1);
        var libBolt2 = Bolt(_bob, ZoneType.Library);
        _bob.Zones.Library.AddCard(libBolt2);

        // A fresh card sits beneath the Bolts so the post-exile draw has
        // something to find.
        var libTop = new Instant("Opt", "{U}") { Owner = _bob, Controller = _bob };
        libTop.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libTop);

        var decoy = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        decoy.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(decoy);

        var brain = TheStoneBrainFactory.Create(_alice, _bob, "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(brain);
        brain.SetZone(ZoneType.Battlefield);

        var handCountBefore = _bob.Zones.Hand.GetCards().Count();

        var ab = brain.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        graveBolt.Zone.Should().Be(ZoneType.Exile);
        handBolt.Zone.Should().Be(ZoneType.Exile);
        libBolt1.Zone.Should().Be(ZoneType.Exile);
        libBolt2.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should()
            .Contain(new ICard[] { graveBolt, handBolt, libBolt1, libBolt2 });

        decoy.Zone.Should().Be(ZoneType.Hand,
            "Counterspell shares no name with Lightning Bolt");

        // One Bolt was exiled from hand -> Bob draws exactly one card.
        // Hand started with handBolt + decoy. After: handBolt exiled (-1),
        // decoy stays, +1 drawn = handCountBefore.
        _bob.Zones.Hand.GetCards().Should().Contain(decoy);
        _bob.Zones.Hand.GetCards().Should().Contain(libTop,
            "Bob drew one card for the single hand Bolt exiled this way");
        _bob.Zones.Hand.GetCards().Count().Should().Be(handCountBefore);

        // The Stone Brain exiled itself as a cost.
        brain.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(brain);
    }

    [Fact]
    public void Resolve_CapsExileAtFourCopies()
    {
        // Six Bolts in Bob's library — only four may be exiled (CR "up to four").
        for (var i = 0; i < 6; i++)
        {
            var b = Bolt(_bob, ZoneType.Library);
            _bob.Zones.Library.AddCard(b);
        }

        var brain = TheStoneBrainFactory.Create(_alice, _bob, "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(brain);
        brain.SetZone(ZoneType.Battlefield);

        var ab = brain.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _bob.Zones.Exile.GetCards().Count(c => c.Name == "Lightning Bolt")
            .Should().Be(4, "the ability exiles up to four cards with the chosen name");
        _bob.Zones.Library.GetCards().Count(c => c.Name == "Lightning Bolt")
            .Should().Be(2, "the remaining two Bolts stay in the library");
    }

    [Fact]
    public void Resolve_NoMatchingCards_StillExilesSelf_NoDraw()
    {
        var brain = TheStoneBrainFactory.Create(_alice, _bob, "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(brain);
        brain.SetZone(ZoneType.Battlefield);

        var handBefore = _bob.Zones.Hand.GetCards().Count();

        var ab = brain.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _bob.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "no hand cards were exiled, so no cards are drawn");
        brain.Zone.Should().Be(ZoneType.Exile);
    }
}
