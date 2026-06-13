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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EndlessOneFactory"/> (Battle for Zendikar, {X}).
///
/// Covers:
/// - Identity (Creature — Eldrazi, {X}, 0/0, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="Card.ManaCostValue.HasX"/> reports true (X-cost spell).
/// - "Enters with X +1/+1 counters" is owned by the generic
///   <see cref="EntersWithCountersBinder"/> (NOT a self-managed ETB trigger):
///   the factory attaches no ETB trigger and does not self-manage; the binder
///   registers a variable-X replacement that reads
///   <see cref="Card.PendingCastX"/> and places the counters AS the permanent
///   enters (CR 614.1d). X=3 → 3 counters; X=0 → 0/0; Hardened Scales bumps.
/// </summary>
public class EndlessOneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EndlessOne_Identity()
    {
        var eo = EndlessOneFactory.Create(_alice);

        eo.Name.Should().Be("Endless One");
        eo.ManaCost.Should().Be("{X}");
        eo.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        eo.HasType(CardType.Creature).Should().BeTrue();
        eo.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        eo.BasePower.Should().Be(0);
        eo.BaseToughness.Should().Be(0);
        eo.Owner.Should().BeSameAs(_alice);
        eo.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EndlessOne_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Endless One", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Endless One");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(0);
        ((Creature)card).BaseToughness.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // ETB X +1/+1 counters: "Endless One enters with X +1/+1 counters on it"
    // (CR 614.1d / CR 202.3b). This is NOT a factory-attached ETB trigger. The
    // factory defers to the generic EntersWithCountersBinder, which on the prod
    // deck-build (DeckCardBuilder APPROACH B → OverlayAdditiveBinders)
    // registers a variable-X EntersWithCountersReplacement that reads
    // PendingCastX and places the counters AS the permanent enters. These tests
    // exercise that exact prod mechanism: build the factory card, run the
    // binder against its real oracle text, then move it onto the battlefield
    // through ZoneService and assert the counters landed.
    //
    // Earlier this card self-managed via an ETB trigger + the
    // MarkSelfManagesEntersWithCounters flag; that produced ZERO counters on
    // the Approach-B route (the trigger was never registered with a live
    // TriggerManager and the flag suppressed the binder). The factory must NOT
    // attach an ETB trigger NOR self-manage — both regression-guarded here.
    // -----------------------------------------------------------------------

    private static CardEntity EndlessOneEntity() =>
        new EmbeddedCardRepository().GetByName("Endless One")!;

    [Fact]
    public void EndlessOne_DoesNotAttachEtbTrigger()
    {
        // CR 614.1d — the ETB counters are a binder-registered replacement, NOT
        // a factory-attached TriggeredAbility. Self-managing via a trigger was
        // the bug: the prod Approach-B route never registers it.
        var eo = EndlessOneFactory.Create(_alice);

        eo.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Endless One's ETB counters are a binder-registered replacement, " +
            "not a self-managed ETB trigger");
    }

    [Fact]
    public void EndlessOne_DoesNotSelfManageEntersWithCounters()
    {
        // The factory must leave SelfManagesEntersWithCounters false so the
        // EntersWithCountersBinder DOES register the variable-X replacement on
        // the prod route. Setting the flag suppresses the binder → 0 counters.
        var eo = EndlessOneFactory.Create(_alice);

        eo.SelfManagesEntersWithCounters.Should().BeFalse(
            "the binder owns the ETB-X replacement; self-managing suppresses it " +
            "and yields zero counters on the Approach-B prod route");
    }

    [Fact]
    public void EndlessOne_BinderReplacement_EntersWithXEquals3_Counters()
    {
        // The prod mechanism: factory build + binder (reads the card's real
        // oracle text) + ZoneService move. X = 3 (cast {3}).
        var bus = new ReplacementBus();
        var eo = EndlessOneFactory.Create(_alice);

        EntersWithCountersBinder.Bind(eo, EndlessOneEntity(), bus).Should().BeTrue(
            "the binder matches 'enters with X +1/+1 counters on it' and registers " +
            "the variable-X replacement");

        eo.SetOwner(_alice);
        eo.SetController(_alice);
        _alice.Zones.Library.AddCard(eo);
        eo.SetZone(ZoneType.Library);
        eo.SetPendingCastX(3);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(eo, ZoneType.Library, ZoneType.Battlefield, _alice);

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Endless One enters WITH X (=3) +1/+1 counters per CR 614.1d → 3/3");
        eo.BasePower.Should().Be(0, "base P/T is unchanged; counters add via Layer 7");
        eo.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void EndlessOne_BinderReplacement_ZeroX_NoCounters()
    {
        // No PendingCastX stamp → X = 0 → a 0/0 the SBA layer sends to the
        // graveyard (CR 704.5f). Non-cast entries (blink, copy) take this path.
        var bus = new ReplacementBus();
        var eo = EndlessOneFactory.Create(_alice);

        EntersWithCountersBinder.Bind(eo, EndlessOneEntity(), bus).Should().BeTrue();

        eo.SetOwner(_alice);
        eo.SetController(_alice);
        _alice.Zones.Library.AddCard(eo);
        eo.SetZone(ZoneType.Library);
        // No SetPendingCastX → X defaults to 0.

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(eo, ZoneType.Library, ZoneType.Battlefield, _alice);

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X = 0 → zero counters placed → 0/0 SBA-fodder (CR 704.5f)");
    }

    [Fact]
    public void EndlessOne_BinderReplacement_HardenedScalesBumpsApply()
    {
        // Hardened Scales bumps the +1/+1 counters AS they enter — it observes
        // the same ZoneMoveIntent.PlusOneCountersOnEnter channel the ETB-X
        // replacement stamps (CR 614). Wire a +1 bump on that channel, cast for
        // X = 2, expect 3 counters.
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<ZoneMoveIntent>(
            applies: (intent, _) => intent.ToZone == ZoneType.Battlefield
                                    && intent.PlusOneCountersOnEnter >= 1,
            replace: (intent, _) => intent with
            {
                PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + 1,
            }));

        var eo = EndlessOneFactory.Create(_alice);
        EntersWithCountersBinder.Bind(eo, EndlessOneEntity(), bus).Should().BeTrue();

        eo.SetOwner(_alice);
        eo.SetController(_alice);
        _alice.Zones.Library.AddCard(eo);
        eo.SetZone(ZoneType.Library);
        eo.SetPendingCastX(2);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(eo, ZoneType.Library, ZoneType.Battlefield, _alice);

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hardened Scales (+1 on the ETB +1/+1 intent channel) bumps X (=2) → 3");
    }
}
