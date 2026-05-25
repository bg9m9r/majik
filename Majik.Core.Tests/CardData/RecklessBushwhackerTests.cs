using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RecklessBushwhackerFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Goblin + Berserker subtypes, 2/1,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Surge keyword marker present (CR 702.115).
/// - ETB triggered ability exists, fires on Bushwhacker's own ETB.
/// - With the Surge primitive deferred (SurgePaid stub returns false),
///   the intervening-if blocks the pump body — controller's other
///   creatures are NOT pumped/granted Haste at ETB.
/// - ApplyPumpAndHaste helper directly pumps the team +1/+0 + Haste EOT
///   (exercises the body once the Surge primitive lands).
/// </summary>
public class RecklessBushwhackerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void RecklessBushwhacker_Identity()
    {
        var c = RecklessBushwhackerFactory.Create(_alice);

        c.Name.Should().Be("Reckless Bushwhacker");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Berserker).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RecklessBushwhacker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Reckless Bushwhacker", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Reckless Bushwhacker");
        ((Creature)c).HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Berserker).Should().BeTrue();
    }

    // ── Surge keyword marker ──────────────────────────────────────────────

    [Fact]
    public void RecklessBushwhacker_HasSurgeKeywordMarker()
    {
        var c = RecklessBushwhackerFactory.Create(_alice);

        var surge = c.Abilities.OfType<KeywordAbility>()
            .FirstOrDefault(k => k.Keyword == "Surge");

        surge.Should().NotBeNull(
            "CR 702.115 — Surge keyword marker must be present (alt-cost primitive deferred).");
    }

    // ── ETB trigger shape ─────────────────────────────────────────────────

    [Fact]
    public void RecklessBushwhacker_HasEtbTrigger()
    {
        var c = RecklessBushwhackerFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        etb.Should().NotBeNull("ETB trigger is attached to the card shape.");
    }

    [Fact]
    public void RecklessBushwhacker_EtbInterveningIf_BlocksWhenSurgeNotPaid()
    {
        // Default factory path — no Surge primitive shipped, so the
        // intervening-if always returns false; the pump body must not
        // touch other creatures even when the ETB trigger fires.
        var svc = new ContinuousEffectsService();

        var teammate = MakeCreature(_alice, "Grizzly Bears");
        teammate.ActiveEffects = svc;

        var bushwhacker = RecklessBushwhackerFactory.Create(_alice, triggers: null);
        bushwhacker.SetZone(ZoneType.Battlefield);
        bushwhacker.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(bushwhacker);

        var trigger = bushwhacker.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        // Drive the effect body directly: simulates the trigger resolving.
        // The inline guard re-checks SurgePaid → returns false → no-op.
        foreach (var eff in trigger.Effects)
        {
            eff.Execute();
        }

        teammate.GetPower().Should().Be(2,
            "Surge primitive not shipped — surge-paid intervening-if blocks the pump.");
        teammate.GetToughness().Should().Be(2);
        CombatAbilities.HasHaste(teammate).Should().BeFalse(
            "no Haste grant either; the whole body is gated on surge-paid.");
    }

    // ── ApplyPumpAndHaste helper — direct exercise of the pump body ──────

    [Fact]
    public void ApplyPumpAndHaste_Plus1Plus0AndHaste_OnControlledCreatures()
    {
        // Direct exercise of the pump-team body — same shape as Violent
        // Outburst's ApplyPumpAndHaste. Once the Surge primitive lands
        // and stamps WasCastForSurge, the ETB trigger will route here.
        var svc = new ContinuousEffectsService();

        var c1 = MakeCreature(_alice, "Grizzly Bears");
        c1.ActiveEffects = svc;

        var c2 = MakeCreature(_alice, "Llanowar Elves");
        c2.ActiveEffects = svc;

        var prePower1 = c1.GetPower();
        var prePower2 = c2.GetPower();

        RecklessBushwhackerFactory.ApplyPumpAndHaste(_alice);

        c1.GetPower().Should().Be(prePower1 + 1,
            "+1/+0 EOT pump applies to each controlled creature.");
        c2.GetPower().Should().Be(prePower2 + 1);

        CombatAbilities.HasHaste(c1).Should().BeTrue(
            "Haste grant applies to each controlled creature.");
        CombatAbilities.HasHaste(c2).Should().BeTrue();
    }

    [Fact]
    public void ApplyPumpAndHaste_DoesNotPump_OpponentsCreatures()
    {
        var svc = new ContinuousEffectsService();

        var oppBear = MakeCreature(_bob, "Grizzly Bears");
        oppBear.ActiveEffects = svc;

        RecklessBushwhackerFactory.ApplyPumpAndHaste(_alice);

        oppBear.GetPower().Should().Be(2,
            "pump is scoped to caster's battlefield (CR 109.5 — 'creatures you control').");
        CombatAbilities.HasHaste(oppBear).Should().BeFalse();
    }
}
