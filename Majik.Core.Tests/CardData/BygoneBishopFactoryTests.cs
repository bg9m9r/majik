using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bygone Bishop (Shadows over Innistrad, {2}{W},
/// Creature — Spirit Cleric 2/3).
///
/// Covers:
///   - Card identity (name, type, subtypes Spirit + Cleric, P/T, mana cost,
///     owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch returns the same shape.
///   - Flying keyword marker attached + one triggered ability.
///   - Casting a creature with mana value ≤ 3 → exactly one Clue token
///     created under controller.
///   - Casting a creature with mana value &gt; 3 → no Clue.
///   - Casting a non-creature spell (instant / sorcery) → no Clue.
///   - Opponent casting a small creature → no Clue (controller gate).
///   - Casting two small creatures → two Clues (additive).
/// </summary>
public class BygoneBishopFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewCreatureSpell(
        Player controller, string name, string manaCost, int power = 1, int toughness = 1)
    {
        var c = new Creature(name, manaCost, power, toughness) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var i = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(i, controller);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BygoneBishop_Identity_SpiritCleric23At2W()
    {
        var b = BygoneBishopFactory.Create(_alice);

        b.Name.Should().Be("Bygone Bishop");
        b.ManaCost.Should().Be("{2}{W}");
        b.HasType(CardType.Creature).Should().BeTrue();
        b.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        b.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        b.BasePower.Should().Be(2);
        b.BaseToughness.Should().Be(3);
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BygoneBishop()
    {
        var card = NamedCardFactory.Create("Bygone Bishop", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bygone Bishop");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void BygoneBishop_HasFlyingKeyword()
    {
        var b = BygoneBishopFactory.Create(_alice);
        b.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
    }

    // -----------------------------------------------------------------------
    // Investigate trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void CastSmallCreature_CreatesOneClueToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bishop = BygoneBishopFactory.Create(_alice, triggers);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        // Cast a 2-mana creature (mv = 2 ≤ 3).
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears", "{1}{G}")));

        triggers.PendingCount.Should().Be(1, "small creature spell triggers Bygone Bishop");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var clues = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Clue))
            .ToList();
        clues.Should().HaveCount(1);
        clues[0].IsToken.Should().BeTrue();
    }

    [Fact]
    public void CastThreeManaCreature_StillTriggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bishop = BygoneBishopFactory.Create(_alice, triggers);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Watchwolf", "{G}{W}{1}", 3, 3)));

        // mv = 3 satisfies "≤ 3" gate.
        triggers.PendingCount.Should().Be(1, "mv == 3 satisfies the ≤ 3 gate");
    }

    [Fact]
    public void CastBigCreature_NoTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bishop = BygoneBishopFactory.Create(_alice, triggers);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        // mv = 4 — over the gate.
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Siege Rhino", "{1}{W}{B}{G}", 4, 5)));

        triggers.PendingCount.Should().Be(0, "mv > 3 fails Bygone Bishop's gate");
    }

    [Fact]
    public void CastInstant_NoTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bishop = BygoneBishopFactory.Create(_alice, triggers);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(0,
            "non-creature spells don't trigger Bygone Bishop");
    }

    [Fact]
    public void OpponentCastsSmallCreature_NoTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bishop = BygoneBishopFactory.Create(_alice, triggers);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        // Bob casts a small creature — should NOT trigger ("you cast").
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_bob, "Bob's Bear", "{1}{G}")));

        triggers.PendingCount.Should().Be(0, "opponent's creature spell doesn't trigger \"you cast\"");
    }

    [Fact]
    public void TwoSmallCreatures_TwoClues()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bishop = BygoneBishopFactory.Create(_alice, triggers);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Bear 1", "{1}{G}")));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Bear 2", "{1}{G}")));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        var clues = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Clue))
            .ToList();
        clues.Should().HaveCount(2, "two small-creature casts → two Clues");
    }
}
