using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MerfolkMistbinderFactory"/>.
///
/// Merfolk Mistbinder (Rivals of Ixalan, {G}{U}). Creature — Merfolk Shaman 2/2.
/// Oracle (verified against Scryfall):
///   "Other Merfolk you control get +1/+1."
///
/// Coverage:
/// - Identity (name, types, subtypes, cost, colours, P/T, owner/controller).
/// - NamedCardFactory dispatch.
/// - Lord static (CR 613.7c): other controller-Merfolk get +1/+1; self,
///   opponent Merfolk, and non-Merfolk unaffected.
/// </summary>
public class MerfolkMistbinderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeMerfolk(Player owner, string name = "Cursecatcher")
    {
        var c = new Creature(name, "{U}", 1, 1, subtypes: new[] { CardSubtype.Merfolk });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonMerfolk(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void MerfolkMistbinder_Identity()
    {
        var c = MerfolkMistbinderFactory.Create(_alice);

        c.Name.Should().Be("Merfolk Mistbinder");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.ManaCost.Should().Be("{G}{U}");
        c.ManaCostValue.TotalValue.Should().Be(2);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        CardColors.GetColors(c).Should().Contain(ManaColor.Blue);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MerfolkMistbinder_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Merfolk Mistbinder", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Merfolk Mistbinder");
        ((Creature)c).HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
    }

    // ── Lord static ─────────────────────────────────────────────────────

    [Fact]
    public void MerfolkMistbinder_BuffsOtherControllerMerfolk_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherMerfolk = MakeMerfolk(_alice);
        otherMerfolk.ActiveEffects = svc;

        var mistbinder = MerfolkMistbinderFactory.Create(_alice, svc);
        mistbinder.SetZone(ZoneType.Battlefield);
        mistbinder.ActiveEffects = svc;

        otherMerfolk.GetPower().Should().Be(2,
            "other Merfolk controlled by the Mistbinder's controller get +1/+1 (1 → 2 power).");
        otherMerfolk.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MerfolkMistbinder_DoesNotBuffItself()
    {
        var svc = new ContinuousEffectsService();

        var mistbinder = MerfolkMistbinderFactory.Create(_alice, svc);
        mistbinder.SetZone(ZoneType.Battlefield);
        mistbinder.ActiveEffects = svc;

        mistbinder.GetPower().Should().Be(2,
            "printed 'Other Merfolk' excludes the Mistbinder itself (CR 613.1g).");
        mistbinder.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MerfolkMistbinder_DoesNotBuffOpponentMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var bobMerfolk = MakeMerfolk(_bob);
        bobMerfolk.ActiveEffects = svc;

        var mistbinder = MerfolkMistbinderFactory.Create(_alice, svc);
        mistbinder.SetZone(ZoneType.Battlefield);
        mistbinder.ActiveEffects = svc;

        bobMerfolk.GetPower().Should().Be(1,
            "controller-scoped lord — Bob's Merfolk are unaffected (allPlayers: false).");
        bobMerfolk.GetToughness().Should().Be(1);
    }

    [Fact]
    public void MerfolkMistbinder_DoesNotBuffNonMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var bears = MakeNonMerfolk(_alice);
        bears.ActiveEffects = svc;

        var mistbinder = MerfolkMistbinderFactory.Create(_alice, svc);
        mistbinder.SetZone(ZoneType.Battlefield);
        mistbinder.ActiveEffects = svc;

        bears.GetPower().Should().Be(2, "the anthem only buffs Merfolk.");
        bears.GetToughness().Should().Be(2);
    }
}
