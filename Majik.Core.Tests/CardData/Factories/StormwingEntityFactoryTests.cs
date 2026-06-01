using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Stormwing Entity (Strixhaven, {3}{U}{U}).
///
/// Card: Creature — Elemental 3/3 (verified against Scryfall).
///   "This spell costs {2}{U} less to cast if you've cast an instant or
///    sorcery spell this turn.
///    Flying
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    When this creature enters, scry 2."
///
/// Covers:
///   - Identity (name, type, subtype, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatcher entry.
///   - Flying keyword marker.
///   - Cost reduction: with no instant/sorcery cast this turn the printed
///     {3}{U}{U} is unchanged; after an instant/sorcery is cast this turn the
///     generic {3} collapses (engine reduces generic only — colored-pip
///     reduction is the documented v1 approximation, see factory remarks).
///   - Prowess wired as a TriggeredAbility when an effects service is
///     supplied; not wired on the single-arg shape-only path.
///   - ETB triggered ability (scry 2) attached.
/// </summary>
public class StormwingEntityFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StormwingEntity_Identity()
    {
        var c = StormwingEntityFactory.Create(_alice);

        c.Name.Should().Be("Stormwing Entity");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StormwingEntity()
    {
        var card = NamedCardFactory.Create("Stormwing Entity", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Stormwing Entity");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{U}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Flying
    // -----------------------------------------------------------------------

    [Fact]
    public void StormwingEntity_HasFlyingMarker()
    {
        var c = StormwingEntityFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying")
            .Should().BeTrue("Stormwing Entity has Flying");
    }

    // -----------------------------------------------------------------------
    // Cost reduction (CR 117.7) — "{2}{U} less if you've cast an instant or
    // sorcery this turn"
    // -----------------------------------------------------------------------

    [Fact]
    public void CostReduction_NoInstantOrSorceryCastThisTurn_FullPrice()
    {
        var bus = new EventBus();
        var card = StormwingEntityFactory.Create(_alice, effects: null, eventBus: bus, triggers: null);

        // No instant/sorcery cast yet → printed {3}{U}{U}.
        var cost = CostReduction.GetEffectiveCost(card, _alice);
        cost.Generic.Should().Be(3, "no instant/sorcery cast this turn → no discount");
        cost.Blue.Should().Be(2);
    }

    [Fact]
    public void CostReduction_AfterInstantCastThisTurn_GenericReduced()
    {
        var bus = new EventBus();
        var card = StormwingEntityFactory.Create(_alice, effects: null, eventBus: bus, triggers: null);

        // Alice casts an instant this turn → the discount turns on.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bus.Publish(new SpellCastEvent(new Spell(bolt, _alice)));

        var cost = CostReduction.GetEffectiveCost(card, _alice);
        // Engine reduces generic only (CR 117.7c floor at colored pips — the
        // documented v1 approximation, same posture as Demilich). The full
        // {2} generic collapses; the {U} portion of "{2}{U} less" cannot be
        // peeled from the printed cost in v1.
        cost.Generic.Should().Be(1, "the {{2}} generic portion of the discount applies");
    }

    [Fact]
    public void CostReduction_OnlyControllerInstantTriggersDiscount()
    {
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var card = StormwingEntityFactory.Create(_alice, effects: null, eventBus: bus, triggers: null);

        // Bob casts an instant — that's not "you" from Alice's perspective.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(bob);
        bolt.SetController(bob);
        bus.Publish(new SpellCastEvent(new Spell(bolt, bob)));

        var cost = CostReduction.GetEffectiveCost(card, _alice);
        cost.Generic.Should().Be(3, "an opponent's instant does not satisfy \"you've cast\"");
    }

    [Fact]
    public void CostReduction_CreatureSpellDoesNotTriggerDiscount()
    {
        var bus = new EventBus();
        var card = StormwingEntityFactory.Create(_alice, effects: null, eventBus: bus, triggers: null);

        // Alice casts a creature spell — not an instant or sorcery.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        bus.Publish(new SpellCastEvent(new Spell(bears, _alice)));

        var cost = CostReduction.GetEffectiveCost(card, _alice);
        cost.Generic.Should().Be(3, "a creature spell does not satisfy the instant/sorcery clause");
    }

    [Fact]
    public void CostReduction_ResetsOnNewTurn()
    {
        var bus = new EventBus();
        var card = StormwingEntityFactory.Create(_alice, effects: null, eventBus: bus, triggers: null);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bus.Publish(new SpellCastEvent(new Spell(bolt, _alice)));
        CostReduction.GetEffectiveCost(card, _alice).Generic.Should().Be(1);

        // New turn — the per-turn flag clears (CR 500.4 / 514 cleanup window).
        bus.Publish(new TurnStartedEvent(_alice, 2));
        CostReduction.GetEffectiveCost(card, _alice).Generic.Should().Be(3,
            "the \"this turn\" flag resets at the start of a new turn");
    }

    // -----------------------------------------------------------------------
    // Prowess
    // -----------------------------------------------------------------------

    [Fact]
    public void SingleArg_ShapeOnly_DoesNotWireProwess()
    {
        var c = StormwingEntityFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Where(t => t != null)
            .Should().NotContain(t => t.Effects.Any(e => e.Description.Contains("prowess")),
                "single-arg dispatcher path is shape-only — no Prowess pump trigger");
    }

    [Fact]
    public void WithEffectsService_AttachesProwessTrigger()
    {
        var effects = new ContinuousEffectsService();
        var c = StormwingEntityFactory.Create(_alice, effects, eventBus: null, triggers: null);

        c.Abilities.OfType<TriggeredAbility>()
            .Any(t => t.Effects.Any(e => e.Description.Contains("prowess")))
            .Should().BeTrue("Prowess wires when ContinuousEffectsService is supplied");
    }

    // -----------------------------------------------------------------------
    // ETB scry 2
    // -----------------------------------------------------------------------

    [Fact]
    public void HasEtbTrigger()
    {
        var c = StormwingEntityFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Any(t => t.Effects.Any(e => e.Description.Contains("scry", System.StringComparison.OrdinalIgnoreCase)))
            .Should().BeTrue("Stormwing Entity has an ETB scry 2 trigger");
    }
}
