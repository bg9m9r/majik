using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HugsGrislyGuardianFactory"/> (Bloomburrow
/// Commander, <c>{X}{R}{R}{G}{G}</c>). Legendary Creature — Badger Warrior 5/5,
/// Trample.
///
/// Covers ONLY Hugs's unique behaviour (the contract test handles dispatch +
/// well-formedness for every implemented card):
/// - Identity ({X}{R}{R}{G}{G}, 5/5, Legendary Creature — Badger Warrior, Trample).
/// - ETB trigger: exile the top X cards of your library, where X is the cast-time
///   {X} read off <see cref="Card.PendingCastX"/>; stamp the "play until end of
///   your next turn" permission (CR 118.9) on each exiled card.
/// - Shallow library: exiles what's available, stamps only those.
/// - X = 0 / no PendingCastX: clean no-op.
/// - "Until end of your next turn" cleanup: first Cleanup of the controller's
///   current turn keeps the grant; second (the controller's next turn) clears it.
/// - Extra-land static: +1 land play (CR 720) while on the battlefield.
/// </summary>
[Trait("Color", "M")]
public class HugsGrislyGuardianFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Card NewCardInLibrary(string name, string cost = "{R}")
    {
        var c = new Card(name, cost);
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static TriggeredAbility EtbTrigger(Creature hugs) =>
        hugs.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield));

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Hugs_Identity_LegendaryBadgerWarrior_5_5_WithTrample()
    {
        var hugs = HugsGrislyGuardianFactory.Create(_alice);

        hugs.Name.Should().Be("Hugs, Grisly Guardian");
        hugs.ManaCost.Should().Be("{X}{R}{R}{G}{G}");
        hugs.HasType(CardType.Creature).Should().BeTrue();
        hugs.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Hugs is Legendary");
        hugs.HasSubtype(CardSubtype.Badger).Should().BeTrue();
        hugs.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        hugs.BasePower.Should().Be(5);
        hugs.BaseToughness.Should().Be(5);
        hugs.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Trample").Should().BeTrue("Hugs has Trample");
    }

    // -----------------------------------------------------------------------
    // Extra-land static
    // -----------------------------------------------------------------------

    [Fact]
    public void Hugs_GrantsOneAdditionalLandPlay()
    {
        var hugs = HugsGrislyGuardianFactory.Create(_alice);

        // CR 720 — "You may play an additional land on each of your turns."
        // Summed live by LandDropTracker; +1 (Azusa is +2).
        hugs.AdditionalLandPlaysGranted.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — exile top X, grant play-until-end-of-next-turn
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_ExilesTopX_AndGrantsPlayToController()
    {
        var hugs = HugsGrislyGuardianFactory.Create(_alice);
        hugs.SetPendingCastX(3); // X = 3 paid at cast time

        var top1 = NewCardInLibrary("Top1", "{R}");
        var top2 = NewCardInLibrary("Top2", "{1}{R}");
        var top3 = NewCardInLibrary("Top3", "{2}{R}");
        var top4 = NewCardInLibrary("Top4", "{3}{R}");

        EtbTrigger(hugs).Resolve();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2, top3 });
        _alice.Zones.Exile.GetCards().Should().NotContain(top4);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top4 });

        // "you may play those cards" — the runtime exile-cast grant (CR 118.9)
        // nominates the controller.
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top3.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Etb_ShallowLibrary_ExilesAvailable_NoExtraGrants()
    {
        var hugs = HugsGrislyGuardianFactory.Create(_alice);
        hugs.SetPendingCastX(5); // X = 5 but only 2 cards in library

        var top1 = NewCardInLibrary("Top1");
        var top2 = NewCardInLibrary("Top2");

        EtbTrigger(hugs).Resolve();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Etb_XZero_NoExile_NoThrow()
    {
        var hugs = HugsGrislyGuardianFactory.Create(_alice);
        // No SetPendingCastX → PendingCastX null → X = 0.

        NewCardInLibrary("Top1");

        var act = () => EtbTrigger(hugs).Resolve();

        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty("X = 0 exiles nothing");
        _alice.Zones.Library.GetCards().Should().HaveCount(1, "library untouched");
    }

    // -----------------------------------------------------------------------
    // "Until end of your next turn" cleanup via Cleanup-step counting
    // -----------------------------------------------------------------------

    [Fact]
    public void EotCleanup_FirstCleanupKeepsGrant_SecondClears()
    {
        var bus = new EventBus();
        var hugs = HugsGrislyGuardianFactory.Create(_alice, bus, triggers: null);
        hugs.SetPendingCastX(1);

        var top = NewCardInLibrary("Top", "{R}");

        EtbTrigger(hugs).Resolve();
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // 1st Cleanup — controller's current turn (Hugs entered this turn).
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first cleanup belongs to the controller's current turn — grant persists");

        // Bob's intervening cleanup is not 'your next turn'.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _bob));
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Bob's cleanup is not Alice's next turn — grant survives");

        // 2nd Cleanup belonging to Alice — her next turn. Grant clears.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        top.RuntimeExileCastAllowedCaster.Should().BeNull(
            "second cleanup belonging to the controller = end of their next turn — grant cleared");
    }
}
