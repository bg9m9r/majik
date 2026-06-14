using FluentAssertions;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Planeswalker = Majik.Core.Cards.Planeswalker;

public class AttackRestrictionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GhostlyPrison_BlocksAttackUntilPaid()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = PayPerAttackerRestriction.FlatMana(_bob, ManaCost.Parse("2"));
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);

        reg.MayAttack(bear, _bob).Should().BeFalse();

        restriction.MarkPaid(bear);
        reg.MayAttack(bear, _bob).Should().BeTrue();
    }

    [Fact]
    public void GhostlyPrison_DoesNotAffectAttacksOnOtherPlayers()
    {
        var carl = new Player("Carl", 20);
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = PayPerAttackerRestriction.FlatMana(_bob, ManaCost.Parse("2"));
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);

        reg.MayAttack(bear, carl).Should().BeTrue();
    }

    [Fact]
    public void ClearForTurn_ResetsPaidMarks()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = PayPerAttackerRestriction.FlatMana(_bob, ManaCost.Parse("2"));
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);
        restriction.MarkPaid(bear);
        reg.MayAttack(bear, _bob).Should().BeTrue();

        restriction.ClearForTurn();
        reg.MayAttack(bear, _bob).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // CR 711 / 508.1g — a "protects you or planeswalkers you control"
    // paywall (Sphere of Safety / Norn's Annex) must protect an EFFECTIVE
    // planeswalker too: a creature-front transform DFC flipped to its
    // planeswalker back carries a transient loyalty body
    // (IsEffectivePlaneswalker) without re-classing the C# instance. The
    // defender-side combat checks (Combat.TargetPlaneswalker,
    // CombatValidator.CanAttackPlaneswalker, DamageIntent.TargetPlaneswalker)
    // were already widened Planeswalker->Permanent; this is the symmetric
    // hole on the attack-restriction side.
    // ------------------------------------------------------------------

    [Fact]
    public void ProtectsPlaneswalkers_ProtectsRealPlaneswalker()
    {
        var jace = new Planeswalker("Jace", "1UU", 3)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = PayPerAttackerRestriction.Dynamic(
            _bob, () => ManaCost.Parse("2"), protectsPlaneswalkers: true);
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);

        reg.MayAttack(bear, jace).Should().BeFalse();
        restriction.MarkPaid(bear);
        reg.MayAttack(bear, jace).Should().BeTrue();
    }

    [Fact]
    public void ProtectsPlaneswalkers_ProtectsEffectivePlaneswalker()
    {
        // Creature-front DFC flipped to a PW back: a Creature instance carrying
        // a transient loyalty body (CR 711) — IsEffectivePlaneswalker(), not a
        // Planeswalker subclass instance.
        var flippedDfc = new Creature("Ral, Leyline Prodigy", "2UR", 0, 0)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        flippedDfc.SetTransientLoyalty(2);
        flippedDfc.IsEffectivePlaneswalker().Should().BeTrue();

        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = PayPerAttackerRestriction.Dynamic(
            _bob, () => ManaCost.Parse("2"), protectsPlaneswalkers: true);
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);

        reg.MayAttack(bear, flippedDfc).Should().BeFalse(
            "a 'protects planeswalkers you control' paywall must guard an effective planeswalker too");
        restriction.MarkPaid(bear);
        reg.MayAttack(bear, flippedDfc).Should().BeTrue();
    }

    [Fact]
    public void ProtectsPlaneswalkers_DoesNotProtectEffectivePlaneswalkerOfAnotherController()
    {
        // Effective PW controlled by the ATTACKER's controller, not the
        // protected player — the paywall must not apply.
        var myWalker = new Creature("My Flipped DFC", "2UR", 0, 0)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        myWalker.SetTransientLoyalty(2);

        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = PayPerAttackerRestriction.Dynamic(
            _bob, () => ManaCost.Parse("2"), protectsPlaneswalkers: true);
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);

        reg.MayAttack(bear, myWalker).Should().BeTrue();
    }
}
