using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ZoralineCosmosCallerFactory"/>.
///
/// Card: Zoraline, Cosmos Caller (Edge of Eternities, {1}{W}{B}). Legendary
/// Creature — Bat Cleric. 3/3. Flying, vigilance.
///
/// Coverage (unique behaviour only — identity/dispatch/well-formedness are
/// covered by CardFactoryContractTests):
/// <list type="bullet">
///   <item>Identity ({1}{W}{B}, 3/3, Legendary Bat Cleric, Flying +
///       Vigilance from JSON).</item>
///   <item>Ability 2 — "Whenever a Bat you control attacks, you gain 1
///       life": fires on a Bat the owner controls attacking; does NOT fire
///       on a non-Bat attacker.</item>
///   <item>Ability 3 — "may pay {W}{B} and 2 life. When you do, return
///       target nonland permanent card MV ≤ 3 from your graveyard with a
///       finality counter": pays + returns + stamps finality on yes;
///       declines (no payment, card stays in graveyard) on no; skips a
///       too-expensive (MV 4) graveyard card.</item>
/// </list>
/// </summary>
[Trait("Color", "M")]
public class ZoralineCosmosCallerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>Yes-to-everything agent for the reflexive optional cost.</summary>
    private sealed class YesAgent : DelegatingAgent
    {
        public override Task<IReadOnlyList<object>> ChooseAsync(
            GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(new object[] { true });
    }

    private Creature MakeZoraline(
        out TriggerManager triggers, out EventBus bus,
        out ZoneService zones, out ReplacementBus reps,
        out Majik.Core.Stack.Stack stack)
    {
        bus = new EventBus();
        stack = new Majik.Core.Stack.Stack(bus);
        reps = new ReplacementBus();
        zones = new ZoneService(bus, reps);
        triggers = new TriggerManager(stack, bus);
        var zoraline = ZoralineCosmosCallerFactory.Create(_alice, triggers, zones, reps, bus);
        zoraline.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(zoraline);
        triggers.BindCard(zoraline);
        return zoraline;
    }

    [Fact]
    public void Zoraline_Identity()
    {
        var z = ZoralineCosmosCallerFactory.Create(_alice);

        z.Name.Should().Be("Zoraline, Cosmos Caller");
        z.ManaCost.Should().Be("{1}{W}{B}");
        z.BasePower.Should().Be(3);
        z.BaseToughness.Should().Be(3);
        z.HasType(CardType.Creature).Should().BeTrue();
        z.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        z.Subtypes.Should().Contain(CardSubtype.Bat);
        z.Subtypes.Should().Contain(CardSubtype.Cleric);

        var keywords = z.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Vigilance");
    }

    [Fact]
    public void Ability2_BatYouControlAttacks_GainsOneLife()
    {
        var zoraline = MakeZoraline(out var triggers, out var bus, out _, out _, out var stack);

        var bat = new Creature("Vampire Bats", "{B}", 1, 1,
            subtypes: new[] { CardSubtype.Bat })
        { Owner = _alice, Controller = _alice };
        bat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bat);
        triggers.BindCard(bat);

        var before = _alice.LifeTotal;
        bus.Publish(new CreatureAttacksEvent(bat, _bob));

        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(before + 1,
            because: "a Bat you control attacked → you gain 1 life");
    }

    [Fact]
    public void Ability2_NonBatAttacker_DoesNotGainLife()
    {
        var zoraline = MakeZoraline(out var triggers, out var bus, out _, out _, out var stack);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        triggers.BindCard(bear);

        var before = _alice.LifeTotal;
        bus.Publish(new CreatureAttacksEvent(bear, _bob));

        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(before,
            because: "a non-Bat attacker does not trigger the lifegain");
    }

    [Fact]
    public async Task Ability3_PayCost_ReturnsMv3PermanentWithFinalityCounter()
    {
        var zoraline = MakeZoraline(out _, out var bus, out var zones, out var reps, out _);

        // Seed Alice's graveyard with a MV-3 creature card (eligible).
        var ghoul = new Creature("Test Ghoul", "{1}{B}{B}", 2, 3)
        { Owner = _alice, Controller = _alice };
        ghoul.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(ghoul);

        // Fund the optional cost: {W}{B} + ample life.
        _alice.AddManaToPool(ManaCost.Parse("{W}{B}"));
        var lifeBefore = _alice.LifeTotal;

        // Drive the reflexive (ETB/attack) reanimation effect directly with a
        // yes-agent context — both reanimation triggers carry the same effect.
        var reanimateAbility = zoraline.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        var ctx = new ResolutionContext(
            Controller: _alice, Agent: new YesAgent(), Game: null,
            ChosenTargets: System.Array.Empty<IReadOnlyList<object>>());

        foreach (var eff in reanimateAbility.Effects)
            await eff.ExecuteAsync(ctx);

        ghoul.Zone.Should().Be(ZoneType.Battlefield);
        ghoul.Controller.Should().BeSameAs(_alice);
        ghoul.Counters.Count(CounterType.Finality).Should().Be(1,
            because: "the reflexive return says 'with a finality counter on it'");
        _alice.LifeTotal.Should().Be(lifeBefore - 2,
            because: "paying the optional cost loses 2 life");
        _alice.ManaPool.CanPay(ManaCost.Parse("{W}{B}")).Should().BeFalse(
            because: "the {W}{B} was spent on the optional cost");
    }

    [Fact]
    public async Task Ability3_DeclinePayment_LeavesCardInGraveyard()
    {
        var zoraline = MakeZoraline(out _, out _, out _, out _, out _);

        var ghoul = new Creature("Test Ghoul", "{1}{B}{B}", 2, 3)
        { Owner = _alice, Controller = _alice };
        ghoul.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(ghoul);
        _alice.AddManaToPool(ManaCost.Parse("{W}{B}"));

        var reanimateAbility = zoraline.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        // Null agent ⇒ no decision surface ⇒ treated as "did not pay".
        var ctx = new ResolutionContext(
            Controller: _alice, Agent: null, Game: null,
            ChosenTargets: System.Array.Empty<IReadOnlyList<object>>());

        foreach (var eff in reanimateAbility.Effects)
            await eff.ExecuteAsync(ctx);

        ghoul.Zone.Should().Be(ZoneType.Graveyard,
            because: "declining the optional cost skips the entire reflexive block");
        _alice.LifeTotal.Should().Be(20, because: "no payment, no life loss");
    }

    [Fact]
    public async Task Ability3_TooExpensiveGraveyardCard_IsNotReturned()
    {
        var zoraline = MakeZoraline(out _, out _, out var zones, out var reps, out _);

        // MV 4 — exceeds the "mana value 3 or less" cap.
        var titan = new Creature("Test Titan", "{2}{R}{R}", 6, 6)
        { Owner = _alice, Controller = _alice };
        titan.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(titan);
        _alice.AddManaToPool(ManaCost.Parse("{W}{B}"));

        var reanimateAbility = zoraline.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        var ctx = new ResolutionContext(
            Controller: _alice, Agent: new YesAgent(), Game: null,
            ChosenTargets: System.Array.Empty<IReadOnlyList<object>>());

        foreach (var eff in reanimateAbility.Effects)
            await eff.ExecuteAsync(ctx);

        titan.Zone.Should().Be(ZoneType.Graveyard,
            because: "an MV-4 card exceeds the 'mana value 3 or less' cap");
    }
}
