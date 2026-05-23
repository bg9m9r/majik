using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Dragon's Rage Channeler (Modern Horizons 2, {R}, Creature —
/// Human Shaman 1/1).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Surveil trigger on noncreature spell cast (instant / sorcery /
///     artifact / etc.).
///   - Surveil trigger does NOT fire on creature spell cast.
///   - Opponent's noncreature cast does not trigger.
///   - Delirium active (4+ types in graveyard): +2/+2 and Flying.
///   - Delirium inactive (3 types): printed 1/1, no Flying.
///   - Delirium dynamic: gaining a 4th type while DRC is on the
///     battlefield lights up the static; losing a type turns it off.
/// </summary>
public class DragonsRageChannelerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    private static void SeedGraveyard(Player owner, params CardType[][] typeBundles)
    {
        var i = 0;
        foreach (var types in typeBundles)
        {
            var card = new Card($"Seed{i++}", "0", types);
            card.SetOwner(owner);
            owner.Zones.Graveyard.AddCard(card);
        }
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DragonsRageChanneler_Identity_HumanShaman_1_1_AtCostR()
    {
        var drc = DragonsRageChannelerFactory.Create(_alice);

        drc.Name.Should().Be("Dragon's Rage Channeler");
        drc.ManaCost.Should().Be("{R}");
        drc.HasType(CardType.Creature).Should().BeTrue();
        drc.HasSubtype(CardSubtype.Human).Should().BeTrue();
        drc.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        drc.BasePower.Should().Be(1);
        drc.BaseToughness.Should().Be(1);
        drc.Owner.Should().BeSameAs(_alice);
        drc.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DragonsRageChanneler()
    {
        var card = NamedCardFactory.Create("Dragon's Rage Channeler", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dragon's Rage Channeler");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Surveil trigger — fires on noncreature spell only
    // -----------------------------------------------------------------------

    [Fact]
    public void NoncreatureSpellCast_TriggersSurveilOne()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var drc = DragonsRageChannelerFactory.Create(_alice, bus, triggers, effects: null);
        drc.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "Top");
        var next = NewCardInLibrary(_alice, "Next");

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // No agent registered → surveiled card goes to graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { next });
    }

    [Fact]
    public void CreatureSpellCast_DoesNotTriggerSurveil()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var drc = DragonsRageChannelerFactory.Create(_alice, bus, triggers, effects: null);
        drc.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "Top");

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Bear")));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void OpponentNoncreatureCast_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var drc = DragonsRageChannelerFactory.Create(_alice, bus, triggers, effects: null);
        drc.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "BobBolt")));

        triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Delirium — conditional +2/+2 and Flying
    // -----------------------------------------------------------------------

    /// <summary>
    /// Helper: build DRC, route it through CardMovedEvent → battlefield so
    /// the delirium lifecycle's Sync registers the +2/+2 / Flying effects.
    /// </summary>
    private Creature CreateAndMoveToBattlefield(EventBus bus, ContinuousEffectsService effects)
    {
        var drc = DragonsRageChannelerFactory.Create(_alice, bus, triggers: null, effects: effects);
        drc.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(drc, ZoneType.Hand, ZoneType.Battlefield));
        return drc;
    }

    [Fact]
    public void DeliriumInactive_ThreeTypes_DRC_Is_1_1_NoFlying()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var drc = CreateAndMoveToBattlefield(bus, effects);

        // 3 distinct card types — below the 4-type threshold.
        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        DragonsRageChannelerFactory.IsDeliriumActive(_alice).Should().BeFalse();

        drc.Power.Should().Be(1);
        drc.Toughness.Should().Be(1);
        CombatAbilities.HasFlying(drc).Should().BeFalse();
    }

    [Fact]
    public void DeliriumActive_FourTypes_DRC_Is_3_3_WithFlying()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var drc = CreateAndMoveToBattlefield(bus, effects);

        // Exactly 4 distinct types — delirium satisfied (CR 702.105).
        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        DragonsRageChannelerFactory.IsDeliriumActive(_alice).Should().BeTrue();

        drc.Power.Should().Be(3);
        drc.Toughness.Should().Be(3);
        CombatAbilities.HasFlying(drc).Should().BeTrue();
    }

    [Fact]
    public void DeliriumDynamic_GainingFourthType_LightsUpStatic()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();

        var drc = DragonsRageChannelerFactory.Create(_alice, bus, triggers: null, effects: effects);
        // Move DRC onto the battlefield via the bus so the lifecycle's
        // CardMovedEvent handler registers the effects.
        var moved = new CardMovedEvent(drc, ZoneType.Hand, ZoneType.Battlefield);
        drc.SetZone(ZoneType.Battlefield);
        bus.Publish(moved);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        drc.Power.Should().Be(1, "3 types is below threshold");
        CombatAbilities.HasFlying(drc).Should().BeFalse();

        // Drop a fourth distinct type into the graveyard — delirium lights
        // up live on the next P/T / keyword read.
        var enchant = new Card("Holy Aura", "1W", new[] { CardType.Enchantment });
        enchant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(enchant);

        drc.Power.Should().Be(3);
        drc.Toughness.Should().Be(3);
        CombatAbilities.HasFlying(drc).Should().BeTrue();
    }
}
