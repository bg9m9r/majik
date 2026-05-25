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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ElvishArchdruidFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Elf + Druid subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - LordStaticEffect: other controller-Elves get +1/+1.
/// - Archdruid itself doesn't double-stack +1/+1 from its own static
///   (includeSelf: false).
/// - Opponent's Elf is NOT pumped (controller-scoped).
/// - LTB lifts the +1/+1 bonus.
/// - {T}: Add {G} for each Elf you control — mana ability identity +
///   activation produces N green for N Elves on the controller's
///   battlefield.
/// </summary>
public class ElvishArchdruidTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeElf(Player owner, string name = "Llanowar Elves")
    {
        var c = new Creature(name, "G", 1, 1, subtypes: new[] { CardSubtype.Elf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // ── Identity ─────────────────────────────────────────────────────────

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

    [Fact]
    public void ElvishArchdruid_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Elvish Archdruid", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Elvish Archdruid");
        ((Creature)c).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    // ── Lord static — +1/+1 to other controller-Elves ────────────────────

    [Fact]
    public void ElvishArchdruid_PumpsOtherControllerElf_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherElf = MakeElf(_alice);
        otherElf.ActiveEffects = svc;

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(archdruid);

        otherElf.GetPower().Should().Be(2,
            "other controller-Elves get +1/+1 from Archdruid's lord static.");
        otherElf.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ElvishArchdruid_DoesNotSelfPump_IncludeSelfFalse()
    {
        var svc = new ContinuousEffectsService();

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;

        archdruid.GetPower().Should().Be(2,
            "includeSelf:false — Archdruid's own +1/+1 static doesn't stack on itself.");
        archdruid.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ElvishArchdruid_DoesNotPump_OpponentElf()
    {
        var svc = new ContinuousEffectsService();

        var oppElf = MakeElf(_bob);
        oppElf.ActiveEffects = svc;

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;

        oppElf.GetPower().Should().Be(1,
            "Lord static is scoped to the controller (CR 109.5 — 'you').");
        oppElf.GetToughness().Should().Be(1);
    }

    [Fact]
    public void ElvishArchdruid_LTB_LiftsBonusFromOtherElf()
    {
        var svc = new ContinuousEffectsService();

        var otherElf = MakeElf(_alice);
        otherElf.ActiveEffects = svc;

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(archdruid);

        otherElf.GetPower().Should().Be(2);

        // LTB — LordStaticEffect.IsActive() short-circuits.
        archdruid.SetZone(ZoneType.Graveyard);

        otherElf.GetPower().Should().Be(1, "bonus lifts on LTB.");
        otherElf.GetToughness().Should().Be(1);
    }

    // ── Mana ability — {T}: Add {G} for each Elf you control ─────────────

    [Fact]
    public void ElvishArchdruid_HasOneManaAbility()
    {
        var c = ElvishArchdruidFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Archdruid has exactly one mana ability: {T}: Add {G} for each Elf you control.");
    }

    [Fact]
    public void ElvishArchdruid_ManaAbility_AddsOneGreenPerElf_SelfOnly()
    {
        // Just Archdruid on the battlefield — count = 1 (Archdruid itself
        // is an Elf and counts; no "other" qualifier).
        var archdruid = ElvishArchdruidFactory.Create(_alice);
        archdruid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(archdruid);

        var manaAbility = archdruid.Abilities.OfType<ManaAbility>().Single();
        manaAbility.CanActivate().Should().BeTrue("Archdruid is untapped.");

        var mana = manaAbility.Activate();
        // ManaCost.ToString() emits "G" for one green pip (no braces).
        mana.ToString().Should().Be("G",
            "1 Elf on the battlefield (Archdruid itself) ⇒ {G}.");
        archdruid.IsTapped.Should().BeTrue("activating the {T} mana ability taps Archdruid.");
    }

    [Fact]
    public void ElvishArchdruid_ManaAbility_AddsGreenPerElf_MultipleElves()
    {
        // Archdruid + 3 other Elves = 4 total ⇒ {G}{G}{G}{G}.
        var elf1 = MakeElf(_alice, "Llanowar Elves");
        var elf2 = MakeElf(_alice, "Fyndhorn Elves");
        var elf3 = MakeElf(_alice, "Elvish Mystic");

        var archdruid = ElvishArchdruidFactory.Create(_alice);
        archdruid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(archdruid);

        var manaAbility = archdruid.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        mana.ToString().Should().Be("GGGG",
            "4 Elves on the controller's battlefield ⇒ {G}{G}{G}{G}.");

        // Make sure the lord static doesn't accidentally affect the
        // count — these helpers don't wire ActiveEffects, the count is
        // pure subtype-membership.
        _ = elf1; _ = elf2; _ = elf3;
    }

    [Fact]
    public void ElvishArchdruid_ManaAbility_DoesNotCountOpponentElves()
    {
        var oppElf = MakeElf(_bob, "Llanowar Elves");

        var archdruid = ElvishArchdruidFactory.Create(_alice);
        archdruid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(archdruid);

        var manaAbility = archdruid.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        mana.ToString().Should().Be("G",
            "opponent's Elves don't count — only the controller's battlefield.");
        _ = oppElf;
    }

    [Fact]
    public void ElvishArchdruid_ManaAbility_CannotActivateWhileTapped()
    {
        var archdruid = ElvishArchdruidFactory.Create(_alice);
        archdruid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(archdruid);

        archdruid.Tap();

        var manaAbility = archdruid.Abilities.OfType<ManaAbility>().Single();
        manaAbility.CanActivate().Should().BeFalse(
            "{T} ability gated on !IsTapped.");
    }

    [Fact]
    public void ComputeManaAddition_ReturnsZero_WhenNoElves()
    {
        // Defensive: in practice Archdruid is itself an Elf and must be
        // on the battlefield to activate, so 0 shouldn't happen.
        // ComputeManaAddition still returns Zero gracefully.
        var mana = ElvishArchdruidFactory.ComputeManaAddition(_alice);
        mana.TotalValue.Should().Be(0,
            "0 Elves ⇒ ManaCost.Zero (defensive).");
    }
}
