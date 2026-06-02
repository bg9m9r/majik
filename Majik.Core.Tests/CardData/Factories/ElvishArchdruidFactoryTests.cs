using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ElvishArchdruidFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Elf + Druid subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - LordStaticEffect: other controller-Elves get +1/+1.
/// - Archdruid itself is NOT pumped by its own static (includeSelf: false).
/// - Opponent's Elf is NOT pumped (controller-scoped).
/// - Non-Elf creature you control is NOT pumped.
/// - {T} mana ability counts Elves on controller's battlefield (including
///   self) and produces that many {G}.
/// </summary>
[Trait("Color", "G")]
public class ElvishArchdruidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeElf(Player owner, string name = "Llanowar Elves")
    {
        var c = new Creature(name, "G", 1, 1, subtypes: new[] { CardSubtype.Elf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonElf(Player owner)
    {
        var c = new Creature("Grizzly Bears", "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void ElvishArchdruid_Identity()
    {
        var c = ElvishArchdruidFactory.Create(_alice);

        c.Name.Should().Be("Elvish Archdruid");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Lord static ────────────────────────────────────────────────────

    [Fact]
    public void ElvishArchdruid_BuffsOtherControllerElf_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherElf = MakeElf(_alice, "Llanowar Elves");
        otherElf.ActiveEffects = svc;

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;

        otherElf.GetPower().Should().Be(2,
            "other Elves controlled by Archdruid's controller get +1/+1 (1 → 2 power).");
        otherElf.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ElvishArchdruid_DoesNotBuffItself()
    {
        var svc = new ContinuousEffectsService();

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;

        archdruid.GetPower().Should().Be(2,
            "printed 'Other Elf creatures' excludes the Archdruid itself (CR 613.1g).");
        archdruid.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ElvishArchdruid_DoesNotBuffOpponentElf()
    {
        var svc = new ContinuousEffectsService();

        var bobElf = MakeElf(_bob, "Heritage Druid");
        bobElf.ActiveEffects = svc;

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;

        bobElf.GetPower().Should().Be(1,
            "controller-scoped lord — Bob's Elves are unaffected (allPlayers: false).");
        bobElf.GetToughness().Should().Be(1);
    }

    [Fact]
    public void ElvishArchdruid_DoesNotBuffNonElfCreature()
    {
        var svc = new ContinuousEffectsService();

        var bears = MakeNonElf(_alice);
        bears.ActiveEffects = svc;

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;

        bears.GetPower().Should().Be(2,
            "matching subtype = Elf only; non-Elf creatures aren't buffed.");
        bears.GetToughness().Should().Be(2);
    }

    // ── Tribal mana ability ────────────────────────────────────────────

    [Fact]
    public void ElvishArchdruid_HasManaAbility()
    {
        var c = ElvishArchdruidFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Elvish Archdruid has one mana ability: {T}: Add {G} × Elves you control.");
    }

    [Fact]
    public void ElvishArchdruid_ManaAbility_AloneProducesOneGreen()
    {
        // Only Elf in play is Archdruid himself — count = 1 (no "other" qualifier).
        var archdruid = ElvishArchdruidFactory.Create(_alice);
        archdruid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(archdruid);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // tribal mana count rather than the {T} sickness gate.
        archdruid.ClearSummoningSickness();

        var manaAbility = archdruid.Abilities.OfType<ManaAbility>().Single();
        manaAbility.CanActivate().Should().BeTrue();

        var mana = manaAbility.Activate();
        mana.ToString().Should().Be("G",
            "with just Archdruid in play, X = 1 → produces one green mana.");
        archdruid.IsTapped.Should().BeTrue("tap cost is paid on activation.");
    }

    [Fact]
    public void ElvishArchdruid_ManaAbility_ScalesWithElfCount()
    {
        var archdruid = ElvishArchdruidFactory.Create(_alice);
        archdruid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(archdruid);
        // CR 302.6 — clear summoning sickness so Activate() is legal and the
        // test asserts the Elf-count scaling, not the sickness gate.
        archdruid.ClearSummoningSickness();

        // Two friend Elves on controller's battlefield.
        var elf1 = MakeElf(_alice, "Llanowar Elves");
        _alice.Zones.Battlefield.AddCard(elf1);

        var elf2 = MakeElf(_alice, "Elvish Mystic");
        _alice.Zones.Battlefield.AddCard(elf2);

        var manaAbility = archdruid.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        // Three Elves total → three green pips.
        mana.ToString().Should().Be("GGG",
            "X = controller's Elves (Archdruid + Llanowar + Mystic = 3) → three green mana.");
    }

    [Fact]
    public void ElvishArchdruid_ManaAbility_IgnoresOpponentElves()
    {
        var archdruid = ElvishArchdruidFactory.Create(_alice);
        archdruid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(archdruid);
        // CR 302.6 — clear summoning sickness so Activate() is legal and the
        // test asserts the controller-scoped Elf count, not the sickness gate.
        archdruid.ClearSummoningSickness();

        // Bob has an Elf — should NOT count toward Alice's X.
        var bobElf = MakeElf(_bob, "Heritage Druid");
        _bob.Zones.Battlefield.AddCard(bobElf);

        var manaAbility = archdruid.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        mana.ToString().Should().Be("G",
            "CR 109.5 — 'you control' filters to the controller's battlefield only.");
    }
}
