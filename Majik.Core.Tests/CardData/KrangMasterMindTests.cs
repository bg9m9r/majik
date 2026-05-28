using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="KrangMasterMindFactory"/>
/// (Universes Beyond: TMNT, {6}{U}{U}).
///
/// Legendary Artifact Creature — Utrom Warrior 1/4. Oracle text:
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    When Krang enters, if you have fewer than four cards in hand, draw
///    cards equal to the difference.
///    Krang gets +1/+0 for each other artifact you control."
///
/// Test categories:
///   1. Identity — name, types, subtypes, MV, P/T, owner/controller.
///   2. NamedCardFactory dispatch.
///   3. Affinity for artifacts — cost reduction at 0 / 3 / 7+ artifacts.
///   4. ETB hand-refill — 0 in hand → draw 4; 3 in hand → draw 1;
///      4+ in hand → no draw (intervening-if gates).
///   5. Variable power — N=0 other artifacts → power 1; N=1 → power 2;
///      N=5 → power 6.
/// </summary>
public class KrangMasterMindTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public KrangMasterMindTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private Artifact MakeArtifact(Player owner, string name = "Bauble")
    {
        var a = new Artifact(name, "{0}");
        PutOnBattlefield(owner, a);
        return a;
    }

    private Card MakeCardInHand(Player owner, string name = "Spell")
    {
        var c = new Card(name, "{0}", new[] { CardType.Instant });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private void AddLibraryCards(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Library{i}", "{0}", new[] { CardType.Instant });
            c.SetOwner(owner);
            c.SetController(owner);
            owner.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    private Creature WireKrang(Player owner)
    {
        var krang = KrangMasterMindFactory.Create(owner, _effects, _bus, triggers: null);
        krang.ActiveEffects = _effects;
        return krang;
    }

    // -----------------------------------------------------------------------
    // 1. Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Krang_Identity_NameAndCost()
    {
        var krang = KrangMasterMindFactory.Create(_alice);

        krang.Name.Should().Be("Krang, Master Mind");
        krang.ManaCost.Should().Be("{6}{U}{U}");
    }

    [Fact]
    public void Krang_Identity_Types()
    {
        var krang = KrangMasterMindFactory.Create(_alice);

        krang.HasType(CardType.Creature).Should().BeTrue("Krang is a Creature");
        krang.HasType(CardType.Artifact).Should().BeTrue("Krang is also an Artifact (CR 301.1 / 302.1)");
        krang.Supertypes.Should().Contain(CardSupertype.Legendary, "Krang is a legendary permanent");
    }

    [Fact]
    public void Krang_Identity_Subtypes()
    {
        var krang = KrangMasterMindFactory.Create(_alice);

        krang.HasSubtype(CardSubtype.Utrom).Should().BeTrue("Krang is an Utrom");
        krang.HasSubtype(CardSubtype.Warrior).Should().BeTrue("Krang is a Warrior");
    }

    [Fact]
    public void Krang_Identity_PowerToughness()
    {
        var krang = KrangMasterMindFactory.Create(_alice);

        krang.BasePower.Should().Be(1, "printed power is 1");
        krang.BaseToughness.Should().Be(4, "printed toughness is 4");
    }

    [Fact]
    public void Krang_Identity_OwnerController()
    {
        var krang = KrangMasterMindFactory.Create(_alice);

        krang.Owner.Should().BeSameAs(_alice);
        krang.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // 2. NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Krang_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Krang, Master Mind", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Krang, Master Mind");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Utrom).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "Affinity-for-artifacts cost reducer attached");
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Affinity",
                "Affinity keyword marker attached for bot discovery");
    }

    // -----------------------------------------------------------------------
    // 3. Affinity for artifacts (CR 702.40)
    //    Krang costs {6}{U}{U} — generic is 6; UU pips are preserved.
    //    With N artifacts, effective generic = max(0, 6 − N).
    // -----------------------------------------------------------------------

    [Fact]
    public void Affinity_ZeroArtifacts_FullGenericCost()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(krang);
        krang.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(krang, _alice);

        effective.Generic.Should().Be(6, "no artifacts → no Affinity discount");
    }

    [Fact]
    public void Affinity_ThreeArtifacts_GenericReducedByThree()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(krang);
        krang.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++) MakeArtifact(_alice, $"Art{i}");

        var effective = CostReduction.GetEffectiveCost(krang, _alice);

        effective.Generic.Should().Be(3, "{6} reduced by 3 → {3}");
    }

    [Fact]
    public void Affinity_SevenArtifacts_FloorAtZero()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(krang);
        krang.SetZone(ZoneType.Hand);

        for (var i = 0; i < 7; i++) MakeArtifact(_alice, $"Art{i}");

        var effective = CostReduction.GetEffectiveCost(krang, _alice);

        effective.Generic.Should().Be(0, "floor at 0 — never negative (CR 117.7c)");
    }

    // -----------------------------------------------------------------------
    // 4. ETB triggered ability — hand refill with intervening-if (CR 603.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_ZeroCardsInHand_DrawsFour()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        PutOnBattlefield(_alice, krang);

        // Library must have enough cards to draw.
        AddLibraryCards(_alice, 6);

        // Hand is empty — verify.
        _alice.Zones.Hand.GetCards().Count().Should().Be(0);

        // Manually fire the ETB effect (no TriggerManager in shape-only mode).
        var etbAbility = krang.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in etbAbility.Effects) eff.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(4,
            "4 − 0 = 4 cards drawn when hand is empty");
    }

    [Fact]
    public void Etb_ThreeCardsInHand_DrawsOne()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        PutOnBattlefield(_alice, krang);

        AddLibraryCards(_alice, 4);

        // Put 3 cards in hand.
        for (var i = 0; i < 3; i++) MakeCardInHand(_alice, $"Hand{i}");

        _alice.Zones.Hand.GetCards().Count().Should().Be(3);

        var etbAbility = krang.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in etbAbility.Effects) eff.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(4,
            "started with 3; 4 − 3 = 1 card drawn → total 4");
    }

    [Fact]
    public void Etb_FourCardsInHand_DrawsNothing()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        PutOnBattlefield(_alice, krang);

        AddLibraryCards(_alice, 4);

        // Exactly 4 cards in hand — threshold is "fewer than four".
        for (var i = 0; i < 4; i++) MakeCardInHand(_alice, $"Hand{i}");

        _alice.Zones.Hand.GetCards().Count().Should().Be(4);

        var etbAbility = krang.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in etbAbility.Effects) eff.Execute();

        // Hand should not change — intervening-if (< 4) fails at resolution.
        _alice.Zones.Hand.GetCards().Count().Should().Be(4,
            "4 cards in hand is NOT fewer than 4 — draw clause does not trigger (CR 603.4)");
    }

    [Fact]
    public void Etb_FiveCardsInHand_DrawsNothing()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        PutOnBattlefield(_alice, krang);

        AddLibraryCards(_alice, 2);

        for (var i = 0; i < 5; i++) MakeCardInHand(_alice, $"Hand{i}");

        _alice.Zones.Hand.GetCards().Count().Should().Be(5);

        var etbAbility = krang.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in etbAbility.Effects) eff.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(5,
            "5 cards in hand — ETB does not draw (CR 603.4 intervening-if)");
    }

    [Fact]
    public void Etb_InterveningIf_TrueWhenHandFewer()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        PutOnBattlefield(_alice, krang);

        // 0 cards in hand → fewer than 4 → true.
        var etbAbility = krang.Abilities.OfType<TriggeredAbility>().First();
        etbAbility.InterveningIf.Should().NotBeNull();
        etbAbility.InterveningIf!().Should().BeTrue(
            "controller has 0 cards in hand, which is fewer than 4");
    }

    [Fact]
    public void Etb_InterveningIf_FalseWhenHandAtThreshold()
    {
        var krang = KrangMasterMindFactory.Create(_alice);
        PutOnBattlefield(_alice, krang);

        for (var i = 0; i < 4; i++) MakeCardInHand(_alice, $"Hand{i}");

        var etbAbility = krang.Abilities.OfType<TriggeredAbility>().First();
        etbAbility.InterveningIf!().Should().BeFalse(
            "controller has exactly 4 cards in hand — NOT fewer than 4");
    }

    // -----------------------------------------------------------------------
    // 5. Variable power (Layer PT_Cda) — "+1/+0 for each other artifact"
    // -----------------------------------------------------------------------

    [Fact]
    public void Power_ZeroOtherArtifacts_IsOne()
    {
        var krang = WireKrang(_alice);
        _zones.MoveCard(krang, ZoneType.Library, ZoneType.Battlefield, _alice);

        // No other artifacts on battlefield — just Krang itself (which has
        // Artifact type but is excluded by "other").
        krang.Power.Should().Be(1, "1 + 0 other artifacts = 1");
        krang.Toughness.Should().Be(4, "toughness is always the printed 4");
    }

    [Fact]
    public void Power_OneOtherArtifact_IsTwo()
    {
        var krang = WireKrang(_alice);
        _zones.MoveCard(krang, ZoneType.Library, ZoneType.Battlefield, _alice);

        MakeArtifact(_alice, "OtherArt");

        krang.Power.Should().Be(2, "1 + 1 other artifact = 2");
    }

    [Fact]
    public void Power_FiveOtherArtifacts_IsSix()
    {
        var krang = WireKrang(_alice);
        _zones.MoveCard(krang, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (var i = 0; i < 5; i++) MakeArtifact(_alice, $"Art{i}");

        krang.Power.Should().Be(6, "1 + 5 other artifacts = 6");
    }

    [Fact]
    public void Power_OpponentArtifactsDoNotCount()
    {
        var krang = WireKrang(_alice);
        _zones.MoveCard(krang, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Opponent has 3 artifacts — should NOT count.
        for (var i = 0; i < 3; i++) MakeArtifact(_bob, $"BobArt{i}");

        krang.Power.Should().Be(1, "opponent's artifacts do not count for Krang's power");
    }

    [Fact]
    public void Power_KrangDoesNotCountItself()
    {
        var krang = WireKrang(_alice);
        _zones.MoveCard(krang, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Krang is an Artifact Creature but the ability says "other artifact".
        // With no OTHER artifacts, power must be 1.
        krang.Power.Should().Be(1, "Krang does not count itself (\"each OTHER artifact\")");
    }

    [Fact]
    public void Power_CdaInactiveOffBattlefield_ReturnsBasePower()
    {
        var krang = WireKrang(_alice);
        // Krang is in library (not battlefield) — CDA IsActive() returns false;
        // Creature.Power falls back to BasePower.
        krang.Zone.Should().NotBe(ZoneType.Battlefield);

        krang.Power.Should().Be(KrangMasterMindFactory.PrintedPower,
            "off-battlefield the CDA is inactive; Power returns the printed base value");
    }
}
