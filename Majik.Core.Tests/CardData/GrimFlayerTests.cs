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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Grim Flayer (Eldritch Moon, {B}{G}, Creature — Human Warrior
/// 2/2).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, cost, owner/controller).
///   - Trample keyword marker.
///   - NamedCardFactory dispatch.
///   - Surveil-3 trigger on combat damage to a player.
///   - Trigger does NOT fire on combat damage dealt to a creature.
///   - Another creature's combat damage does not trigger.
///   - Delirium active (4+ types in graveyard): +2/+2 (no Flying — Grim
///     Flayer grants no keyword).
///   - Delirium inactive (3 types): printed 2/2.
///   - Delirium dynamic: gaining a 4th type while Grim Flayer is on the
///     battlefield lights up the static.
/// </summary>
public class GrimFlayerTests
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
    public void GrimFlayer_Identity_HumanWarrior_2_2_AtCostBG()
    {
        var gf = GrimFlayerFactory.Create(_alice);

        gf.Name.Should().Be("Grim Flayer");
        gf.ManaCost.Should().Be("{B}{G}");
        gf.HasType(CardType.Creature).Should().BeTrue();
        gf.HasSubtype(CardSubtype.Human).Should().BeTrue();
        gf.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        gf.BasePower.Should().Be(2);
        gf.BaseToughness.Should().Be(2);
        gf.Owner.Should().BeSameAs(_alice);
        gf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GrimFlayer_HasTrample()
    {
        var gf = GrimFlayerFactory.Create(_alice);
        CombatAbilities.HasTrample(gf).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GrimFlayer()
    {
        var card = NamedCardFactory.Create("Grim Flayer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Grim Flayer");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Surveil trigger — fires on combat damage to a player
    // -----------------------------------------------------------------------

    [Fact]
    public void CombatDamageToPlayer_TriggersSurveilThree()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var gf = GrimFlayerFactory.Create(_alice, bus, triggers, effects: null);
        gf.SetZone(ZoneType.Battlefield);

        var c1 = NewCardInLibrary(_alice, "C1");
        var c2 = NewCardInLibrary(_alice, "C2");
        var c3 = NewCardInLibrary(_alice, "C3");
        var c4 = NewCardInLibrary(_alice, "C4");

        bus.Publish(new CombatDamageDealtEvent(gf, _bob, amount: 2));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // No agent registered → all 3 surveiled cards go to graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { c1, c2, c3 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { c4 });
    }

    [Fact]
    public void CombatDamageToCreature_DoesNotTriggerSurveil()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var gf = GrimFlayerFactory.Create(_alice, bus, triggers, effects: null);
        gf.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "Top");

        var blocker = new Creature("Wall", "1G", 0, 4) { Owner = _bob };
        bus.Publish(new CombatDamageDealtEvent(gf, blocker, amount: 2));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void OtherCreatureCombatDamage_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var gf = GrimFlayerFactory.Create(_alice, bus, triggers, effects: null);
        gf.SetZone(ZoneType.Battlefield);

        var other = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        bus.Publish(new CombatDamageDealtEvent(other, _bob, amount: 2));

        triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Delirium — conditional +2/+2 (no keyword grant)
    // -----------------------------------------------------------------------

    private Creature CreateAndMoveToBattlefield(EventBus bus, ContinuousEffectsService effects)
    {
        var gf = GrimFlayerFactory.Create(_alice, bus, triggers: null, effects: effects);
        gf.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(gf, ZoneType.Hand, ZoneType.Battlefield));
        return gf;
    }

    [Fact]
    public void DeliriumInactive_ThreeTypes_GrimFlayer_Is_2_2()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var gf = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        GrimFlayerFactory.IsDeliriumActive(_alice).Should().BeFalse();

        gf.Power.Should().Be(2);
        gf.Toughness.Should().Be(2);
    }

    [Fact]
    public void DeliriumActive_FourTypes_GrimFlayer_Is_4_4()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var gf = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        GrimFlayerFactory.IsDeliriumActive(_alice).Should().BeTrue();

        gf.Power.Should().Be(4);
        gf.Toughness.Should().Be(4);
    }

    [Fact]
    public void DeliriumDynamic_GainingFourthType_LightsUpStatic()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();

        var gf = GrimFlayerFactory.Create(_alice, bus, triggers: null, effects: effects);
        var moved = new CardMovedEvent(gf, ZoneType.Hand, ZoneType.Battlefield);
        gf.SetZone(ZoneType.Battlefield);
        bus.Publish(moved);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        gf.Power.Should().Be(2, "3 types is below threshold");

        // Drop a fourth distinct type into the graveyard — delirium lights up
        // on the next P/T read. The graveyard add bypasses the event bus, so
        // invalidate the layer-system cache explicitly via Clear().
        var enchant = new Card("Holy Aura", "1W", new[] { CardType.Enchantment });
        enchant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(enchant);
        effects.Clear();

        gf.Power.Should().Be(4);
        gf.Toughness.Should().Be(4);
    }
}
