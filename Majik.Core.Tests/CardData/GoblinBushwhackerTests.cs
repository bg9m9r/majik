using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Goblin Bushwhacker (Worldwake, {R}, Creature — Goblin Warrior
/// 1/1).
///
/// Covers:
/// - Identity (Goblin + Warrior, {R}, 1/1).
/// - NamedCardFactory dispatch.
/// - Kicker rider exposed via <see cref="GoblinBushwhackerFactory.BuildAdditionalCost"/>.
/// - <see cref="KickerAltCostProbe"/> recognises Bushwhacker as a {R}-kicker
///   card (matches Burst Lightning's discovery posture).
/// - ETB trigger shape: intervening-if gated on <see cref="Card.WasKicked"/>.
/// - Kicked ETB resolution: creatures Alice controls get +1/+0 + haste EOT.
/// - Not-kicked ETB: intervening-if false → no pump/haste applied.
/// - Opposing creatures are NOT affected.
/// </summary>
public class GoblinBushwhackerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Bushwhacker_Identity()
    {
        var bw = GoblinBushwhackerFactory.Create(_alice);

        bw.Name.Should().Be("Goblin Bushwhacker");
        bw.ManaCost.Should().Be("{R}");
        bw.HasType(CardType.Creature).Should().BeTrue();
        bw.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        bw.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        bw.BasePower.Should().Be(GoblinBushwhackerFactory.Power);
        bw.BaseToughness.Should().Be(GoblinBushwhackerFactory.Toughness);
        bw.Owner.Should().BeSameAs(_alice);
        bw.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Bushwhacker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Bushwhacker", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Bushwhacker");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Kicker additional-cost surface
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAdditionalCost_ReturnsKickerCostAtR()
    {
        var bw = GoblinBushwhackerFactory.Create(_alice);
        var cost = GoblinBushwhackerFactory.BuildAdditionalCost(bw);

        cost.Should().BeOfType<KickerAdditionalCost>();
        ((KickerAdditionalCost)cost).KickerCost.Should().Be(ManaCost.Parse("{R}"));
    }

    [Fact]
    public void KickerAltCostProbe_Recognises_Bushwhacker()
    {
        // Bushwhacker must be in the bot's default kicker-lookup so the
        // decision layer surfaces the kicker without per-card wiring.
        var bw = GoblinBushwhackerFactory.Create(_alice);
        bw.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bw);

        var probe = new KickerAltCostProbe();
        var kickerCost = probe.KickerCostFor(bw, _alice);

        kickerCost.Should().Be(ManaCost.Parse("{R}"));
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Bushwhacker_HasEtbInterveningIfTrigger()
    {
        var bw = GoblinBushwhackerFactory.Create(_alice);

        var etb = bw.Abilities.OfType<TriggeredAbility>().Single();
        etb.InterveningIf.Should().NotBeNull(
            "Bushwhacker's ETB is intervening-if-kicked — CR 603.4");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — kicked branch pumps + haste
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_Kicked_PumpsAndGrantsHasteToControllerCreatures()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        var ces = new ContinuousEffectsService();

        // Alice has a Grizzly Bears already out.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = ces,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        // Bob has an opposing creature — should NOT pick up the rider.
        var bobBear = new Creature("Bob's Bear", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = ces,
        };
        _bob.Zones.Battlefield.AddCard(bobBear);

        var bw = GoblinBushwhackerFactory.Create(_alice);
        bw.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bw);
        bw.ActiveEffects = ces;
        bw.SetWasKicked(true);  // simulate kicker-paid cast (CR 702.33).
        triggers.BindCard(bw);

        zones.MoveCardTo(bw, ZoneType.Battlefield);

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "intervening-if = true (kicked) → ETB queues");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Alice's bear: +1/+0, +Haste.
        bear.GetPower().Should().Be(3, "Grizzly Bears gets +1/+0");
        bear.GetToughness().Should().Be(2, "+0 toughness — Bushwhacker is +1/+0");
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        // Bushwhacker pumps himself too (CR 608.2 — he's on the battlefield
        // by ETB resolution; "creatures you control" includes him).
        bw.GetPower().Should().Be(2, "solo Bushwhacker becomes a 2/1");
        CombatAbilities.HasHaste(bw).Should().BeTrue();

        // Bob's bear: unaffected.
        bobBear.GetPower().Should().Be(2);
        CombatAbilities.HasHaste(bobBear).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ETB resolution — not-kicked branch does nothing
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_NotKicked_NoPumpNoHaste()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        var ces = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = ces,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var bw = GoblinBushwhackerFactory.Create(_alice);
        bw.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bw);
        bw.ActiveEffects = ces;
        // bw.WasKicked stays false — no kicker paid.
        triggers.BindCard(bw);

        zones.MoveCardTo(bw, ZoneType.Battlefield);

        // CR 603.4 — intervening-if false at announce → trigger never goes
        // on the stack. PendingCount may include other bus triggers (none
        // here), but the bear stays unpumped.
        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            while (stack.Top != null) stack.Pop()!.Resolve();
        }

        bear.GetPower().Should().Be(2, "not-kicked Bushwhacker doesn't pump");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeFalse();
    }
}
