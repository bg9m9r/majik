using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AdventuringGearFactory"/>.
///
/// Adventuring Gear (Zendikar, {1}) — Artifact — Equipment. Oracle text
/// (Scryfall, verified 2026-06-23):
///   "Landfall — Whenever a land you control enters, equipped creature gets
///    +2/+2 until end of turn.
///    Equip {1} ({1}: Attach to target creature you control. Equip only as a
///    sorcery.)"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, type, Equipment subtype, mana cost) — single assert.
/// - Equip {1} activated-ability cost shape.
/// - Landfall pumps the EQUIPPED creature +2/+2 (CR 702.142 / 613.1g), not
///   the Gear itself.
/// - The pump expires at end of turn (CR 514.2).
/// - Unattached: a landfall trigger resolve is a no-op (no equipped creature).
/// </summary>
[Trait("Color", "C")]
public class AdventuringGearTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AdventuringGear_Identity()
    {
        var c = AdventuringGearFactory.Create(_alice);

        c.Name.Should().Be("Adventuring Gear");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Adventuring Gear is an Equipment");
        c.ManaCost.Should().Be("{1}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AdventuringGear_EquipAbility_HasGenericOneCost()
    {
        var c = AdventuringGearFactory.Create(_alice);

        var ability = c.Abilities.OfType<EquipActivatedAbility>().Single();

        ability.EquipCost.Generic.Should().Be(1, "Equip {1} is the printed activation cost");
    }

    [Fact]
    public void AdventuringGear_Landfall_PumpsEquippedCreature_Plus2Plus2()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var gear = AdventuringGearFactory.Create(_alice);
        gear.Zone = ZoneType.Battlefield;
        gear.AttachTo(bear);

        // Resolve the landfall ability's effect directly (CR 603.6a — no
        // target; the pump names "equipped creature"). The +2/+2 lands on the
        // bearer, not the Gear.
        var landfall = gear.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in landfall.Effects)
        {
            effect.Execute();
        }

        bear.GetPower().Should().Be(4, "landfall grants the equipped creature +2/+2");
        bear.GetToughness().Should().Be(4, "landfall grants the equipped creature +2/+2");
    }

    [Fact]
    public void AdventuringGear_LandfallPump_ExpiresAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var gear = AdventuringGearFactory.Create(_alice);
        gear.Zone = ZoneType.Battlefield;
        gear.AttachTo(bear);

        var landfall = gear.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in landfall.Effects)
        {
            effect.Execute();
        }

        bear.GetPower().Should().Be(4);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2, "the +2/+2 pump expires at end of turn");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void AdventuringGear_Landfall_Unattached_IsNoOp()
    {
        var gear = AdventuringGearFactory.Create(_alice);
        gear.Zone = ZoneType.Battlefield;
        // intentionally not equipped — no equipped creature to pump.

        var landfall = gear.Abilities.OfType<TriggeredAbility>().Single();

        // Resolving with no equipped creature must not throw (CR 608.2b).
        var act = () =>
        {
            foreach (var effect in landfall.Effects)
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow();
    }
}
