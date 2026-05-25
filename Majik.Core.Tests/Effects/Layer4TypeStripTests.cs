using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Tests for <see cref="Layer4TypeStripEffect"/> — the CR 205.2 / 613.1d
/// Layer 4 (type-changing) continuous effect that conditionally strips
/// <see cref="CardType.Creature"/> from a permanent while a
/// controller-supplied predicate evaluates true.
/// </summary>
public class Layer4TypeStripTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature SeedHeliodLike(Player owner, ContinuousEffectsService service)
    {
        var c = new Creature("God-Test", "{1}{W}{W}", 5, 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.God });
        c.AddCardType(CardType.Enchantment);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = service;
        return c;
    }

    [Fact]
    public void StripEffect_WhilePredicateTrue_RemovesCreatureType()
    {
        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        service.Register(new Layer4TypeStripEffect(target, predicate: () => true));

        var chars = service.Compute((Permanent)target);

        chars.Types.Should().NotContain(CardType.Creature);
        // Enchantment (printed) is untouched — strip is creature-only.
        chars.Types.Should().Contain(CardType.Enchantment);
    }

    [Fact]
    public void StripEffect_WhilePredicateFalse_LeavesCreatureType()
    {
        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        service.Register(new Layer4TypeStripEffect(target, predicate: () => false));

        var chars = service.Compute((Permanent)target);

        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Enchantment);
    }

    [Fact]
    public void StripEffect_PredicateIsReEvaluatedEachCompute()
    {
        // Live predicate — predicate flickers true → false → true across
        // three Compute passes. The effect should reflect the latest read
        // each time without re-registering.
        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        var gate = true;
        service.Register(new Layer4TypeStripEffect(target, predicate: () => gate));

        service.Compute((Permanent)target).Types.Should().NotContain(CardType.Creature);

        gate = false;
        service.Compute((Permanent)target).Types.Should().Contain(CardType.Creature);

        gate = true;
        service.Compute((Permanent)target).Types.Should().NotContain(CardType.Creature);
    }

    [Fact]
    public void StripEffect_OnlyAppliesToSource()
    {
        // CR 613 — the strip is scoped to its source permanent; other
        // permanents (even creatures controlled by the same player) are
        // not affected.
        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        var bystander = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bystander);
        bystander.SetZone(ZoneType.Battlefield);
        bystander.ActiveEffects = service;

        service.Register(new Layer4TypeStripEffect(target, predicate: () => true));

        service.Compute((Permanent)target).Types.Should().NotContain(CardType.Creature);
        service.Compute((Permanent)bystander).Types.Should().Contain(CardType.Creature);
    }

    [Fact]
    public void StripEffect_EndsWhenSourceLeavesBattlefield()
    {
        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        var effect = new Layer4TypeStripEffect(target, predicate: () => true);
        service.Register(effect);

        // While on battlefield: effect active, strip applies.
        effect.IsActive().Should().BeTrue();
        service.Compute((Permanent)target).Types.Should().NotContain(CardType.Creature);

        // LTB → effect inactive; Prune drops it.
        target.SetZone(ZoneType.Graveyard);
        effect.IsActive().Should().BeFalse();

        service.Prune();
        // Reseed for the next Compute on the original card (zone moved,
        // but characteristics-from-printed are unchanged by Prune).
        // After Prune, the effect is gone — even if the source re-entered
        // the battlefield with the same identity, the strip is no longer
        // registered.
        target.SetZone(ZoneType.Battlefield);
        service.Compute((Permanent)target).Types.Should().Contain(CardType.Creature);
    }

    [Fact]
    public void StripEffect_StacksWithOtherLayer4Effects_AddSubtypeUnchanged()
    {
        // CR 613.7 — multiple Layer 4 effects are applied in the same
        // layer. A type-strip and an add-subtype effect on the same
        // target should coexist: the subtype is added even while the
        // creature-type is stripped (the subtype slot is independent of
        // the type slot at Layer 4).
        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        service.Register(new Layer4TypeStripEffect(target, predicate: () => true));
        service.Register(new AddSubtypeEffect(target, CardSubtype.Soldier));

        var chars = service.Compute((Permanent)target);

        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Soldier);
        // Printed subtype (God) is preserved by AddSubtypeEffect (additive).
        chars.Subtypes.Should().Contain(CardSubtype.God);
    }

    [Fact]
    public void StripEffect_StacksWithLayer4AddCreature_LastTimestampWins()
    {
        // CR 613.7 — when multiple Layer 4 effects modify the same slot
        // (one stripping Creature, one adding Creature), they apply in
        // timestamp order. Latest-timestamp wins because the type set is
        // mutated in place and the later Apply observes / overwrites the
        // earlier one's state.
        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        // Strip first (earlier timestamp).
        var strip = new Layer4TypeStripEffect(target, predicate: () => true);
        service.Register(strip);

        // Ensure a strictly-later timestamp on the add effect (DateTime.UtcNow
        // resolution can collide otherwise).
        System.Threading.Thread.Sleep(2);

        // Karn-style animate (Layer 4 add Creature) registered AFTER the
        // strip — should observe latest-timestamp wins → Creature present.
        var animate = new KarnAnimateArtifactEffect(target);
        service.Register(animate);

        var chars = service.Compute((Permanent)target);
        // Add applied after strip → Creature ends up in the type set.
        chars.Types.Should().Contain(CardType.Creature);
    }

    [Fact]
    public void StripEffect_NullSourceOrPredicate_Throws()
    {
        FluentActions.Invoking(() =>
            new Layer4TypeStripEffect(source: null!, predicate: () => true))
            .Should().Throw<ArgumentNullException>();

        var service = new ContinuousEffectsService();
        var target = SeedHeliodLike(_alice, service);

        FluentActions.Invoking(() =>
            new Layer4TypeStripEffect(source: target, predicate: null!))
            .Should().Throw<ArgumentNullException>();
    }
}
