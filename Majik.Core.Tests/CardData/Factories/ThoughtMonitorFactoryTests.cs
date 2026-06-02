using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ThoughtMonitorFactory"/>
/// (Modern Horizons 2, {6}{U}).
///
/// Artifact Creature — Construct 2/2. Oracle text (verified against
/// Scryfall):
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    Flying
///    When this creature enters, draw two cards."
///
/// Covers:
///   - Identity (dual Artifact + Creature, Construct subtype, {6}{U}, 2/2,
///     owner/controller).
///   - NamedCardFactory dispatch.
///   - Affinity for artifacts wires CostReductionAbility + Affinity keyword
///     marker; Frogmite-shape reduction at 0 / 3 / 6 / 10 artifacts (floor
///     at zero, {U} pip survives).
///   - Flying keyword (CR 702.9).
///   - ETB draw-two trigger: structural shape + draws two off the top.
/// </summary>
[Trait("Color", "U")]
public class ThoughtMonitorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Creature(name, "{0}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void ThoughtMonitor_Identity()
    {
        var c = ThoughtMonitorFactory.Create(_alice);

        c.Name.Should().Be("Thought Monitor");
        c.ManaCost.Should().Be("{6}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------------
    // Keyword markers
    // -------------------------------------------------------------------------

    [Fact]
    public void ThoughtMonitor_AbilityList_HasFlyingAndAffinityMarkers()
    {
        var c = ThoughtMonitorFactory.Create(_alice);
        var kw = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();

        kw.Should().Contain("Flying", "CR 702.9 — Flying marker");
        kw.Should().Contain("Affinity",
            "CR 702.40 — Affinity-for-artifacts discoverability marker");
    }

    [Fact]
    public void ThoughtMonitor_HasFlying()
    {
        var c = ThoughtMonitorFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue(
            "Thought Monitor prints Flying (CR 702.9).");
    }

    // -------------------------------------------------------------------------
    // Affinity for artifacts (CR 702.40 / CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void Affinity_NoArtifacts_FullPrintedCost()
    {
        var monitor = ThoughtMonitorFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(monitor);
        monitor.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(monitor, _alice);

        effective.Generic.Should().Be(6, "no artifacts → full {6} generic");
        effective.Blue.Should().Be(1, "the {U} pip is unaffected by Affinity (CR 117.7c)");
        effective.TotalValue.Should().Be(7);
    }

    [Fact]
    public void Affinity_ThreeArtifacts_GenericThree_PlusBlue()
    {
        var monitor = ThoughtMonitorFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(monitor);
        monitor.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(monitor, _alice);

        effective.Generic.Should().Be(3, "{6} reduced by 3 → {3}");
        effective.Blue.Should().Be(1);
        effective.TotalValue.Should().Be(4);
    }

    [Fact]
    public void Affinity_SixArtifacts_OneBlueOnly()
    {
        // The headline Affinity-blue dream: six artifacts → cast for {U}.
        var monitor = ThoughtMonitorFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(monitor);
        monitor.SetZone(ZoneType.Hand);

        for (var i = 0; i < 6; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(monitor, _alice);

        effective.Generic.Should().Be(0, "{6} reduced by 6 → {0}");
        effective.Blue.Should().Be(1, "{U} pip is unaffected (CR 117.7c)");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Affinity_TenArtifacts_FloorAtZero_BlueRemains()
    {
        var monitor = ThoughtMonitorFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(monitor);
        monitor.SetZone(ZoneType.Hand);

        for (var i = 0; i < 10; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(monitor, _alice);

        effective.Generic.Should().Be(0, "floor-at-zero (CR 117.7c) — never negative");
        effective.Blue.Should().Be(1, "{U} pip always survives");
    }

    // -------------------------------------------------------------------------
    // ETB draw two (CR 603.6)
    // -------------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_IsStructurallyPresent()
    {
        var monitor = ThoughtMonitorFactory.Create(_alice);

        var triggers = monitor.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "Thought Monitor prints one triggered ability — the ETB draw-two.");
        triggers[0].Source.Should().BeSameAs(monitor);
        triggers[0].Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EtbTrigger_DrawsTwoCardsFromTopOfLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        var c2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3"); // remains in library

        var monitor = ThoughtMonitorFactory.Create(_alice);
        var trigger = monitor.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });
        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly two cards were drawn off the top");
    }
}
