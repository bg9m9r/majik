using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Siege-Gang Commander (Onslaught, {3}{R}{R}, Creature — Goblin 2/2).
///
/// Oracle text (Scryfall, verified):
///   "When this creature enters, create three 1/1 red Goblin creature tokens.
///    {1}{R}, Sacrifice a Goblin: This creature deals 2 damage to any target."
///
/// Covers:
/// - Card identity (name, {3}{R}{R}, 2/2, Creature — Goblin, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB triggered ability shape (self-match) and resolution (creates exactly
///   three 1/1 red Goblin tokens under the controller).
/// - Activated ability shape — {1}{R} mana cost + Sacrifice-a-Goblin cost +
///   single any-target request.
/// - Activated ability resolution deals 2 damage to the chosen target; no-op
///   when no target chosen (defensive shape guard).
/// </summary>
public class SiegeGangCommanderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static ActivatedAbility GetSacAbility(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SiegeGangCommander_Identity()
    {
        var c = SiegeGangCommanderFactory.Create(_alice);

        c.Name.Should().Be("Siege-Gang Commander");
        c.ManaCost.Should().Be("{3}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.BasePower.Should().Be(SiegeGangCommanderFactory.Power);
        c.BaseToughness.Should().Be(SiegeGangCommanderFactory.Toughness);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SiegeGangCommander_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Siege-Gang Commander", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Siege-Gang Commander");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape + predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void SiegeGangCommander_HasEtbTrigger_MatchesSelfOnly()
    {
        var c = SiegeGangCommanderFactory.Create(_alice);
        // The ETB trigger is active in the battlefield zone; the CardMovedEvent
        // fires once the card is already on the battlefield (CR 603.6a), so put
        // the source there before probing IsTriggered.
        c.SetZone(ZoneType.Battlefield);
        var trigger = GetEtbTrigger(c);

        trigger.IsTriggered(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeTrue("CR 603.6a — this creature entering triggers its own ETB.");

        var other = new Creature("Mogg Fanatic", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        other.SetOwner(_alice);
        other.SetController(_alice);
        trigger.IsTriggered(new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeFalse("the ETB trigger only fires for Siege-Gang Commander itself.");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — three 1/1 red Goblin tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void SiegeGangCommander_EtbResolution_CreatesThreeGoblinTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var sgc = SiegeGangCommanderFactory.Create(_alice, triggers: null, zoneService: zones);
        _alice.Zones.Battlefield.AddCard(sgc);
        sgc.SetZone(ZoneType.Battlefield);

        var trigger = GetEtbTrigger(sgc);
        foreach (var e in trigger.Effects) e.Execute();

        var spawned = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, sgc))
            .ToList();

        spawned.Should().HaveCount(3, "the ETB creates three 1/1 red Goblin tokens (CR 111).");
        spawned.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Goblin");
            t.BasePower.Should().Be(SiegeGangCommanderFactory.TokenPower);
            t.BaseToughness.Should().Be(SiegeGangCommanderFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
            t.Controller.Should().BeSameAs(_alice);
        });
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SiegeGangCommander_ActivatedAbility_HasManaAndSacrificeCost_AndAnyTarget()
    {
        var sgc = SiegeGangCommanderFactory.Create(_alice);
        var ability = GetSacAbility(sgc);

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            c => c.Cost.Equals(Majik.Core.ValueObjects.ManaCost.Parse("{1}{R}")),
            "the activated ability costs {1}{R} plus a sacrifice");
        ability.Costs.OfType<SacrificeAGoblinCost>().Should().ContainSingle(
            "the activated ability requires sacrificing a Goblin");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void SiegeGangCommander_ActivatedAbility_DealsTwoDamageToChosenTarget()
    {
        var sgc = SiegeGangCommanderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sgc);
        sgc.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var ability = GetSacAbility(sgc);
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in ability.Effects) e.Execute();

        target.Damage.Should().Be(2, "Siege-Gang Commander deals 2 damage to any target.");
    }

    [Fact]
    public void SiegeGangCommander_ActivatedAbility_NoTargetChosen_IsNoOp()
    {
        var sgc = SiegeGangCommanderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sgc);
        sgc.SetZone(ZoneType.Battlefield);

        var ability = GetSacAbility(sgc);
        // No SetChosenTargets call.

        var act = () =>
        {
            foreach (var e in ability.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }
}
