using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SkateboardFactory"/>.
///
/// Covers:
/// - Identity (Artifact + Equipment subtype, mana cost {1}, MV 1).
/// - NamedCardFactory dispatch.
/// - ETB trigger present + taps the chosen target permanent.
/// - ETB trigger no-op when target has left the battlefield (CR 608.2b).
/// - Static +1/+0 boost via <see cref="AttachedBoostEffect"/> (Layer 7c).
/// - Haste grant on equipped creature via <see cref="GrantAbilityEffect"/>
///   (Layer 6); revoked on detach.
/// - Shape-only path: Haste marker lives on the Skateboard card itself.
/// - Equip {1} activated ability shape.
/// </summary>
public class SkateboardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Skateboard_Identity()
    {
        var sb = SkateboardFactory.Create(_alice);

        sb.Name.Should().Be("Skateboard");
        sb.ManaCost.Should().Be("{1}");
        sb.HasType(CardType.Artifact).Should().BeTrue();
        sb.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        sb.Owner.Should().BeSameAs(_alice);
        sb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Skateboard_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Skateboard", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Skateboard");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip {1}
    // -----------------------------------------------------------------------

    [Fact]
    public void Skateboard_EquipAbility_HasGenericOneCost()
    {
        var sb = SkateboardFactory.Create(_alice);

        var equip = sb.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(1, "printed Equip {1}");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — tap target permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void Skateboard_HasEtbTrigger()
    {
        var sb = SkateboardFactory.Create(_alice);

        sb.Abilities.OfType<TriggeredAbility>().Should().NotBeEmpty(
            "Skateboard has a 'when this Equipment enters' ETB trigger");
    }

    [Fact]
    public void Skateboard_EtbTrigger_TapsChosenPermanent()
    {
        var sb = SkateboardFactory.Create(_alice);
        sb.Zone = ZoneType.Battlefield;

        // Set up a target permanent on the battlefield (untapped).
        var target = new Artifact("Sol Ring", "{1}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        target.IsTapped.Should().BeFalse("target starts untapped");

        var trigger = sb.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new[] { new object[] { target } });

        foreach (var effect in trigger.Effects) effect.Execute();

        target.IsTapped.Should().BeTrue(
            "ETB trigger taps the chosen target permanent (no 'may' rider)");
    }

    [Fact]
    public void Skateboard_EtbTrigger_NoOp_WhenTargetHasLeftBattlefield()
    {
        // CR 608.2b — if the chosen permanent is no longer on the battlefield
        // at resolution, the effect does nothing.
        var sb = SkateboardFactory.Create(_alice);
        sb.Zone = ZoneType.Battlefield;

        var target = new Artifact("Sol Ring", "{1}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Graveyard, // already left the battlefield
        };

        var trigger = sb.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new[] { new object[] { target } });

        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        target.IsTapped.Should().BeFalse("no-op when target is off the battlefield");
    }

    [Fact]
    public void Skateboard_EtbTrigger_NoOp_WhenNoTargetChosen()
    {
        // When the agent supplies no target (empty chosen list), the effect
        // must not throw and must not tap anything.
        var sb = SkateboardFactory.Create(_alice);
        sb.Zone = ZoneType.Battlefield;

        var trigger = sb.Abilities.OfType<TriggeredAbility>().Single();
        // No SetChosenTargets call — ChosenTargets is empty by default.

        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Static +1/+0 boost (Layer 7c) + Haste grant (Layer 6)
    // -----------------------------------------------------------------------

    [Fact]
    public void Skateboard_Equipped_Bear_Gets_Plus1Power_AndHaste()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var sb = SkateboardFactory.Create(_alice, svc, triggers: null);
        sb.Zone = ZoneType.Battlefield;

        sb.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1/+0 boost from Skateboard");
        bear.GetToughness().Should().Be(2, "toughness unchanged");
        CombatAbilities.HasHaste(bear).Should().BeTrue(
            "Skateboard grants Haste to the equipped creature");
    }

    [Fact]
    public void Skateboard_Detach_RestoresPT_AndHasteLapses()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var sb = SkateboardFactory.Create(_alice, svc, triggers: null);
        sb.Zone = ZoneType.Battlefield;
        sb.AttachTo(bear);

        // Sanity check while equipped.
        bear.GetPower().Should().Be(3);
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        sb.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach");
        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "Haste grant is revoked when no longer attached");
    }

    [Fact]
    public void Skateboard_ShapeOnly_HasteMarkerOnCard()
    {
        // Shape-only path (no ContinuousEffectsService): the Haste marker
        // lives on the Skateboard card so factory-shape tests can observe it.
        var sb = SkateboardFactory.Create(_alice);

        sb.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => string.Equals(
                k.Keyword, "Haste", System.StringComparison.OrdinalIgnoreCase),
                "shape-only path stamps the Haste marker on the Skateboard card");
    }
}
