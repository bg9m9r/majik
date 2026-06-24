using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SaltRoadPackbeastFactory"/> ({5}{W}).
///
/// Creature — Beast 4/3. Oracle text (verified against Scryfall):
///   "Affinity for creatures (This spell costs {1} less to cast for each
///    creature you control.)
///    When this creature enters, draw a card."
///
/// Covers:
///   - Identity (Creature — Beast, {5}{W}, 4/3, owner/controller).
///   - Affinity for creatures wires CostReductionAbility + Affinity keyword
///     marker; reduction at 0 / 3 / 5 / 10 controlled creatures (floor at
///     zero, {W} pip survives).
///   - ETB draw-a-card trigger: structural shape + draws one off the top.
/// </summary>
[Trait("Color", "W")]
public class SaltRoadPackbeastFactoryTests
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
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void SaltRoadPackbeast_Identity()
    {
        var c = SaltRoadPackbeastFactory.Create(_alice);

        c.Name.Should().Be("Salt Road Packbeast");
        c.ManaCost.Should().Be("{5}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SaltRoadPackbeast_AbilityList_HasAffinityMarker()
    {
        var c = SaltRoadPackbeastFactory.Create(_alice);

        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "the Affinity-for-creatures cost reducer is attached");
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Affinity",
                "CR 702.40 — Affinity-for-creatures discoverability marker");
    }

    // -------------------------------------------------------------------------
    // Affinity for creatures (CR 702.40 / CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void Affinity_NoCreatures_FullPrintedCost()
    {
        var beast = SaltRoadPackbeastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(beast);
        beast.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(beast, _alice);

        effective.Generic.Should().Be(5, "no creatures → full {5} generic");
        effective.White.Should().Be(1, "the {W} pip is unaffected by Affinity (CR 117.7c)");
        effective.TotalValue.Should().Be(6);
    }

    [Fact]
    public void Affinity_ThreeCreatures_GenericTwo_PlusWhite()
    {
        var beast = SaltRoadPackbeastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(beast);
        beast.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++)
        {
            PutOnBattlefield(_alice, new Creature($"Bear {i}", "{0}", 2, 2));
        }

        var effective = CostReduction.GetEffectiveCost(beast, _alice);

        effective.Generic.Should().Be(2, "{5} reduced by 3 → {2}");
        effective.White.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Affinity_FiveCreatures_OneWhiteOnly()
    {
        // The headline Affinity dream: five creatures → cast for {W}.
        var beast = SaltRoadPackbeastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(beast);
        beast.SetZone(ZoneType.Hand);

        for (var i = 0; i < 5; i++)
        {
            PutOnBattlefield(_alice, new Creature($"Bear {i}", "{0}", 2, 2));
        }

        var effective = CostReduction.GetEffectiveCost(beast, _alice);

        effective.Generic.Should().Be(0, "{5} reduced by 5 → {0}");
        effective.White.Should().Be(1, "{W} pip is unaffected (CR 117.7c)");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Affinity_TenCreatures_FloorAtZero_WhiteRemains()
    {
        var beast = SaltRoadPackbeastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(beast);
        beast.SetZone(ZoneType.Hand);

        for (var i = 0; i < 10; i++)
        {
            PutOnBattlefield(_alice, new Creature($"Bear {i}", "{0}", 2, 2));
        }

        var effective = CostReduction.GetEffectiveCost(beast, _alice);

        effective.Generic.Should().Be(0, "floor-at-zero (CR 117.7c) — never negative");
        effective.White.Should().Be(1, "{W} pip always survives");
    }

    // -------------------------------------------------------------------------
    // ETB draw a card (CR 603.6)
    // -------------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_IsStructurallyPresent()
    {
        var beast = SaltRoadPackbeastFactory.Create(_alice);

        var triggers = beast.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "Salt Road Packbeast prints one triggered ability — the ETB draw-a-card.");
        triggers[0].Source.Should().BeSameAs(beast);
        triggers[0].Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EtbTrigger_DrawsOneCardFromTopOfLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        SeedLibraryCard(_alice, "Top2"); // remains in library

        var beast = SaltRoadPackbeastFactory.Create(_alice);
        var trigger = beast.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(c1);
        c1.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly one card was drawn off the top");
    }
}
