using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Boromir, Warden of the Tower (LOTR, {2}{W}).
///
/// Oracle:
///   "Vigilance"
///   "Whenever an opponent casts a spell, if no mana was spent to cast it,
///    counter that spell."
///   "Sacrifice Boromir: Creatures you control gain indestructible until end
///    of turn. The Ring tempts you."
///
/// Coverage:
/// - Identity: {2}{W} 2/3 white legendary Human Soldier, mana value 3.
/// - Vigilance keyword marker; NamedCardFactory dispatch.
/// - Free-spell counter: counters an opponent's 0-mana cast; ignores a
///   mana-paid cast; ignores the controller's own free cast ("an opponent").
/// - Sac ability grants the controller's creatures indestructible AND tempts
///   (Ring created, count incremented, Ring-bearer designated).
/// </summary>
[Trait("Color", "W")]
public class BoromirWardenOfTheTowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasBoromirShape()
    {
        var boromir = BoromirWardenOfTheTowerFactory.Create(_alice);

        boromir.Should().BeOfType<Creature>();
        boromir.Name.Should().Be("Boromir, Warden of the Tower");
        boromir.ManaCost.Should().Be("{2}{W}");
        boromir.ManaCostValue.TotalValue.Should().Be(3);
        boromir.BasePower.Should().Be(2);
        boromir.BaseToughness.Should().Be(3);
        boromir.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        boromir.HasSubtype(CardSubtype.Human).Should().BeTrue();
        boromir.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        CardColors.GetColors(boromir).Should().Contain(ManaColor.White);
        boromir.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasVigilanceMarker_CounterTrigger_AndSacAbility()
    {
        var boromir = BoromirWardenOfTheTowerFactory.Create(_alice);

        boromir.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Vigilance");
        boromir.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        boromir.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }
    [Fact]
    public void SacAbility_CostIsSacrificeSelf()
    {
        var boromir = BoromirWardenOfTheTowerFactory.Create(_alice);
        var sac = boromir.Abilities.OfType<ActivatedAbility>().Single();
        sac.Costs.Should().ContainSingle().Which.Should().BeOfType<SacrificeSelfCost>();
    }

    // -----------------------------------------------------------------------
    // Free-spell counter
    // -----------------------------------------------------------------------

    private (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers, Creature boromir)
        WireBoromirOnBattlefield()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var boromir = BoromirWardenOfTheTowerFactory.Create(
            _alice, bus, triggers, continuousEffects: null, stack: stack,
            allPlayersResolver: () => new[] { _alice, _bob }, ringBearerChooser: null);
        boromir.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(boromir);
        return (bus, stack, triggers, boromir);
    }

    [Fact]
    public void FreeSpellCounter_CountersOpponentFreeCast()
    {
        var (bus, stack, triggers, _) = WireBoromirOnBattlefield();

        // Bob (opponent) casts a 0-mana spell.
        var memnite = new Card("Memnite", "{0}");
        memnite.SetOwner(_bob);
        memnite.SetZone(ZoneType.Stack);
        var freeSpell = new Majik.Core.Spells.Spell(memnite, _bob) { WasFreeCast = true };
        stack.Push(freeSpell);

        bus.Publish(new SpellCastEvent(freeSpell));
        triggers.PendingCount.Should().Be(1, "opponent cast a free spell — counter trigger fires");

        triggers.PutPendingTriggersOnStack(_alice);
        // Pop the trigger off the top and resolve it.
        var trigger = stack.Pop()!;
        trigger.Resolve();

        stack.GetAll().Should().NotContain(freeSpell, "the free spell was countered (CR 701.5)");
        memnite.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FreeSpellCounter_IgnoresOpponentManaPaidCast()
    {
        var (bus, stack, triggers, _) = WireBoromirOnBattlefield();

        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        var paidSpell = new Majik.Core.Spells.Spell(bolt, _bob); // WasFreeCast defaults false
        stack.Push(paidSpell);

        bus.Publish(new SpellCastEvent(paidSpell));

        triggers.PendingCount.Should().Be(0, "mana was paid — Boromir does not counter");
        stack.GetAll().Should().Contain(paidSpell);
    }

    [Fact]
    public void FreeSpellCounter_IgnoresControllersOwnFreeCast()
    {
        var (bus, stack, triggers, _) = WireBoromirOnBattlefield();

        // Alice (Boromir's controller) casts her own free spell.
        var ornithopter = new Card("Ornithopter", "{0}");
        ornithopter.SetOwner(_alice);
        var ownFree = new Majik.Core.Spells.Spell(ornithopter, _alice) { WasFreeCast = true };
        stack.Push(ownFree);

        bus.Publish(new SpellCastEvent(ownFree));

        triggers.PendingCount.Should().Be(0,
            "the trigger is 'whenever an opponent casts' — the controller's own free cast is exempt");
    }

    // -----------------------------------------------------------------------
    // Sacrifice ability: indestructible + the Ring tempts you
    // -----------------------------------------------------------------------

    [Fact]
    public void SacAbility_GrantsIndestructible_AndTemptsController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var continuous = new ContinuousEffectsService();

        var boromir = BoromirWardenOfTheTowerFactory.Create(
            _alice, bus, triggers, continuous, stack,
            allPlayersResolver: () => new[] { _alice, _bob }, ringBearerChooser: null);
        boromir.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(boromir);

        // Another creature to be the Ring-bearer + receive indestructible.
        var soldier = new Creature("Soldier", "{W}", 1, 1);
        soldier.SetOwner(_alice);
        soldier.SetController(_alice);
        soldier.SetZone(ZoneType.Battlefield);
        soldier.ActiveEffects = continuous;
        _alice.Zones.Battlefield.AddCard(soldier);

        _alice.Ring.Should().BeNull();

        // Resolve the sac ability's effect chain.
        var sac = boromir.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in sac.Effects) e.Execute();

        // Indestructible granted to the controller's creatures.
        CombatAbilities.HasIndestructible(soldier).Should().BeTrue(
            "the controller's creatures gain indestructible until end of turn");

        // The Ring tempts you: emblem created, count incremented, bearer chosen.
        _alice.Ring.Should().NotBeNull("'The Ring tempts you' created the emblem (CR 701.54c)");
        _alice.Ring!.TemptCount.Should().Be(1);
        _alice.Ring.RingBearer.Should().NotBeNull("a Ring-bearer was designated (CR 701.54a)");
    }
}
