using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for <see cref="CombatMembershipRegistry"/> — the live
/// "who is attacking / blocking right now" surface (CR 508 / CR 509) that
/// <see cref="CombatFlow"/> populates and an in-combat target gate
/// (Eiganjo, Seat of the Empire's channel) reads.
/// </summary>
public class CombatMembershipRegistryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature Creature(string name)
    {
        var c = new Creature(name, "{1}", 2, 2) { Owner = _alice, Controller = _alice };
        return c;
    }

    [Fact]
    public void FreshRegistry_ReportsNoCombatMembership()
    {
        var reg = new CombatMembershipRegistry();
        var c = Creature("Grizzly Bears");

        reg.IsAttacking(c).Should().BeFalse();
        reg.IsBlocking(c).Should().BeFalse();
        reg.IsAttackingOrBlocking(c).Should().BeFalse();
        reg.AttackingOrBlocking().Should().BeEmpty();
    }

    [Fact]
    public void RecordAttacker_MarksCreatureAttacking()
    {
        var reg = new CombatMembershipRegistry();
        var c = Creature("Goblin Guide");

        reg.RecordAttacker(c);

        reg.IsAttacking(c).Should().BeTrue();
        reg.IsBlocking(c).Should().BeFalse();
        reg.IsAttackingOrBlocking(c).Should().BeTrue();
        reg.AttackingOrBlocking().Should().ContainSingle().Which.Should().BeSameAs(c);
    }

    [Fact]
    public void RecordBlocker_MarksCreatureBlocking()
    {
        var reg = new CombatMembershipRegistry();
        var c = Creature("Wall of Omens");

        reg.RecordBlocker(c);

        reg.IsBlocking(c).Should().BeTrue();
        reg.IsAttacking(c).Should().BeFalse();
        reg.IsAttackingOrBlocking(c).Should().BeTrue();
    }

    [Fact]
    public void Clear_DropsAllMembership()
    {
        var reg = new CombatMembershipRegistry();
        var atk = Creature("Attacker");
        var blk = Creature("Blocker");
        reg.RecordAttacker(atk);
        reg.RecordBlocker(blk);

        reg.Clear();

        reg.IsAttackingOrBlocking(atk).Should().BeFalse();
        reg.IsAttackingOrBlocking(blk).Should().BeFalse();
        reg.AttackingOrBlocking().Should().BeEmpty();
    }

    [Fact]
    public void RemoveFromCombat_DropsASingleCreatureWithoutAffectingOthers()
    {
        // CR 506.4 / CR 701.15c — a creature removed from combat (e.g. by
        // consuming a regeneration shield) is no longer attacking/blocking even
        // though combat continues for the rest. The registry must drop just
        // that creature, not the whole combat (which is what Clear() does).
        var reg = new CombatMembershipRegistry();
        var atk = Creature("Attacker");
        var blk = Creature("Blocker");
        var other = Creature("OtherAttacker");
        reg.RecordAttacker(atk);
        reg.RecordBlocker(blk);
        reg.RecordAttacker(other);

        reg.RemoveFromCombat(atk);
        reg.RemoveFromCombat(blk);

        reg.IsAttackingOrBlocking(atk).Should().BeFalse(
            "a creature removed from combat is no longer attacking (CR 506.4)");
        reg.IsAttackingOrBlocking(blk).Should().BeFalse(
            "a creature removed from combat is no longer blocking (CR 506.4)");
        reg.IsAttacking(other).Should().BeTrue(
            "removing one creature must not disturb the rest of the combat");
        reg.AttackingOrBlocking().Should().ContainSingle().Which.Should().BeSameAs(other);
    }

    [Fact]
    public void RemoveFromCombat_UnknownCreature_IsANoOp()
    {
        var reg = new CombatMembershipRegistry();
        var atk = Creature("Attacker");
        reg.RecordAttacker(atk);

        reg.RemoveFromCombat(Creature("NeverInCombat"));

        reg.IsAttacking(atk).Should().BeTrue("removing a non-member leaves members untouched");
    }

    [Fact]
    public void AttackingOrBlocking_DeduplicatesACreatureThatIsBoth()
    {
        // A creature can't really attack AND block the same combat, but the
        // snapshot must never double-list the same reference.
        var reg = new CombatMembershipRegistry();
        var c = Creature("Both");
        reg.RecordAttacker(c);
        reg.RecordBlocker(c);

        reg.AttackingOrBlocking().Should().ContainSingle().Which.Should().BeSameAs(c);
    }
}
