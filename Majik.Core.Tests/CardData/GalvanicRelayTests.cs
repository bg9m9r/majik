using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Galvanic Relay (Strixhaven: Mystical Archive, {2}{R}, Sorcery).
///
/// Oracle: "Exile the top X cards of your library, where X is the number
/// of times you've cast this spell from your hand this turn. Until the end
/// of your next turn, you may play those cards. Storm (When you cast this
/// spell, copy it for each spell cast before it this turn.)"
///
/// v1 implements Storm + exile-top-X (X = name-keyed cast count by
/// controller this turn, from any zone). The may-play-from-exile rider is
/// deferred (see factory xmldoc).
/// </summary>
public class GalvanicRelayTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Sorcery($"Filler {i}", "{R}");
            c.SetOwner(p);
            c.SetController(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_2R()
    {
        var g = GalvanicRelayFactory.Create(_alice);

        g.Name.Should().Be("Galvanic Relay");
        g.HasType(CardType.Sorcery).Should().BeTrue();
        g.ManaCost.Should().Be("{2}{R}");
        g.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(g).Should().Contain(ManaColor.Red);
        g.Owner.Should().BeSameAs(_alice);
        g.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsRelayShape()
    {
        var dispatched = NamedCardFactory.Create("Galvanic Relay", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Galvanic Relay");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{2}{R}");
    }

    // ---------------------------------------------------------------
    // Structural shape — Storm trigger attached
    // ---------------------------------------------------------------

    [Fact]
    public void Card_HasStructuralStormTrigger()
    {
        var g = GalvanicRelayFactory.Create(_alice);

        var triggers = g.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Galvanic Relay prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(g);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.40a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    // ---------------------------------------------------------------
    // Resolve — exile top X cards using a constant-X tally
    // ---------------------------------------------------------------

    [Fact]
    public void BuildDefinition_ExilesTopXCardsOfControllersLibrary()
    {
        SeedLibrary(_alice, 5);

        var def = GalvanicRelayFactory.BuildDefinition(_alice, getXFn: () => 3);

        def.TargetRequests.Should().BeEmpty();
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();

        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().HaveCount(3,
            "X = 3 → exile the top 3 cards of the controller's library");
        _alice.Zones.Library.GetCards().Should().HaveCount(2,
            "5 - 3 = 2 cards remain in the controller's library");
    }

    [Fact]
    public void BuildDefinition_XZero_ExilesNothing()
    {
        SeedLibrary(_alice, 5);

        var def = GalvanicRelayFactory.BuildDefinition(_alice, getXFn: () => 0);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().BeEmpty(
            "first cast of Galvanic Relay with no prior casts → X = 0 (or one, see name tally tests)");
        _alice.Zones.Library.GetCards().Should().HaveCount(5);
    }

    [Fact]
    public void BuildDefinition_XGreaterThanLibrary_ExilesWhatIsAvailable()
    {
        SeedLibrary(_alice, 2);

        var def = GalvanicRelayFactory.BuildDefinition(_alice, getXFn: () => 5);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().HaveCount(2,
            "library only has 2 cards — exile stops short silently per CR 117.x");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // Name-cast tally — increments on Galvanic Relay casts by controller
    // ---------------------------------------------------------------

    [Fact]
    public void NameCastTally_IncrementsOnEachControllerRelayCast()
    {
        var bus = new EventBus();
        var counter = GalvanicRelayFactory.BuildNameCastTally(_alice, bus);

        counter().Should().Be(0, "no Galvanic Relay casts yet this turn");

        // Cast Galvanic Relay once.
        var relay1 = GalvanicRelayFactory.Create(_alice);
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(relay1, _alice)));
        counter().Should().Be(1);

        // Cast a different spell — must not increment.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(bolt, _alice)));
        counter().Should().Be(1);

        // Cast Galvanic Relay again.
        var relay2 = GalvanicRelayFactory.Create(_alice);
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(relay2, _alice)));
        counter().Should().Be(2);
    }

    [Fact]
    public void NameCastTally_DoesNotCountOpponentRelayCasts()
    {
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var counter = GalvanicRelayFactory.BuildNameCastTally(_alice, bus);

        var bobRelay = GalvanicRelayFactory.Create(bob);
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(bobRelay, bob)));

        counter().Should().Be(0, "the tally is scoped to Alice's casts only");
    }
}
