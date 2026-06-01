using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 711.3 — back-face ABILITY swap. On the back face (Graveyard Glutton),
/// the FRONT's "exile up to one + drain-if-any-creature" rider is suppressed
/// and the BACK's "exile up to two + drain-per-creature-card" rider is active.
/// Both faces' triggers are attached and gated by an
/// <see cref="TriggeredAbility.ActiveWhen"/> face predicate so the day/night
/// flip needs no register/unregister churn.
/// </summary>
public class GraveyardGluttonBackFaceAbilityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private List<Player> Players => new() { _alice, _bob };

    private static IReadOnlyList<TriggeredAbility> ActiveTriggers(Creature gt) =>
        gt.Abilities.OfType<TriggeredAbility>().Where(t => t.IsActiveFace()).ToList();

    private static void FireActiveEntersRider(Creature gt)
    {
        // The ETB and attack triggers share one effect body; fire exactly one
        // active trigger's effects so the rider applies once.
        var trigger = ActiveTriggers(gt).First();
        foreach (var fx in trigger.Effects) fx.Execute();
    }

    private void StockGraveyard(Player owner, int creatures)
    {
        for (var i = 0; i < creatures; i++)
        {
            var c = new Creature($"Corpse{i}", "{1}{G}", 2, 2) { Owner = owner };
            owner.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }
    }

    // Both faces' triggers attach so the ability set is stable across the flip.
    [Fact]
    public void Trespasser_AttachesBothFaces_TriggerSets()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);

        gt.Abilities.OfType<TriggeredAbility>().Should().HaveCount(4,
            "front exile-up-to-one (ETB+attack) + back exile-up-to-two (ETB+attack)");
    }

    [Fact]
    public void FrontFace_OnlyFrontTriggersAreActive()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);

        gt.MdfcState!.IsBackFace.Should().BeFalse();
        ActiveTriggers(gt).Should().HaveCount(2, "only the front face's ETB + attack triggers are active");
    }

    [Fact]
    public void BackFace_OnlyBackTriggersAreActive()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);
        gt.MdfcState!.Transform();

        ActiveTriggers(gt).Should().HaveCount(2, "only the back face's ETB + attack triggers are active");
    }

    [Fact]
    public void Front_Exiles_UpToOne_DrainsOnceIfAnyCreature()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: Players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);
        StockGraveyard(_bob, 2);

        FireActiveEntersRider(gt);

        _bob.Zones.Exile.GetCards().Should().HaveCount(1, "front exiles UP TO ONE");
        _bob.LifeTotal.Should().Be(19, "front drains 1 (a creature was exiled)");
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void Back_Exiles_UpToTwo_DrainsPerCreatureCard()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: Players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);
        gt.MdfcState!.Transform(); // → Graveyard Glutton
        StockGraveyard(_bob, 2);

        FireActiveEntersRider(gt);

        _bob.Zones.Exile.GetCards().Should().HaveCount(2, "back exiles UP TO TWO");
        _bob.LifeTotal.Should().Be(18, "back drains 1 PER creature card exiled (2)");
        _alice.LifeTotal.Should().Be(22, "controller gains 1 per creature card exiled (2)");
    }

    [Fact]
    public void Back_LiveWiring_OnlyBackTriggerSurfacesOnEtb()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var gt = GraveyardTrespasserFactory.Create(_alice, triggers, Players);
        gt.MdfcState!.Transform(); // back face

        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);
        // Drive the ETB event so OnEnterBattlefieldSelf fires (card already on
        // battlefield so the ActiveZones gate passes).
        bus.Publish(new CardMovedEvent(gt, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "only the active back face's ETB trigger surfaces (front suppressed via ActiveWhen)");
    }

    [Fact]
    public void Front_LiveWiring_OnlyFrontTriggerSurfacesOnEtb()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var gt = GraveyardTrespasserFactory.Create(_alice, triggers, Players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(gt, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "only the active front face's ETB trigger surfaces (back suppressed via ActiveWhen)");
    }
}
