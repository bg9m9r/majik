using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Leyline of Resonance (Duskmourn: House of Horror, {2}{R}{R}).
///
/// Oracle text (verified against Scryfall + the embedded seed):
///   "If this card is in your opening hand, you may begin the game with it on
///    the battlefield.
///    Whenever you cast an instant or sorcery spell that targets only a single
///    creature you control, copy that spell. You may choose new targets for the
///    copy."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (exact {2}{R}{R} Enchantment shape) + opening-hand Leyline marker.
///   - Trigger fires when you cast an instant/sorcery targeting only a single
///     creature you control.
///   - Resolution pushes a distinct copy spell onto the stack (CR 707.10).
///   - Negatives: opponent's cast, multi-target spell, single non-creature
///     target, and a creature an opponent controls do NOT trigger.
/// </summary>
[Trait("Color", "R")]
public class LeylineOfResonanceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature NewCreature(Player controller, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Majik.Core.Spells.Spell BuildSpell(Player controller, ITarget target)
    {
        var bolt = new Instant("Lightning Bolt", "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(bolt, controller, new[] { target });
    }

    private static void PlaceOnBattlefield(Enchantment leyline, Player owner)
    {
        owner.Zones.Battlefield.AddCard(leyline);
        leyline.SetZone(ZoneType.Battlefield);
    }

    private (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers, Enchantment leyline)
        WireLeyline(Player owner)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var leyline = LeylineOfResonanceFactory.Create(owner, bus, triggers);
        PlaceOnBattlefield(leyline, owner);
        return (bus, stack, triggers, leyline);
    }

    private static GameContext LiveContext(
        Player self, Player opp, Majik.Core.Stack.Stack stack) =>
        new(self, new[] { self, opp }, self, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

    [Fact]
    public void Leyline_Identity()
    {
        var c = LeylineOfResonanceFactory.Create(_alice);

        c.Name.Should().Be("Leyline of Resonance");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CarriesOpeningHandLeylineMarker()
    {
        var c = LeylineOfResonanceFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == OpeningHandLeylineAlternativeCost.LeylineKeyword);
    }

    [Fact]
    public void CastInstantTargetingYourCreature_Triggers()
    {
        var (bus, _, triggers, _) = WireLeyline(_alice);
        var bear = NewCreature(_alice);

        var spell = BuildSpell(_alice, Target.Permanent(bear));
        bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(1,
            "casting an instant that targets only a single creature you control triggers Resonance");
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolution_PushesADistinctCopyOntoTheStack()
    {
        var (bus, stack, triggers, leyline) = WireLeyline(_alice);
        var bear = NewCreature(_alice);

        var spell = BuildSpell(_alice, Target.Permanent(bear));
        stack.Push(spell);                       // the original spell on the stack
        bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(1);
        stack.Count.Should().Be(1, "only the original spell is on the stack before the trigger resolves");

        var trigger = leyline.Abilities.OfType<TriggeredAbility>().Single();
        await trigger.ResolveAsync(agent: null, LiveContext(_alice, _bob, stack));

        // CR 706.10a / 707.10 — a distinct copy spell now sits above the original.
        stack.Count.Should().Be(2, "the trigger pushes a distinct copy spell onto the stack");
        var copy = stack.Top.Should().BeOfType<Majik.Core.Spells.Spell>().Subject;
        copy.IsCopy.Should().BeTrue("the pushed object is a copy (CR 707)");
        copy.Should().NotBeSameAs(spell, "the copy is its own IStackObject");
        copy.Controller.Should().BeSameAs(_alice, "the copy is controlled by Resonance's controller (CR 707.10)");
    }

    [Fact]
    public void OpponentsCast_DoesNotTrigger()
    {
        var (bus, _, triggers, _) = WireLeyline(_alice);
        var bobBear = NewCreature(_bob);

        // Bob casts, targeting his own creature — "you cast" gate fails.
        var spell = BuildSpell(_bob, Target.Permanent(bobBear));
        bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(0,
            "Resonance only fires for a spell YOU cast (CR 109.5)");
    }

    [Fact]
    public void TargetsACreatureAnOpponentControls_DoesNotTrigger()
    {
        var (bus, _, triggers, _) = WireLeyline(_alice);
        var bobBear = NewCreature(_bob);

        // Alice casts targeting Bob's creature — not "a creature you control".
        var spell = BuildSpell(_alice, Target.Permanent(bobBear));
        bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(0,
            "the single target must be a creature YOU control");
    }

    [Fact]
    public void TargetsYouThePlayer_DoesNotTrigger()
    {
        var (bus, _, triggers, _) = WireLeyline(_alice);

        // A single non-creature target (a player) — not "a single creature".
        var spell = BuildSpell(_alice, Target.Player(_alice));
        bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(0,
            "the sole target must be a creature, not a player");
    }

    [Fact]
    public void TargetsTwoCreatures_DoesNotTrigger()
    {
        var (bus, _, triggers, _) = WireLeyline(_alice);
        var bear1 = NewCreature(_alice, "Grizzly Bears");
        var bear2 = NewCreature(_alice, "Runeclaw Bear");

        // Two targets — "targets ONLY a single creature" fails.
        var bolt = new Instant("Twin Bolt", "1R") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(
            bolt, _alice, new[] { Target.Permanent(bear1), Target.Permanent(bear2) });
        bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(0,
            "a spell with more than one target does not qualify (CR 115 — 'only a single')");
    }
}
