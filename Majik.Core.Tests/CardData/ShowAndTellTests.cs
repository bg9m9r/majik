using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Show and Tell (Urza's Saga, {2}{U}, Sorcery).
///
/// Oracle text:
///   "Each player may put an artifact, creature, enchantment, or land card
///    from their hand onto the battlefield."
///
/// Covers:
///   - Card identity (Sorcery, {2}{U}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve: each player puts a creature card from hand → battlefield
///     (default deterministic first-permanent picker).
///   - Decline: a player whose custom picker returns null does NOT put
///     anything onto the battlefield.
///   - Eligibility: an Instant in hand is filtered out — Show and Tell
///     only puts artifact / creature / enchantment / land permanents.
/// </summary>
public class ShowAndTellTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ShowAndTell_IsSorcery_AtCost2U()
    {
        var s = ShowAndTellFactory.Create(_alice);

        s.Name.Should().Be("Show and Tell");
        s.ManaCost.Should().Be("{2}{U}");
        s.HasType(CardType.Sorcery).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ShowAndTell()
    {
        var card = NamedCardFactory.Create("Show and Tell", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Show and Tell");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — happy path: each player puts a creature card → battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_EachPlayerPutsCreature_FromHand_ToBattlefield()
    {
        // Each player has a creature card in hand. The default picker
        // takes the first Permanent in hand — both will be put onto the
        // battlefield under their owner's control.
        var aliceCreature = new Creature("Emrakul-ish", "15", 15, 15);
        aliceCreature.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(aliceCreature);
        aliceCreature.SetZone(ZoneType.Hand);

        var bobCreature = new Creature("Griselbrand-ish", "4BBBB", 7, 7);
        bobCreature.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobCreature);
        bobCreature.SetZone(ZoneType.Hand);

        var effects = ShowAndTellFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Alice's creature: hand → battlefield, controlled by Alice.
        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "Alice's creature was put onto the battlefield (CR 113.6c / 117.1a)");
        _alice.Zones.Hand.GetCards().Should().NotContain(aliceCreature);
        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceCreature);
        aliceCreature.Controller.Should().BeSameAs(_alice,
            "the card is controlled by its hand-owner (CR 110.2a)");

        // Bob's creature: hand → battlefield, controlled by Bob.
        bobCreature.Zone.Should().Be(ZoneType.Battlefield,
            "Bob's creature was put onto the battlefield (CR 113.6c / 117.1a)");
        _bob.Zones.Hand.GetCards().Should().NotContain(bobCreature);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobCreature);
        bobCreature.Controller.Should().BeSameAs(_bob);
    }

    // -----------------------------------------------------------------------
    // Resolve — decline path: custom picker returns null for a player
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PlayerDeclines_PutsNothingOntoBattlefield()
    {
        // Alice picks, Bob declines (his picker returns null). Bob's card
        // must stay in hand — the "may" decline clause is a legal no-op
        // (CR 605.1 / 117.x).
        var aliceCreature = new Creature("Sneak Attacker", "2R", 3, 3);
        aliceCreature.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(aliceCreature);
        aliceCreature.SetZone(ZoneType.Hand);

        var bobCreature = new Creature("Decliner", "U", 1, 1);
        bobCreature.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobCreature);
        bobCreature.SetZone(ZoneType.Hand);

        Permanent? Picker(Player pl, IReadOnlyList<Permanent> cands)
            => ReferenceEquals(pl, _bob) ? null : cands.FirstOrDefault();

        var effects = ShowAndTellFactory.BuildResolveEffect(
            new[] { _alice, _bob },
            zoneService: null,
            picker: Picker);
        foreach (var e in effects) e.Execute();

        // Alice put hers down.
        aliceCreature.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceCreature);

        // Bob declined — his creature stays in hand.
        bobCreature.Zone.Should().Be(ZoneType.Hand,
            "Bob declined the 'may' (CR 605.1 / 117.x) so nothing moves for him");
        _bob.Zones.Hand.GetCards().Should().Contain(bobCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobCreature);
    }

    // -----------------------------------------------------------------------
    // Eligibility — instant in hand is NOT a permanent card (CR 110.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_InstantInHand_IsNotEligible()
    {
        // Alice's hand: ONLY an Instant. Show and Tell restricts to
        // artifact / creature / enchantment / land cards (a strict subset
        // of Permanent in our card hierarchy), so the Instant must be
        // filtered out by OfType<Permanent>() and Alice's resolve is a
        // no-op even though her hand is non-empty.
        var counterspell = new Instant("Counterspell-ish", "UU");
        counterspell.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(counterspell);
        counterspell.SetZone(ZoneType.Hand);

        var effects = ShowAndTellFactory.BuildResolveEffect(new[] { _alice });
        foreach (var e in effects) e.Execute();

        counterspell.Zone.Should().Be(ZoneType.Hand,
            "an Instant card is not a Permanent — Show and Tell can't put it onto the battlefield");
        _alice.Zones.Hand.GetCards().Should().Contain(counterspell);
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "no eligible permanent card in hand = no-op (CR 605.1 / 117.x)");
    }

    // -----------------------------------------------------------------------
    // Eligibility — variety: artifact / enchantment / land all eligible
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PermanentTypes_AreAllEligible()
    {
        // Three players with three different permanent-card types in hand:
        // artifact (Alice), enchantment (Bob), land (third). All three
        // must be put onto the battlefield by the default picker.
        var carol = new Player("Carol", 20);

        var artifact = new Artifact("Test Artifact", "3");
        artifact.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(artifact);
        artifact.SetZone(ZoneType.Hand);

        var enchant = new Enchantment("Test Enchantment", "2W");
        enchant.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(enchant);
        enchant.SetZone(ZoneType.Hand);

        var land = new Land("Test Land");
        land.SetOwner(carol);
        carol.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var effects = ShowAndTellFactory.BuildResolveEffect(new[] { _alice, _bob, carol });
        foreach (var e in effects) e.Execute();

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "an artifact card in hand IS a permanent card eligible for Show and Tell");
        enchant.Zone.Should().Be(ZoneType.Battlefield,
            "an enchantment card in hand IS a permanent card eligible for Show and Tell");
        land.Zone.Should().Be(ZoneType.Battlefield,
            "a land card in hand IS a permanent card eligible for Show and Tell");
    }
}
