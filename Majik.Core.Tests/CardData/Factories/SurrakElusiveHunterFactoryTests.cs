using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SurrakElusiveHunterFactory"/>.
///
/// Surrak, Elusive Hunter (Tarkir: Dragonstorm, {2}{G}). Legendary Creature —
/// Human Warrior 4/3. Oracle (verified against Scryfall 2026-06-23):
///   "This spell can't be countered.
///    Trample
///    Whenever a creature you control or a creature spell you control becomes
///    the target of a spell or ability an opponent controls, draw a card."
///
/// Coverage of the card's UNIQUE behaviour:
/// - Identity (name, supertype/types/subtypes, cost, colour, P/T).
/// - "This spell can't be countered" marker (CR 701.5b — Uncounterable).
/// - Trample (CR 702.19).
/// - Becomes-the-target draw trigger (CR 603.6c / 115.6):
///   fires off an opponent's spell/ability targeting a creature you control OR
///   a creature spell you control; does NOT fire off your own spell, nor off an
///   opponent targeting their own creature.
/// </summary>
[Trait("Color", "G")]
public class SurrakElusiveHunterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player controller, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2) { Owner = controller };
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void StockLibrary(Player p, int n = 1)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Creature($"Forest {i}", "{G}", 1, 1);
            c.SetOwner(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }

    // ── Identity / markers ──────────────────────────────────────────────

    [Fact]
    public void Surrak_Identity()
    {
        var c = SurrakElusiveHunterFactory.Create(_alice);

        c.Name.Should().Be("Surrak, Elusive Hunter");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{G}");
        c.ManaCostValue.TotalValue.Should().Be(3);
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Surrak_HasUncounterableAndTrampleMarkers()
    {
        var c = SurrakElusiveHunterFactory.Create(_alice);

        // CR 701.5b — "This spell can't be countered" (SpellCastFlow reads this).
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Uncounterable");
        // CR 702.19 — Trample.
        CombatAbilities.HasTrample(c).Should().BeTrue();
    }

    // ── Becomes-the-target draw trigger ─────────────────────────────────

    [Fact]
    public void Surrak_OpponentTargetsYourCreature_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        StockLibrary(_alice);

        var surrak = SurrakElusiveHunterFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(surrak);
        surrak.SetZone(ZoneType.Battlefield);

        var mine = NewCreature(_alice, "Llanowar Elves");

        // Bob (opponent) casts Lightning Bolt targeting Alice's creature.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(mine) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent targeting a creature Alice controls fires Surrak's draw trigger");

        var trigger = surrak.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e =>
                e.Description.Contains("draw", System.StringComparison.OrdinalIgnoreCase)));
        var handBefore = _alice.Zones.Hand.Count;
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.Count.Should().Be(handBefore + 1,
            "the trigger draws a card for Surrak's controller (CR 120.2).");
    }

    [Fact]
    public void Surrak_OpponentTargetsYourCreatureSpell_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var surrak = SurrakElusiveHunterFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(surrak);
        surrak.SetZone(ZoneType.Battlefield);

        // Alice has a creature SPELL on the stack (e.g. casting a Bear).
        var bearCard = new Creature("Runeclaw Bear", "{1}{G}", 2, 2) { Owner = _alice };
        var creatureSpell = new Majik.Core.Spells.Spell(bearCard, _alice);

        // Bob (opponent) casts a counterspell targeting Alice's creature spell.
        var negate = new Instant("Essence Scatter", "{1}{U}") { Owner = _bob };
        var counterSpell = new Majik.Core.Spells.Spell(
            negate, _bob, new[] { Target.Spell(creatureSpell) });
        bus.Publish(new TargetsChosenEvent(counterSpell, counterSpell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent targeting a creature SPELL Alice controls fires Surrak's draw trigger (CR 603.6c).");
    }

    [Fact]
    public void Surrak_YourOwnSpellTargetsYourCreature_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var surrak = SurrakElusiveHunterFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(surrak);
        surrak.SetZone(ZoneType.Battlefield);

        var mine = NewCreature(_alice, "Llanowar Elves");

        // Alice (the controller) targets her own creature — not "an opponent".
        var pump = new Instant("Giant Growth", "{G}") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(pump, _alice, new[] { Target.Permanent(mine) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the trigger only fires off a spell or ability an OPPONENT controls (CR 109.5).");
    }

    [Fact]
    public void Surrak_OpponentTargetsTheirOwnCreature_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var surrak = SurrakElusiveHunterFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(surrak);
        surrak.SetZone(ZoneType.Battlefield);

        var theirs = NewCreature(_bob, "Goblin Guide");

        // Bob targets HIS OWN creature — not "a creature you control" (Alice).
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(theirs) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the trigger requires a creature (or creature spell) ALICE controls to become the target.");
    }
}
