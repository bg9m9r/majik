using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="StarPupilFactory"/> (March of the Machine, {W}).
///
/// Card: Star Pupil — Creature — Human Wizard {W} 0/0.
///   "This creature enters with a +1/+1 counter on it.
///    When this creature dies, put its counters on target creature you control."
///
/// Covers the card's UNIQUE behaviour vs the Servant of the Scale analogue:
///   - Identity (printed 0/0, {W}, white Human Wizard).
///   - Single mandatory dies trigger structure (target creature you control).
///   - Enters-with-counter via ZoneService + ReplacementBus (CR 614.1d).
///   - Dies: moves ALL of its counters — every counter type, not just an
///     X-count of +1/+1 (CR 122 / CR 700.4 "its counters") — onto the target
///     (CR 608.2g last-known-information).
///   - Dies with no counters → no-op.
/// </summary>
[Trait("Color", "W")]
public class StarPupilFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void EnterBattlefield(Creature card, Player owner, ReplacementBus bus)
    {
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private static Creature TargetCreature(Player owner)
    {
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(owner);
        grizzly.SetController(owner);
        grizzly.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(grizzly);
        return grizzly;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StarPupil_Identity()
    {
        var c = StarPupilFactory.Create(_alice);

        c.Name.Should().Be("Star Pupil");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.Power.Should().Be(0, "printed 0/0 — the +1/+1 ETB counter makes it a 1/1 on the battlefield");
        c.Toughness.Should().Be(0);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StarPupil_HasSingleMandatoryTargetDiesTrigger()
    {
        var c = StarPupilFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "single dies trigger");

        var dies = triggers[0];
        dies.TargetRequests.Should().HaveCount(1);
        dies.TargetRequests[0].MinTargets.Should().Be(1,
            "printed 'target creature you control' is a mandatory single target (CR 601.2c)");
        dies.TargetRequests[0].MaxTargets.Should().Be(1);
        dies.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // Enters-with-counter (CR 614.1d / CR 122.1g)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersWithOnePlusOnePlusOneCounter()
    {
        var bus = new ReplacementBus();
        var card = StarPupilFactory.Create(_alice, triggers: null, replacements: bus);

        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Star Pupil enters with one +1/+1 counter on it (CR 614.1d)");
    }

    [Fact]
    public void NoReplacementBus_EntersVanilla()
    {
        var bus = new ReplacementBus();
        var card = StarPupilFactory.Create(_alice); // no replacement bus wired

        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no enters-with-counter replacement registered on the shape path → no counter");
    }

    // -----------------------------------------------------------------------
    // Dies trigger (CR 603.6c / CR 700.4) — moves ALL counters, every type
    // -----------------------------------------------------------------------

    [Fact]
    public void Dies_MovesAllCounters_EveryType_OntoTarget()
    {
        var pupil = StarPupilFactory.Create(_alice);
        // Star Pupil has accumulated a mix of counter types (ETB +1/+1 plus an
        // external pump and, say, a stun counter). "Put its counters" moves the
        // whole bag, every type, 1:1 — unlike Servant which moves only +1/+1.
        pupil.Counters.Add(CounterType.PlusOnePlusOne, 3);
        pupil.Counters.Add(CounterType.Stun, 2);

        // Star Pupil has died — already in the graveyard (CR 608.2g LKI; counters
        // persist on the card object until the next cleanup step, CR 514.2).
        pupil.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(pupil);

        var grizzly = TargetCreature(_alice);

        var dies = pupil.Abilities.OfType<TriggeredAbility>().Single();
        dies.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var e in dies.Effects) e.Execute();

        grizzly.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "all +1/+1 counters move from the dying Star Pupil onto the target");
        grizzly.Counters.Count(CounterType.Stun).Should().Be(2,
            "non-+1/+1 counters move too — 'its counters' means every counter type (CR 122)");
    }

    [Fact]
    public void Dies_NoCounters_NoOp()
    {
        var pupil = StarPupilFactory.Create(_alice);
        // No counters on Star Pupil (never landed via ETB-counter path in this
        // shape test) → nothing to move, even with a valid target.
        pupil.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(pupil);

        var grizzly = TargetCreature(_alice);

        var dies = pupil.Abilities.OfType<TriggeredAbility>().Single();
        dies.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var e in dies.Effects) e.Execute();

        grizzly.Counters.HasAny.Should().BeFalse(
            "no counters on Star Pupil → none placed on the target");
    }
}
