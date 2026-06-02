using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TrollOfKhazadDumFactory"/> (LTR).
///
/// Covers:
/// - Identity ({5}{B} Creature — Troll 6/5).
/// - "Can't be blocked except by three or more creatures" (CR 509.1b)
///   via <see cref="BlockLegality.MinBlockersSatisfied"/>.
/// - Swampcycling {1} keyword markers (CR 702.32d — typed + generic).
/// - Cycling activated ability shape ({1} mana + DiscardSelfCost).
/// - Swampcycling end-to-end: pays {1}, discards self, tutors a Swamp
///   to hand, leaves non-Swamp cards in the library, publishes
///   <see cref="CardCycledEvent"/> on the bus.
/// - Cycling cost gate: DiscardSelfCost CanPay is hand-only.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "B")]
public class TrollOfKhazadDumFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TrollOfKhazadDum_Identity_Troll65BlackSixManaValue()
    {
        var card = TrollOfKhazadDumFactory.Create(_alice);

        card.Name.Should().Be("Troll of Khazad-dûm");
        card.ManaCost.ToString().Should().Be("{5}{B}");
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(5);
        card.ManaCostValue.TotalValue.Should().Be(6, "mana value = 5 generic + 1 black");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Troll).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // "Can't be blocked except by three or more creatures" (CR 509.1b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TrollOfKhazadDum_BlockRestriction_MarkerHasArgThree()
    {
        var troll = TrollOfKhazadDumFactory.Create(_alice);

        var marker = troll.Abilities
            .OfType<KeywordAbility>()
            .Single(k => k.Keyword == "CantBeBlockedExceptByMinBlockers");

        marker.Arg.Should().Be(3, "three or more blockers required");
    }

    [Fact]
    public void TrollOfKhazadDum_GetMinBlockerRestriction_ReturnsThree()
    {
        var troll = TrollOfKhazadDumFactory.Create(_alice);
        CombatAbilities.GetMinBlockerRestriction(troll).Should().Be(3);
    }

    [Fact]
    public void MinBlockersSatisfied_ZeroBlockers_IsLegal()
    {
        // CR 509.1b — "unblocked" (0 blockers) is always a legal declaration;
        // the restriction only governs which creatures may participate in
        // a block declaration, not whether the attacker may go unblocked.
        var troll = TrollOfKhazadDumFactory.Create(_alice);
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 0).Should().BeTrue();
    }

    [Fact]
    public void MinBlockersSatisfied_OneBlocker_IsIllegal()
    {
        var troll = TrollOfKhazadDumFactory.Create(_alice);
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 1).Should().BeFalse();
    }

    [Fact]
    public void MinBlockersSatisfied_TwoBlockers_IsIllegal()
    {
        var troll = TrollOfKhazadDumFactory.Create(_alice);
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 2).Should().BeFalse();
    }

    [Fact]
    public void MinBlockersSatisfied_ThreeBlockers_IsLegal()
    {
        var troll = TrollOfKhazadDumFactory.Create(_alice);
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 3).Should().BeTrue();
    }

    [Fact]
    public void MinBlockersSatisfied_FourBlockers_IsLegal()
    {
        var troll = TrollOfKhazadDumFactory.Create(_alice);
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 4).Should().BeTrue();
    }

    [Fact]
    public void MinBlockersSatisfied_PlainCreature_AlwaysSatisfied()
    {
        // Regression: creatures without the restriction should never
        // be rejected regardless of blocker count.
        var plain = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        BlockLegality.MinBlockersSatisfied(plain, blockerCount: 1).Should().BeTrue();
        BlockLegality.MinBlockersSatisfied(plain, blockerCount: 0).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Swampcycling ability shape — CR 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void TrollOfKhazadDum_HasSwampcyclingActivatedAbility_With1GenericAndDiscardSelf()
    {
        var card = TrollOfKhazadDumFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2, "swampcycling = {1} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1, "swampcycling {1} charges one generic");
    }

    // -----------------------------------------------------------------------
    // Swampcycling end-to-end — pays {1}, discards, tutors Swamp,
    // publishes CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void TrollOfKhazadDum_Swampcycling_EndToEnd_TutorsSwampAndPublishesCardCycledEvent()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var swamp = new Land(
            "Swamp",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        _alice.Zones.Library.AddCard(swamp);
        swamp.SetZone(ZoneType.Library);

        var noise = new Instant("Dark Ritual", "{B}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var troll = TrollOfKhazadDumFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(troll);
        troll.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("1"));

        var cycling = troll.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        troll.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(swamp,
            "Swampcycling tutors a Swamp card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(forest,
            "Swampcycling filters to Swamp subtype only");
        _alice.Zones.Hand.GetCards().Should().NotContain(noise,
            "Swampcycling filters to Swamp subtype only");
        swamp.Zone.Should().Be(ZoneType.Hand);

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(troll);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cycling cost gate — DiscardSelfCost CanPay is hand-only
    // -----------------------------------------------------------------------

    [Fact]
    public void TrollOfKhazadDum_Swampcycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var card = TrollOfKhazadDumFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}
