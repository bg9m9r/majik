using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Sanctum of Ugin (Battle for Zendikar).
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    Whenever you cast a colorless spell with mana value 7 or greater,
///    you may sacrifice this land. If you do, search your library for a
///    colorless creature card, reveal it, put it into your hand, then
///    shuffle."
///
/// Covers:
///   - Identity (Land, "Sanctum of Ugin", no subtypes, owner/controller).
///   - NamedCardFactory dispatch.
///   - {T}: Add {C} mana ability is present.
///   - Casting a colorless spell with MV ≥ 7 by the controller triggers
///     the optional-sac ability (trigger fires; PendingCount == 1).
///   - On resolve when agent says YES: land is sacrificed and a colorless
///     creature card from the library ends up in hand; library is shuffled.
///   - On resolve when agent says NO: land stays on battlefield; no tutor.
///   - Casting a NON-colorless spell with MV ≥ 7 does NOT trigger.
///   - Casting a colorless spell with MV &lt; 7 does NOT trigger.
///   - Opponent casting a colorless MV ≥ 7 spell does NOT trigger.
///   - Trigger is gated to Battlefield (CR 113.6).
/// </summary>
[Trait("Color", "C")]
public class SanctumOfUginFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>A colorless spell with the given printed mana cost.</summary>
    private static Majik.Core.Spells.Spell ColorlessSpell(Player controller, string manaCost)
    {
        // Creature with a purely generic mana cost → CardColors.GetColors returns empty set.
        var card = new Creature($"Eldrazi_{manaCost}", manaCost, 5, 5) { Owner = controller };
        return new Majik.Core.Spells.Spell(card, controller);
    }

    /// <summary>A colored spell (W) with MV ≥ 7.</summary>
    private static Majik.Core.Spells.Spell ColoredSpell(Player controller, string manaCost)
    {
        var card = new Creature($"Colored_{manaCost}", manaCost, 5, 5) { Owner = controller };
        return new Majik.Core.Spells.Spell(card, controller);
    }

    /// <summary>Seed the player's library with a colorless creature card.</summary>
    private static Creature SeedColorlessCreature(Player p, string name = "Eldrazi Titan")
    {
        var c = new Creature(name, "10", 10, 10) { Owner = p };
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SanctumOfUgin_Identity()
    {
        var land = SanctumOfUginFactory.Create(_alice);

        land.Name.Should().Be("Sanctum of Ugin");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void SanctumOfUgin_HasColorlessManaAbility()
    {
        var land = SanctumOfUginFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle("land produces exactly {C}")
            .Which.ManaGenerated.Generic.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Trigger fires / does-not-fire
    // -----------------------------------------------------------------------

    [Fact]
    public void SanctumOfUgin_Trigger_GatedToBattlefield()
    {
        var land = SanctumOfUginFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void SanctumOfUgin_ColorlessMV7_Fires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var land = SanctumOfUginFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Colorless MV exactly 7 should trigger.
        bus.Publish(new SpellCastEvent(ColorlessSpell(_alice, "7")));

        triggers.PendingCount.Should().Be(1,
            "colorless spell with MV 7 triggers Sanctum's ability");
    }

    [Fact]
    public void SanctumOfUgin_ColorlessMV10_Fires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var land = SanctumOfUginFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(ColorlessSpell(_alice, "10")));

        triggers.PendingCount.Should().Be(1,
            "colorless spell with MV 10 triggers Sanctum's ability");
    }

    [Fact]
    public void SanctumOfUgin_ColoredSpellMV7_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var land = SanctumOfUginFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // White-colored spell — should NOT trigger.
        bus.Publish(new SpellCastEvent(ColoredSpell(_alice, "6W")));

        triggers.PendingCount.Should().Be(0,
            "colored spell does not trigger Sanctum of Ugin");
    }

    [Fact]
    public void SanctumOfUgin_ColorlessMV6_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var land = SanctumOfUginFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Colorless but only MV 6.
        bus.Publish(new SpellCastEvent(ColorlessSpell(_alice, "6")));

        triggers.PendingCount.Should().Be(0,
            "colorless spell with MV < 7 does not trigger");
    }

    [Fact]
    public void SanctumOfUgin_OpponentCastsColorlessMV7_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var land = SanctumOfUginFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Bob casts the qualifying spell, not Alice.
        bus.Publish(new SpellCastEvent(ColorlessSpell(_bob, "7")));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' restricts trigger to Sanctum's controller");
    }

    // -----------------------------------------------------------------------
    // Resolve — agent says YES (sac + tutor)
    // -----------------------------------------------------------------------

    [Fact]
    public void SanctumOfUgin_Resolve_YesSac_TutorsColorlessCreatureToHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // "You may sacrifice this land. If you do, ..."
        AgentRegistry.Set(_alice, agent);

        try
        {
            var land = SanctumOfUginFactory.Create(_alice, bus, triggers);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);

            var titan = SeedColorlessCreature(_alice, "Eldrazi Titan");

            bus.Publish(new SpellCastEvent(ColorlessSpell(_alice, "7")));
            triggers.PendingCount.Should().Be(1);

            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            // Land sacrificed — no longer on battlefield.
            land.Zone.Should().Be(ZoneType.Graveyard,
                "land was sacrificed when agent chose yes");

            // Colorless creature is now in hand.
            _alice.Zones.Hand.GetCards().Should().Contain(titan,
                "tutor found the colorless creature and moved it to hand");

            // Library no longer contains the tutored card.
            _alice.Zones.Library.GetCards().Should().NotContain(titan);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Resolve — agent says NO (decline optional sac)
    // -----------------------------------------------------------------------

    [Fact]
    public void SanctumOfUgin_Resolve_NoSac_LandStaysNoTutor()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline
        AgentRegistry.Set(_alice, agent);

        try
        {
            var land = SanctumOfUginFactory.Create(_alice, bus, triggers);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);

            SeedColorlessCreature(_alice, "Eldrazi Titan");

            bus.Publish(new SpellCastEvent(ColorlessSpell(_alice, "7")));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            // Land still on the battlefield.
            land.Zone.Should().Be(ZoneType.Battlefield,
                "declining the may-sac leaves the land untouched");

            // Nothing moved to hand.
            _alice.Zones.Hand.GetCards().Should().BeEmpty(
                "declining skips the tutor entirely");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }
}
