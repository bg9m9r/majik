using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SteelLeafChampionFactory"/>.
///
/// Steel Leaf Champion (Dominaria, {G}{G}{G}) — Creature — Elf Knight 5/4.
/// Oracle text (verified against Scryfall 2026-06-14):
///   "This creature can't be blocked by creatures with power 2 or less."
///
/// Covers identity (non-vanilla statline) plus the unique conditional block
/// restriction (CR 509.1b).
/// </summary>
[Trait("Color", "G")]
public class SteelLeafChampionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void SteelLeafChampion_Identity()
    {
        var c = SteelLeafChampionFactory.Create(_alice);

        c.Name.Should().Be("Steel Leaf Champion");
        c.ManaCost.Should().Be("{G}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Conditional block restriction (CR 509.1b) ───────────────────────────

    [Fact]
    public void SteelLeafChampion_CantBeBlockedByPowerTwoOrLess()
    {
        var svc = new ContinuousEffectsService();
        var champ = SteelLeafChampionFactory.Create(_alice, svc);
        champ.SetZone(ZoneType.Battlefield);

        // Power 1 and power 2 blockers are forbidden; power 3 is allowed.
        var power1 = Blocker("Bird", 1);
        var power2 = Blocker("Bear", 2);
        var power3 = Blocker("Ogre", 3);

        BlockLegality.CanBlock(power1, champ, out _).Should().BeFalse(
            "power 1 ≤ 2 — can't block Steel Leaf Champion.");
        BlockLegality.CanBlock(power2, champ, out _).Should().BeFalse(
            "power 2 ≤ 2 — can't block Steel Leaf Champion.");
        BlockLegality.CanBlock(power3, champ, out _).Should().BeTrue(
            "power 3 > 2 — a legal blocker.");
    }

    [Fact]
    public void SteelLeafChampion_PumpedBlockerBecomesLegal()
    {
        // CR 509.1b is checked at block declaration against CURRENT power, so a
        // 2/2 pumped to 3/3 through the layer system becomes a legal blocker.
        var svc = new ContinuousEffectsService();
        var champ = SteelLeafChampionFactory.Create(_alice, svc);
        champ.SetZone(ZoneType.Battlefield);

        var bear = Blocker("Bear", 2);
        bear.ActiveEffects = svc;
        BlockLegality.CanBlock(bear, champ, out _).Should().BeFalse();

        // +1/+1 pump (CR 613.1f Layer 7c) lifts it to power 3.
        svc.Register(new PumpUntilEndOfTurnEffect(bear, 1, 1));
        bear.Power.Should().Be(3);
        BlockLegality.CanBlock(bear, champ, out _).Should().BeTrue(
            "pumped to power 3 — now a legal blocker.");
    }

    [Fact]
    public void SteelLeafChampion_ShapeOnlyPath_NoRestrictionRegistered()
    {
        // No effects service → restriction is not wired; any creature can block.
        var champ = SteelLeafChampionFactory.Create(_alice);
        champ.SetZone(ZoneType.Battlefield);

        var power1 = Blocker("Bird", 1);
        BlockLegality.CanBlock(power1, champ, out _).Should().BeTrue(
            "shape-only path registers no restriction.");
    }

    private Creature Blocker(string name, int power) =>
        new(name, "{1}", power, power)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
}
