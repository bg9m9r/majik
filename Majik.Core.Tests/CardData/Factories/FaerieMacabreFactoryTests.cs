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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FaerieMacabreFactory"/> (Morningtide).
///
/// Covers:
/// - Identity ({1}{B}{B} Creature — Faerie Rogue 2/2).
/// - Flash + Flying keyword markers.
/// - Discard-self activated ability: cost is <see cref="DiscardSelfCost"/>;
///   activation gated to controller's Hand (CR 702.74a-style).
/// - Up-to-two target slot (CR 115.1b — MinTargets=0 / MaxTargets=2).
/// - On resolution exiles each chosen graveyard target to its owner's
///   exile zone.
/// - CR 608.2b — illegal-on-resolution skip (target no longer in
///   graveyard) is per-target; other legal targets still resolve.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "B")]
public class FaerieMacabreFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieMacabre_Identity_FaerieRogueTwoTwo()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);

        macabre.Name.Should().Be("Faerie Macabre");
        macabre.ManaCost.ToString().Should().Be("{1}{B}{B}");
        macabre.BasePower.Should().Be(2);
        macabre.BaseToughness.Should().Be(2);
        macabre.HasType(CardType.Creature).Should().BeTrue();
        macabre.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        macabre.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        macabre.Owner.Should().BeSameAs(_alice);
        macabre.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Keyword markers — Flash + Flying (CR 702.8 / 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieMacabre_HasFlashAndFlyingMarkers()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);

        var keywords = macabre.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    // -----------------------------------------------------------------------
    // Discard-self activated ability — cost + zone gate
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieMacabre_HasDiscardSelfActivatedAbility()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);

        var ability = macabre.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        ability.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1,
            "the printed cost is 'discard this card' (no mana)");
        ability.Costs.Should().HaveCount(1, "no other costs printed");
        ability.TargetRequests.Should().HaveCount(1);
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(0, "CR 115.1b — 'up to two' means MinTargets=0");
        req.MaxTargets.Should().Be(2);
    }

    /// <summary>
    /// CR 702.74a-style — DiscardSelfCost is payable only while the card
    /// is in the controller's hand. From any other zone the cost rejects.
    /// </summary>
    [Fact]
    public void FaerieMacabre_DiscardSelfCost_PayableOnlyFromHand()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);
        var discardCost = macabre.Abilities
            .OfType<ActivatedAbility>().Single()
            .Costs.OfType<DiscardSelfCost>().Single();

        // Not in hand → can't pay.
        discardCost.CanPay(_alice).Should().BeFalse(
            "Faerie Macabre is not in Alice's hand yet");

        // Put into hand → can pay.
        _alice.Zones.Hand.AddCard(macabre);
        macabre.SetZone(ZoneType.Hand);
        discardCost.CanPay(_alice).Should().BeTrue(
            "Faerie Macabre's discard-self ability activates from hand");
    }

    // -----------------------------------------------------------------------
    // Resolution — exile up to two graveyard cards (CR 115.1b)
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieMacabre_Resolve_ExilesTwoChosenGraveyardCards()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);

        // Seed two graveyards.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        var ponder = new Instant("Ponder", "{U}");
        ponder.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(ponder);
        ponder.SetZone(ZoneType.Graveyard);

        var ability = macabre.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { goyf, ponder },
        });

        ability.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().NotContain(goyf);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ponder);
        _bob.Zones.Exile.GetCards().Should().Contain(goyf);
        _alice.Zones.Exile.GetCards().Should().Contain(ponder);
        goyf.Zone.Should().Be(ZoneType.Exile);
        ponder.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void FaerieMacabre_Resolve_NoTargetsChosen_NoOp()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var ability = macabre.Abilities.OfType<ActivatedAbility>().Single();
        // No SetChosenTargets — CR 115.1b "up to two" allows zero picks.

        ability.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().Contain(bolt,
            "no targets chosen → no exile");
        _bob.Zones.Exile.GetCards().Should().NotContain(bolt);
    }

    /// <summary>
    /// CR 608.2b — illegal-on-resolution rechecks per-target. A target
    /// that left its graveyard before resolution is skipped; the other
    /// legal target still resolves.
    /// </summary>
    [Fact]
    public void FaerieMacabre_Resolve_IllegalTargetSkipped_OthersResolve()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        var ponder = new Instant("Ponder", "{U}");
        ponder.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(ponder);
        ponder.SetZone(ZoneType.Graveyard);

        // Move goyf out of the graveyard before resolution — illegal at
        // resolution. Per-target skip; ponder still resolves.
        _bob.Zones.Graveyard.RemoveCard(goyf);
        _bob.Zones.Hand.AddCard(goyf);
        goyf.SetZone(ZoneType.Hand);

        var ability = macabre.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { goyf, ponder },
        });

        ability.Resolve();

        // Goyf was illegal at resolution → not exiled, stays in hand.
        _bob.Zones.Hand.GetCards().Should().Contain(goyf);
        _bob.Zones.Exile.GetCards().Should().NotContain(goyf);

        // Ponder was legal → exiled.
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ponder);
        _alice.Zones.Exile.GetCards().Should().Contain(ponder);
    }

    /// <summary>
    /// CR 115.1b — "up to two" accepts a single pick. Trailing slots can
    /// be left empty; the lone chosen target still resolves.
    /// </summary>
    [Fact]
    public void FaerieMacabre_Resolve_OneTargetChosen_SingleExile()
    {
        var macabre = FaerieMacabreFactory.Create(_alice);

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        var ability = macabre.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { goyf },
        });

        ability.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(goyf);
        goyf.Zone.Should().Be(ZoneType.Exile);
    }
}
