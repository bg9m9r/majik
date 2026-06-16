using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Revolutionist (Shadows over Innistrad, {5}{R}
/// Creature — Human Wizard 3/3).
///
/// Oracle text (verified against Scryfall 2026-06-16):
///   "When this creature enters, return target instant or sorcery card from
///    your graveyard to your hand.
///    Madness {3}{R}"
///
/// Covers:
///   - Card identity: name, types, subtypes (Human + Wizard), P/T, mana cost,
///     mana value 6, owner/controller.
///   - ETB trigger shape: exactly one ETB TriggeredAbility with a 1..1
///     TargetRequest for "instant or sorcery card in your graveyard", with a
///     live CandidateGatherer scoped to the controller's graveyard.
///   - ETB effect: instant card from graveyard moves to hand.
///   - ETB effect: sorcery card from graveyard moves to hand.
///   - ETB effect: non-instant/sorcery card is NOT a legal target (no-ops if
///     illegally chosen).
///   - ETB effect: empty graveyard — no-op (no crash).
///   - Madness {3}{R} is catalogued intrinsically (CR 702.35).
/// </summary>
[Trait("Color", "R")]
public class RevolutionistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Card identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Revolutionist_Identity_HumanWizard_3_3_At5R()
    {
        var rev = RevolutionistFactory.Create(_alice);

        rev.Name.Should().Be("Revolutionist");
        rev.ManaCost.Should().Be("{5}{R}");
        rev.HasType(CardType.Creature).Should().BeTrue();
        rev.HasSubtype(CardSubtype.Human).Should().BeTrue("Revolutionist is a Human");
        rev.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Revolutionist is a Wizard");
        rev.BasePower.Should().Be(3);
        rev.BaseToughness.Should().Be(3);
        rev.Owner.Should().BeSameAs(_alice);
        rev.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Revolutionist_ManaValue_IsSix()
    {
        var rev = RevolutionistFactory.Create(_alice);
        // {5}{R} = mana value 6 (CR 202.3).
        rev.ManaCostValue.TotalValue.Should().Be(6,
            "CR 202.3 — {5}{R} has mana value 6");
    }

    // ── ETB trigger shape ─────────────────────────────────────────────────────

    [Fact]
    public void Revolutionist_HasExactlyOneTriggeredAbility_TheEtb()
    {
        var rev = RevolutionistFactory.Create(_alice);

        rev.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "only the ETB graveyard-recur trigger is attached");
    }

    [Fact]
    public void Revolutionist_EtbTrigger_Shape_SingleTarget_InstantOrSorcery_InGraveyard()
    {
        var rev = RevolutionistFactory.Create(_alice);

        var etb = rev.Abilities.OfType<TriggeredAbility>().Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery",
            "target is restricted to instant or sorcery card type");
        req.Description.Should().Contain("graveyard",
            "target must be in the graveyard");
        req.CandidateGatherer.Should().NotBeNull(
            "the instant-or-sorcery graveyard candidate pool is gathered live");
    }

    [Fact]
    public void Revolutionist_EtbTrigger_CandidateGatherer_OnlyInstantsAndSorceries_InControllersGraveyard()
    {
        var rev = RevolutionistFactory.Create(_alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var divination = new Sorcery("Divination", "2U") { Owner = _alice };
        divination.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(divination);

        // Noise — a creature card in the graveyard must NOT be a candidate.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        var req = rev.Abilities.OfType<TriggeredAbility>().Single().TargetRequests[0];
        var candidates = req.CandidateGatherer!(null!);

        candidates.Should().BeEquivalentTo(new object[] { bolt, divination },
            "only instant/sorcery cards in the controller's graveyard are legal targets");
    }

    // ── ETB effect: instant from graveyard → hand ─────────────────────────────

    [Fact]
    public void Revolutionist_EtbEffect_InstantInGraveyard_MovesToHand()
    {
        var rev = RevolutionistFactory.Create(_alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var etb = rev.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
        {
            new object[] { bolt },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        bolt.Zone.Should().Be(ZoneType.Hand,
            "ETB returns the chosen instant from graveyard to hand");
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bolt);
    }

    // ── ETB effect: sorcery from graveyard → hand ─────────────────────────────

    [Fact]
    public void Revolutionist_EtbEffect_SorceryInGraveyard_MovesToHand()
    {
        var rev = RevolutionistFactory.Create(_alice);

        var divination = new Sorcery("Divination", "2U") { Owner = _alice };
        divination.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(divination);

        var etb = rev.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
        {
            new object[] { divination },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        divination.Zone.Should().Be(ZoneType.Hand,
            "ETB returns the chosen sorcery from graveyard to hand");
        _alice.Zones.Hand.GetCards().Should().Contain(divination);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(divination);
    }

    // ── ETB effect: non-instant/sorcery card — no-op ─────────────────────────

    [Fact]
    public void Revolutionist_EtbEffect_NonInstantSorcery_DoesNotMoveToHand()
    {
        // CR 608.2b — if the target is not an instant or sorcery at
        // resolution, the effect no-ops (doesn't return a creature card).
        var rev = RevolutionistFactory.Create(_alice);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        var etb = rev.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "non-instant/sorcery target is rejected — card stays in graveyard");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── ETB effect: empty graveyard — no-op, no crash ─────────────────────────

    [Fact]
    public void Revolutionist_EtbEffect_EmptyGraveyard_NoOpNoCrash()
    {
        var rev = RevolutionistFactory.Create(_alice);

        var etb = rev.Abilities.OfType<TriggeredAbility>().Single();
        // No targets set — ChosenTargets defaults to empty.

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("empty target list should be silently skipped");

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── Madness {3}{R} is intrinsic (CR 702.35) ───────────────────────────────

    [Fact]
    public void Revolutionist_MadnessCost_IsCatalogued_3R()
    {
        // CR 702.35 — Madness works intrinsically via MadnessCatalog consulted
        // by the central discard funnel; no factory code needed.
        var rev = RevolutionistFactory.Create(_alice);
        MadnessCatalog.HasMadness(rev).Should().BeTrue(
            "Revolutionist has Madness {3}{R}");
        // {3}{R} = mana value 4 (CR 202.3).
        MadnessCatalog.CostFor(rev)!.TotalValue.Should().Be(4,
            "Madness {3}{R} has mana value 4");
    }
}
