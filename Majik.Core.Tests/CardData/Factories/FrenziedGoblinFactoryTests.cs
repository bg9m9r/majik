using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Frenzied Goblin (Time Spiral / Ravnica, {R}, Creature —
/// Goblin Berserker 1/1). Oracle text (verified against Scryfall):
///   "Whenever this creature attacks, you may pay {R}. If you do, target
///    creature can't block this turn."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - A single attack TriggeredAbility with a 1..1 "target creature"
///     TargetRequest.
///   - Attack trigger fires when Frenzied Goblin itself attacks
///     (CR 508.1f) and not for an unrelated attacker.
///   - Resolution: when {R} is paid, a CannotBlock CombatRestrictionEffect is
///     registered on the chosen target (CR 509.1c).
///   - "If you do" gate: with no mana available the rider fizzles — no
///     restriction (CR 117.5 / 117.12).
///   - Illegal-target / null-ActiveEffects resolution guards (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class FrenziedGoblinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewTargetCreature(Player owner, string name = "Bear")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = new ContinuousEffectsService();
        return c;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FrenziedGoblin_Identity_GoblinBerserker_1_1_AtCostR()
    {
        var card = FrenziedGoblinFactory.Create(_alice);

        card.Name.Should().Be("Frenzied Goblin");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Berserker).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FrenziedGoblin_HasSingleAttackTrigger_WithOneTargetRequest()
    {
        var card = FrenziedGoblinFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].TargetRequests.Should().HaveCount(1, "the rider targets one creature");
        triggers[0].TargetRequests[0].MinTargets.Should().Be(1);
        triggers[0].TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchesFrenziedGoblin()
    {
        var card = NamedCardFactory.Create("Frenzied Goblin", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Frenzied Goblin");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Attack trigger firing (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_FiresWhenFrenziedGoblinAttacks()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FrenziedGoblinFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(card, _bob));

        triggers.PendingCount.Should().Be(1,
            "the attack trigger fires when Frenzied Goblin itself is declared as an attacker");
    }

    [Fact]
    public void AttackTrigger_DoesNotFireForUnrelatedAttacker()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FrenziedGoblinFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var other = new Creature("Other Attacker", "2", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        other.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(other, _bob));

        triggers.PendingCount.Should().Be(0,
            "the trigger is per-attacker on Frenzied Goblin itself (CR 508.1f)");
    }

    // -----------------------------------------------------------------------
    // Resolution — may pay {R} → target creature can't block
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resolution_PaysR_RegistersCannotBlockOnTarget()
    {
        var card = FrenziedGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Mana available + no agent => auto-pay path.
        _alice.AddManaToPool(ManaCost.Parse("{R}"));

        var target = NewTargetCreature(_bob);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var e in trigger.Effects) await e.ExecuteAsync(BuildCtx(trigger));

        target.ActiveEffects!.HasRestriction(target, CombatRestriction.CannotBlock)
            .Should().BeTrue("paying {R} locks the chosen creature out of blocking this turn (CR 509.1c)");
    }

    [Fact]
    public async Task Resolution_NoManaAvailable_NoRestriction()
    {
        var card = FrenziedGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // No mana added — PayMana({R}) fails, "If you do" never fires.

        var target = NewTargetCreature(_bob);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var e in trigger.Effects) await e.ExecuteAsync(BuildCtx(trigger));

        target.ActiveEffects!.HasRestriction(target, CombatRestriction.CannotBlock)
            .Should().BeFalse("with no mana the optional cost can't be paid (CR 117.5 / 117.12)");
    }

    [Fact]
    public async Task Resolution_TargetLeftBattlefield_NoRestriction()
    {
        var card = FrenziedGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("{R}"));

        var target = NewTargetCreature(_bob);
        target.SetZone(ZoneType.Graveyard); // CR 608.2b — illegal at resolution.

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var e in trigger.Effects) await e.ExecuteAsync(BuildCtx(trigger));

        target.ActiveEffects!.HasRestriction(target, CombatRestriction.CannotBlock)
            .Should().BeFalse("the target left the battlefield between choose and resolve (CR 608.2b)");
    }

    [Fact]
    public async Task Resolution_TargetWithoutActiveEffects_DoesNotThrow()
    {
        var card = FrenziedGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("{R}"));

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        // No ActiveEffects wired.

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = async () =>
        {
            foreach (var e in trigger.Effects) await e.ExecuteAsync(BuildCtx(trigger));
        };

        await act.Should().NotThrowAsync("the effect body guards on a null ActiveEffects");
    }

    private ResolutionContext BuildCtx(TriggeredAbility trigger) =>
        ResolutionContext.For(
            controller: _alice,
            agent: null,
            game: null,
            chosenTargets: trigger.ChosenTargets);
}
