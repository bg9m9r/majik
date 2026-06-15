using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="EzuriRenegadeLeaderFactory"/> (Legendary Creature —
/// Elf Warrior {1}{G}{G} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "{G}: Regenerate another target Elf.
///    {2}{G}{G}{G}: Elf creatures you control get +3/+3 and gain trample until
///    end of turn."
///
/// Covers:
/// - Identity (Legendary Elf Warrior 2/2 {1}{G}{G}).
/// - {G} regenerate (CR 701.18 / 701.15a): adds a regeneration shield to
///   another target Elf; rejects non-Elf, off-battlefield, and Ezuri itself.
/// - {2}{G}{G}{G} overrun (CR 602 / 613): every Elf the controller controls
///   gets +3/+3 and gains Trample until end of turn; non-Elves + opponent Elves
///   untouched.
/// </summary>
[Trait("Color", "G")]
public class EzuriRenegadeLeaderFactoryTests
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

    private static Creature MakeNonElf(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ManaCostCost.Description renders pips brace-free ("G", "2GGG").
    private static ActivatedAbility RegenerateAbility(Creature ezuri)
        => ezuri.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>()
                .Any(m => m.Description == "G"));

    private static ActivatedAbility OverrunAbility(Creature ezuri)
        => ezuri.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>()
                .Any(m => m.Description == "2GGG"));

    // ── Identity ───────────────────────────────────────────────────────

    [Fact]
    public void Identity()
    {
        var c = EzuriRenegadeLeaderFactory.Create(_alice);

        c.Name.Should().Be("Ezuri, Renegade Leader");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasTwoActivatedAbilities()
    {
        var c = EzuriRenegadeLeaderFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the {G} regenerate and the {2}{G}{G}{G} overrun");
    }

    // ── {G}: Regenerate another target Elf (CR 701.18 / 701.15a) ─────────

    [Fact]
    public void Regenerate_HasManaCostAndTargetRequest()
    {
        var ability = RegenerateAbility(EzuriRenegadeLeaderFactory.Create(_alice));

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Description.Should().Be("G");
        ability.TargetRequests.Should().ContainSingle()
            .Which.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Regenerate_AddsShieldToTargetElf()
    {
        var ezuri = EzuriRenegadeLeaderFactory.Create(_alice);
        ezuri.SetZone(ZoneType.Battlefield);

        var targetElf = MakeElf(_alice);
        targetElf.RegenerationShieldCount.Should().Be(0);

        var ability = RegenerateAbility(ezuri);
        ability.SetChosenTargets(new List<IReadOnlyList<object>> { new List<object> { targetElf } });
        foreach (var e in ability.Effects) e.Execute();

        targetElf.RegenerationShieldCount.Should().Be(1,
            "CR 701.18 — 'Regenerate target Elf' creates one regeneration shield.");
    }

    [Fact]
    public void Regenerate_DoesNotShield_NonElf()
    {
        var ezuri = EzuriRenegadeLeaderFactory.Create(_alice);
        ezuri.SetZone(ZoneType.Battlefield);

        var bears = MakeNonElf(_alice);

        var ability = RegenerateAbility(ezuri);
        ability.SetChosenTargets(new List<IReadOnlyList<object>> { new List<object> { bears } });
        foreach (var e in ability.Effects) e.Execute();

        bears.RegenerationShieldCount.Should().Be(0,
            "the regenerate is scoped to Elves (CR 608.2b resolve-time recheck).");
    }

    [Fact]
    public void Regenerate_DoesNotShield_Itself()
    {
        var ezuri = EzuriRenegadeLeaderFactory.Create(_alice);
        ezuri.SetZone(ZoneType.Battlefield);

        var ability = RegenerateAbility(ezuri);
        ability.SetChosenTargets(new List<IReadOnlyList<object>> { new List<object> { ezuri } });
        foreach (var e in ability.Effects) e.Execute();

        ezuri.RegenerationShieldCount.Should().Be(0,
            "the printed 'another target Elf' excludes Ezuri itself.");
    }

    [Fact]
    public void Regenerate_DoesNotShield_OffBattlefieldElf()
    {
        var ezuri = EzuriRenegadeLeaderFactory.Create(_alice);
        ezuri.SetZone(ZoneType.Battlefield);

        var elf = MakeElf(_alice);
        elf.SetZone(ZoneType.Graveyard); // no longer on the battlefield

        var ability = RegenerateAbility(ezuri);
        ability.SetChosenTargets(new List<IReadOnlyList<object>> { new List<object> { elf } });
        foreach (var e in ability.Effects) e.Execute();

        elf.RegenerationShieldCount.Should().Be(0,
            "CR 608.2b — an off-battlefield target is an illegal regenerate target.");
    }

    // ── {2}{G}{G}{G} overrun (CR 602 / 613) ─────────────────────────────

    [Fact]
    public void Overrun_HasManaCost()
    {
        var ability = OverrunAbility(EzuriRenegadeLeaderFactory.Create(_alice));

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Description.Should().Be("2GGG");
    }

    [Fact]
    public void Overrun_PumpsElvesAndGrantsTrample_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();

        var ezuri = EzuriRenegadeLeaderFactory.Create(_alice);
        ezuri.SetZone(ZoneType.Battlefield);
        ezuri.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(ezuri);

        var friendElf = MakeElf(_alice, "Llanowar Elves");
        friendElf.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(friendElf);

        var bears = MakeNonElf(_alice);
        bears.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(bears);

        var bobElf = MakeElf(_bob, "Heritage Druid");
        bobElf.ActiveEffects = effects;
        _bob.Zones.Battlefield.AddCard(bobElf);

        var ability = OverrunAbility(ezuri);
        foreach (var e in ability.Effects) e.Execute();

        // Controller's Elves: +3/+3 + Trample.
        ezuri.GetPower().Should().Be(5, "2/2 base +3/+3.");
        ezuri.GetToughness().Should().Be(5);
        CombatAbilities.HasTrample(ezuri).Should().BeTrue();

        friendElf.GetPower().Should().Be(4, "1/1 base +3/+3.");
        friendElf.GetToughness().Should().Be(4);
        CombatAbilities.HasTrample(friendElf).Should().BeTrue();

        // Non-Elf you control: untouched.
        bears.GetPower().Should().Be(2);
        bears.GetToughness().Should().Be(2);
        CombatAbilities.HasTrample(bears).Should().BeFalse(
            "the pump is scoped to Elves only.");

        // Opponent's Elf: untouched (CR 109.5 — 'Elf creatures you control').
        bobElf.GetPower().Should().Be(1);
        bobElf.GetToughness().Should().Be(1);
        CombatAbilities.HasTrample(bobElf).Should().BeFalse();
    }

    [Fact]
    public void Overrun_NoElves_NoOpsCleanly()
    {
        var ability = OverrunAbility(EzuriRenegadeLeaderFactory.Create(_alice));

        var act = () => { foreach (var e in ability.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
