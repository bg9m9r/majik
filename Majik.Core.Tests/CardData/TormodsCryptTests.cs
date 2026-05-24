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
/// Tests for <see cref="TormodsCryptFactory"/> — Artifact {0} with one
/// activated ability:
///   "{T}, Sacrifice Tormod's Crypt: Exile all cards from target
///    player's graveyard."
///
/// Covers:
/// - Card identity (Artifact, {0}, owner / controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: tap + sacrifice costs, 1..1 player target.
/// - Resolution: target player's graveyard → exile (all cards), self-sac
///   moves the Crypt to its owner's graveyard.
/// - Empty graveyard target → clean no-op (CR 608.2b).
/// </summary>
public class TormodsCryptTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TormodsCrypt_IsArtifact_WithZeroManaCost()
    {
        var crypt = TormodsCryptFactory.Create(_alice);

        crypt.HasType(CardType.Artifact).Should().BeTrue();
        crypt.Name.Should().Be("Tormod's Crypt");
        crypt.ManaCost.Should().Be("{0}");
        crypt.Owner.Should().BeSameAs(_alice);
        crypt.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TormodsCrypt()
    {
        var card = NamedCardFactory.Create("Tormod's Crypt", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Tormod's Crypt");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TormodsCrypt_HasOneActivatedAbility_WithTapAndSacCosts()
    {
        var crypt = TormodsCryptFactory.Create(_alice);

        var ability = crypt.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the activation requires {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the activation requires sacrificing Tormod's Crypt");
    }

    [Fact]
    public void TormodsCrypt_TargetRequest_IsOnePlayer()
    {
        var crypt = TormodsCryptFactory.Create(_alice);

        var ability = crypt.Abilities.OfType<ActivatedAbility>().Single();

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("player");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ExilesEveryCard_FromTargetPlayersGraveyard()
    {
        // Bob has three cards in graveyard. Alice activates {T}, Sac
        // targeting Bob: all three are exiled and the Crypt is sacrificed.
        var card1 = new Card("Dead Spell 1", "{1}");
        var card2 = new Card("Dead Spell 2", "{2}");
        var card3 = new Card("Dead Spell 3", "{3}");
        _bob.Zones.Graveyard.AddCard(card1);
        card1.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(card2);
        card2.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(card3);
        card3.SetZone(ZoneType.Graveyard);

        var crypt = TormodsCryptFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(crypt);
        crypt.SetZone(ZoneType.Battlefield);

        var ability = crypt.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        // Every card in Bob's graveyard is now in exile.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "all cards from the target player's graveyard are exiled");
        _bob.Zones.Exile.GetCards().Should().BeEquivalentTo(new[] { card1, card2, card3 });
        card1.Zone.Should().Be(ZoneType.Exile);
        card2.Zone.Should().Be(ZoneType.Exile);
        card3.Zone.Should().Be(ZoneType.Exile);

        // Self-sac: the Crypt is in Alice's graveyard.
        crypt.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(crypt);
        _alice.Zones.Graveyard.GetCards().Should().Contain(crypt);
    }

    [Fact]
    public void Resolve_EmptyTargetGraveyard_IsNoOp_ButCryptStillSacrificed()
    {
        // CR 608.2b — the target player has an empty graveyard. The
        // ability still resolves; no cards move (no-op), but the Crypt
        // is still sacrificed because the cost was paid.
        var crypt = TormodsCryptFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(crypt);
        crypt.SetZone(ZoneType.Battlefield);

        var ability = crypt.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        // Bob's graveyard / exile remain empty.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().BeEmpty();

        // Crypt still sacrificed.
        crypt.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(crypt);
    }

    [Fact]
    public void Resolve_OtherPlayersGraveyards_AreUntouched()
    {
        // Confirms scoping: Alice targets Bob, so Alice's own graveyard
        // is not affected.
        var aliceCard = new Card("Alice's Spell", "{1}");
        _alice.Zones.Graveyard.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Graveyard);

        var bobCard = new Card("Bob's Spell", "{1}");
        _bob.Zones.Graveyard.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Graveyard);

        var crypt = TormodsCryptFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(crypt);
        crypt.SetZone(ZoneType.Battlefield);

        var ability = crypt.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        // Bob's graveyard swept clean; Alice's untouched.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().Contain(bobCard);

        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCard,
            "Alice's graveyard is not the target — should remain intact " +
            "(modulo the sacrificed Crypt itself)");
        _alice.Zones.Exile.GetCards().Should().NotContain(aliceCard);
    }
}
