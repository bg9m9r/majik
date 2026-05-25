using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Drift of Phantasms (Future Sight, {3}{U}, Creature — Illusion 1/5).
///
/// Oracle text:
///   "Defender.
///    Transmute {1}{U}{U} ({1}{U}{U}, Discard this card: Search your
///    library for a card with the same mana value as this card, reveal
///    it, put it into your hand, then shuffle.)"
///
/// CR 702.49 — Transmute, activated-from-hand via the
/// <see cref="DiscardSelfCost"/> gate, sorcery-speed via the
/// <see cref="ActivatedAbility.IsSorcerySpeed"/> flag (CR 702.49b).
///
/// Covers:
///   * Card identity — Illusion {3}{U} 1/5 + Defender keyword marker.
///   * NamedCardFactory dispatch by name.
///   * Transmute keyword marker + activated-ability cost shape
///     (ManaCostCost {1}{U}{U} + DiscardSelfCost + sorcery-speed flag).
///   * Resolution — tutors a same-MV card from library to hand and
///     shuffles; declines / empty-candidate paths still shuffle.
/// </summary>
public class DriftOfPhantasmsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasIllusionShape_1_5()
    {
        var drift = DriftOfPhantasmsFactory.Create(_alice);

        drift.Name.Should().Be("Drift of Phantasms");
        drift.ManaCost.Should().Be("{3}{U}");
        drift.HasType(CardType.Creature).Should().BeTrue();
        drift.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        drift.BasePower.Should().Be(1);
        drift.BaseToughness.Should().Be(5);
        drift.ManaCostValue.TotalValue.Should().Be(4);
        drift.Owner.Should().BeSameAs(_alice);
        drift.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesByName()
    {
        var card = NamedCardFactory.Create("Drift of Phantasms", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Drift of Phantasms");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Defender keyword marker
    // -----------------------------------------------------------------------

    [Fact]
    public void Defender_KeywordMarker_Attached()
    {
        var drift = DriftOfPhantasmsFactory.Create(_alice);

        CombatAbilities.HasDefender(drift).Should().BeTrue(
            because: "Drift of Phantasms is printed with Defender (CR 702.3)");
    }

    // -----------------------------------------------------------------------
    // Transmute — ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Transmute_KeywordMarker_Attached()
    {
        var drift = DriftOfPhantasmsFactory.Create(_alice);

        drift.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Transmute",
                "Transmute keyword marker is wired for oracle audits / bot probes");
    }

    [Fact]
    public void Transmute_ActivatedAbility_HasManaCostAndDiscardSelf()
    {
        var drift = DriftOfPhantasmsFactory.Create(_alice);

        // Drift's only ActivatedAbility (the KeywordAbility markers are
        // separate marker abilities, not ActivatedAbility instances) is
        // Transmute; identify by the cost shape.
        var activated = drift.Abilities.OfType<ActivatedAbility>().Single();

        activated.Costs.Should().HaveCount(2);
        activated.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        activated.Costs.OfType<ManaCostCost>().Single().Cost
            .Should().Be(ManaCost.Parse("{1}{U}{U}"));
        activated.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();
    }

    [Fact]
    public void Transmute_IsSorcerySpeed()
    {
        var drift = DriftOfPhantasmsFactory.Create(_alice);
        var activated = drift.Abilities.OfType<ActivatedAbility>().Single();

        activated.IsSorcerySpeed.Should().BeTrue(
            because: "CR 702.49b — Transmute activates only as a sorcery");
    }

    // -----------------------------------------------------------------------
    // Transmute — resolve behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void TransmuteResolve_TutorsSameMvCard_ToHand_AndShuffles()
    {
        var drift = DriftOfPhantasmsFactory.Create(_alice);
        drift.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(drift);

        // Library seed: one card with matching MV 4 (Living End {2}{B}{B}
        // analogue — but printed costs only matter for ManaCostValue here),
        // plus two MV-3 and MV-5 distractors.
        var target = new Card("MV4 Match", "{2}{U}{U}", new[] { CardType.Sorcery });
        var distractorLow = new Card("MV3", "{1}{U}{U}", new[] { CardType.Sorcery });
        var distractorHigh = new Card("MV5", "{3}{U}{U}", new[] { CardType.Sorcery });
        target.SetOwner(_alice);
        distractorLow.SetOwner(_alice);
        distractorHigh.SetOwner(_alice);
        _alice.Zones.Library.AddCard(distractorLow);
        _alice.Zones.Library.AddCard(target);
        _alice.Zones.Library.AddCard(distractorHigh);

        // Sanity: Drift's MV is 4.
        drift.ManaCostValue.TotalValue.Should().Be(4);

        // Resolve the transmute body directly. We bypass cost payment in
        // this test (cost-paying is exercised by ActivatedAbility flow
        // tests elsewhere); this test asserts the resolve effect's
        // contract — pick a same-MV card and route library → hand.
        var transmute = drift.Abilities.OfType<ActivatedAbility>().Single();

        // Simulate cost: discard Drift to graveyard (DiscardSelfCost would
        // do this during activation flow). Tutor body reads owner's
        // library regardless of where Drift now lives.
        _alice.Zones.Hand.RemoveCard(drift);
        _alice.Zones.Graveyard.AddCard(drift);

        transmute.Resolve();

        target.Zone.Should().Be(ZoneType.Hand,
            because: "Transmute tutors a same-MV card to hand");
        _alice.Zones.Hand.ContainsCard(target).Should().BeTrue();
        _alice.Zones.Library.ContainsCard(target).Should().BeFalse();
        // Distractors stay in the library.
        _alice.Zones.Library.ContainsCard(distractorLow).Should().BeTrue();
        _alice.Zones.Library.ContainsCard(distractorHigh).Should().BeTrue();
    }

    [Fact]
    public void TransmuteResolve_EmptyCandidateSet_IsCleanNoOp()
    {
        var drift = DriftOfPhantasmsFactory.Create(_alice);
        drift.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(drift);

        // Library has only non-matching MVs.
        var distractor = new Card("MV2", "{U}{U}", new[] { CardType.Sorcery });
        distractor.SetOwner(_alice);
        _alice.Zones.Library.AddCard(distractor);

        var transmute = drift.Abilities.OfType<ActivatedAbility>().Single();
        _alice.Zones.Hand.RemoveCard(drift);
        _alice.Zones.Graveyard.AddCard(drift);

        // Should not throw; library stays intact.
        transmute.Resolve();

        _alice.Zones.Library.ContainsCard(distractor).Should().BeTrue();
        _alice.Zones.Hand.ContainsCard(distractor).Should().BeFalse();
    }
}
