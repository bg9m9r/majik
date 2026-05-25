using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="EidolonOfTheGreatRevelFactory"/> (Journey into Nyx,
/// {R}{R}). Oracle: "Whenever a player casts a spell with mana value 3 or
/// less, Eidolon of the Great Revel deals 2 damage to that player."
///
/// Covers:
/// - Identity (Spirit 2/2, mana cost {R}{R}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Trigger fires for any player (controller + opponent) on MV &lt;= 3.
/// - Trigger does NOT fire on MV &gt; 3.
/// - Resolution deals 2 damage to the spell's caster (LifeLostThisTurn
///   ticks up — feeds Spectacle / Revolt).
/// - Trigger only active on the battlefield.
/// </summary>
public class EidolonOfTheGreatRevelTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewSpell(Player controller, string name, string manaCost)
    {
        var c = new Instant(name, manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static void PlaceOnBattlefield(Player controller, Creature card)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void Eidolon_Identity_Spirit_2_2_AtCostRR()
    {
        var card = EidolonOfTheGreatRevelFactory.Create(_alice);

        card.Name.Should().Be("Eidolon of the Great Revel");
        card.ManaCost.Should().Be("{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Eidolon_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Eidolon of the Great Revel", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Eidolon of the Great Revel");
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
    }

    [Fact]
    public void Eidolon_HasSingleTriggeredAbility()
    {
        var card = EidolonOfTheGreatRevelFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------
    // Trigger behaviour
    // -------------------------------------------------------------------

    [Fact]
    public void OpponentCastsMv3Spell_TriggersAndDeals2ToOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var eidolon = EidolonOfTheGreatRevelFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, eidolon);

        // Bob casts a 3-MV spell ({1}{R}{R}).
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "BoltX", "{1}{R}{R}")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(18, "Eidolon deals 2 damage to the caster on MV<=3");
        _bob.LifeLostThisTurn.Should().Be(2, "the loss feeds Spectacle / Revolt");
    }

    [Fact]
    public void ControllerCastsMv1Spell_TriggersAndDealsDamageToController()
    {
        // Eidolon's oracle is "a player" — no controller exclusion. Its
        // controller's own cheap spells also bounce the damage back.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var eidolon = EidolonOfTheGreatRevelFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, eidolon);

        // Alice casts Lightning Bolt ({R}).
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "Lightning Bolt", "{R}")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(18, "the controller's own cheap spell still triggers Eidolon");
    }

    [Fact]
    public void ExpensiveSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var eidolon = EidolonOfTheGreatRevelFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, eidolon);

        // Bob casts a 4-MV spell ({2}{R}{R}).
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "BigBolt", "{2}{R}{R}")));

        triggers.PendingCount.Should().Be(0, "MV 4 is above the 'mana value 3 or less' threshold");
    }

    [Fact]
    public void Mv0Spell_StillTriggers()
    {
        // Zero-cost spells (e.g. {0} cantrips like Mishra's Bauble) are
        // still "mana value 3 or less".
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var eidolon = EidolonOfTheGreatRevelFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, eidolon);

        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Mishra's Bauble", "{0}")));

        triggers.PendingCount.Should().Be(1, "MV 0 is <=3");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        _bob.LifeTotal.Should().Be(18);
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var card = EidolonOfTheGreatRevelFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
