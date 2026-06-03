using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Mardu Woe-Reaper (Fate Reforged, {W}).
///
/// Creature — Human Warrior 2/1. Oracle text (current Scryfall):
///   "Whenever this creature or another Warrior you control enters, you may
///   exile target creature card from a graveyard. If you do, you gain 1 life."
///
/// Exercises the subtype-gated ETB-of-another trigger (CR 603.6e —
/// <c>whenever_another_creature_enters</c> with <c>subtype: "Warrior"</c> +
/// <c>includeSelf: true</c> + <c>youControlOnly: true</c>) AND the new
/// declarative "you may exile target creature card from a graveyard. If you do,
/// you gain 1 life." payoff (CR 701.21 exile / CR 119.3 lifegain).
/// </summary>
[Trait("Color", "W")]
public class MarduWoeReaperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MarduWoeReaper_Identity()
    {
        var c = MarduWoeReaperFactory.Create(_alice);

        c.Name.Should().Be("Mardu Woe-Reaper");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MarduWoeReaper_AnotherWarriorYouControlEnters_TriggerMatches()
    {
        var reaper = MarduWoeReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);
        reaper.SetController(_alice);

        var warrior = new Creature("Goblin Warrior", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });
        warrior.SetOwner(_alice);
        warrior.SetController(_alice);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(warrior, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "a Warrior you control entering fires the subtype-gated trigger");
    }

    [Fact]
    public void MarduWoeReaper_SelfEnters_TriggerMatches()
    {
        var reaper = MarduWoeReaperFactory.Create(_alice);
        reaper.SetController(_alice);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(reaper, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "'this creature or another Warrior' includes the source's own entry (includeSelf)");
    }

    [Fact]
    public void MarduWoeReaper_NonWarriorEnters_DoesNotTrigger()
    {
        var reaper = MarduWoeReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);
        reaper.SetController(_alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "a non-Warrior creature does not fire the subtype-gated trigger");
    }

    [Fact]
    public void MarduWoeReaper_OpponentWarriorEnters_DoesNotTrigger()
    {
        var reaper = MarduWoeReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);
        reaper.SetController(_alice);

        var oppWarrior = new Creature("Bob's Warrior", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Warrior });
        oppWarrior.SetOwner(_bob);
        oppWarrior.SetController(_bob);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(oppWarrior, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "'a Warrior you control' excludes an opponent's Warrior");
    }

    [Fact]
    public void MarduWoeReaper_DeclaresOneOptionalTarget()
    {
        var reaper = MarduWoeReaperFactory.Create(_alice);
        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].MinTargets.Should().Be(0,
            "'you MAY exile target creature card from a graveyard' is optional");
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void MarduWoeReaper_OnResolve_ExilesChosenCardAndGainsOneLife()
    {
        var reaper = MarduWoeReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);

        // A creature card sitting in Bob's graveyard.
        var corpse = new Creature("Dead Bear", "{1}{G}", 2, 2);
        corpse.SetOwner(_bob);
        corpse.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(corpse);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { corpse } });
        trigger.Resolve();

        corpse.Zone.Should().Be(ZoneType.Exile, "the chosen creature card is exiled (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(corpse);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(corpse);
        _alice.LifeTotal.Should().Be(21, "'If you do, you gain 1 life' (CR 119.3)");
    }

    [Fact]
    public void MarduWoeReaper_OnResolve_NoTargetChosen_NoExileNoLifegain()
    {
        var reaper = MarduWoeReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        // The "may" declined — no target chosen.
        trigger.SetChosenTargets(System.Array.Empty<IReadOnlyList<object>>());
        trigger.Resolve();

        _alice.LifeTotal.Should().Be(20,
            "declining the optional exile means no card is exiled, so no life is gained");
    }
}
