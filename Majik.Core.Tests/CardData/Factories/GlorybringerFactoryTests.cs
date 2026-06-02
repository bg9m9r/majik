using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlorybringerFactory"/> (Amonkhet, {3}{R}{R}).
///
/// Creature — Dragon 4/4. Oracle text (verified against Scryfall):
///   "Flying, haste
///    You may exert this creature as it attacks. When you do, it deals 4
///    damage to target non-Dragon creature an opponent controls. (An
///    exerted creature won't untap during your next untap step.)"
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - Flying + Haste keyword markers (CR 702.9 / 702.10).
///   - Exert attack trigger (CR 508.1f / 603.1 reflexive "when you do" /
///     CR 702.139): exert → 4 damage to the resolver-chosen non-Dragon
///     opponent creature, plus the "won't untap next untap step" rider.
///   - Decline exert → no damage, no untap-skip.
///   - Target gate: a Dragon is not a legal "non-Dragon" target.
///   - Trigger fires only when Glorybringer's controller attacks.
/// </summary>
public class GlorybringerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_ShipsDragonShape()
    {
        var dragon = GlorybringerFactory.Create(_alice);

        dragon.Should().BeOfType<Creature>();
        dragon.Name.Should().Be("Glorybringer");
        dragon.Power.Should().Be(4);
        dragon.Toughness.Should().Be(4);
        dragon.ManaCost.Should().Be("{3}{R}{R}");
        dragon.ManaCostValue.TotalValue.Should().Be(5);
        dragon.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        dragon.Owner.Should().BeSameAs(_alice);
        dragon.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsGlorybringerShape()
    {
        var dispatched = NamedCardFactory.Create("Glorybringer", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Glorybringer");
        dispatched.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void Create_AttachesFlying_Haste()
    {
        var dragon = GlorybringerFactory.Create(_alice);

        var keywords = dragon.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Haste");
    }

    [Fact]
    public void Create_AttachesAttackTrigger()
    {
        var dragon = GlorybringerFactory.Create(_alice);

        dragon.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Exert attack trigger
    // -------------------------------------------------------------------------

    [Fact]
    public void Exert_DealsFourToOpponentNonDragonCreature()
    {
        var bear = NonDragon("Grizzly Bears", _bob, 2, 2);
        var dragon = GlorybringerFactory.Create(
            _alice, eventBus: null, triggers: null,
            mayExert: () => true,
            opponentCreaturesResolver: _ => new[] { bear });

        Fire(dragon, AttackWith(dragon));

        bear.Damage.Should().Be(4, "exerted → 4 damage to target non-Dragon opponent creature");

        UntapStepRestrictions.RemoveAll(dragon);
    }

    [Fact]
    public void Exert_MarksCreatureWontUntap()
    {
        var bear = NonDragon("Grizzly Bears", _bob, 2, 2);
        var dragon = GlorybringerFactory.Create(
            _alice, eventBus: null, triggers: null,
            mayExert: () => true,
            opponentCreaturesResolver: _ => new[] { bear });

        Fire(dragon, AttackWith(dragon));

        // CR 702.139c — an exerted creature won't untap during your next
        // untap step.
        UntapStepRestrictions.ShouldSkipUntap(dragon, _alice)
            .Should().BeTrue("exert registers a 'won't untap next untap step' rider");

        // Cleanup so process-level untap-skip state doesn't leak across tests.
        UntapStepRestrictions.RemoveAll(dragon);
    }

    [Fact]
    public void DeclineExert_NoDamage_NoUntapSkip()
    {
        var bear = NonDragon("Grizzly Bears", _bob, 2, 2);
        var dragon = GlorybringerFactory.Create(
            _alice, eventBus: null, triggers: null,
            mayExert: () => false,
            opponentCreaturesResolver: _ => new[] { bear });

        Fire(dragon, AttackWith(dragon));

        bear.Damage.Should().Be(0, "declining the optional exert deals no damage");
        UntapStepRestrictions.ShouldSkipUntap(dragon, _alice)
            .Should().BeFalse("no exert → no untap-skip");
    }

    [Fact]
    public void Exert_DragonCreature_IsNotALegalTarget_NoDamage()
    {
        var oppDragon = new Creature("Furyblade Vampire", "{R}", 3, 3,
            subtypes: new[] { CardSubtype.Dragon });
        oppDragon.SetOwner(_bob);
        oppDragon.SetController(_bob);

        var dragon = GlorybringerFactory.Create(
            _alice, eventBus: null, triggers: null,
            mayExert: () => true,
            // Resolver returns the eligible (non-Dragon, opponent) pool; a
            // Dragon is excluded, so the pool is empty and no damage lands.
            opponentCreaturesResolver: _ => new[] { oppDragon });

        Fire(dragon, AttackWith(dragon));

        oppDragon.Damage.Should().Be(0,
            "'target non-Dragon creature' excludes Dragons");

        UntapStepRestrictions.RemoveAll(dragon);
    }

    [Fact]
    public void Exert_OnlyAttackingPlayersTrigger_OpponentAttackDoesNothing()
    {
        var bear = NonDragon("Grizzly Bears", _bob, 2, 2);
        var dragon = GlorybringerFactory.Create(
            _alice, eventBus: null, triggers: null,
            mayExert: () => true,
            opponentCreaturesResolver: _ => new[] { bear });

        // Bob (the opponent) is the attacking player — Glorybringer's
        // "as it attacks" trigger belongs to Alice and must not fire.
        var combat = new Majik.Core.Combat.Combat(attackingPlayer: _bob, defendingPlayer: _alice);
        Fire(dragon, combat);

        bear.Damage.Should().Be(0, "trigger fires only when Glorybringer's controller attacks");
        UntapStepRestrictions.ShouldSkipUntap(dragon, _alice).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Creature NonDragon(string name, Player controller, int p, int t)
    {
        var c = new Creature(name, "{1}", p, t);
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    private Majik.Core.Combat.Combat AttackWith(Creature attacker)
    {
        var combat = new Majik.Core.Combat.Combat(attackingPlayer: _alice, defendingPlayer: _bob);
        combat.AddAttacker(new Attacker(attacker, targetPlayer: _bob));
        return combat;
    }

    private void Fire(Creature dragon, Majik.Core.Combat.Combat combat)
    {
        var trigger = dragon.Abilities.OfType<TriggeredAbility>().Single();

        // Evaluate the condition (latches the combat for the effect body),
        // then drive the effect bodies directly — the same execution-by-hand
        // posture as the Inti / Stormbreath factory tests.
        var fired = trigger.Condition.Matches(
            new AttackersDeclaredEvent(combat), trigger);
        if (!fired) return;
        foreach (var e in trigger.Effects) e.Execute();
    }
}
