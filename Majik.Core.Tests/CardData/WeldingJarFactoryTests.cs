using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WeldingJarFactory"/>.
///
/// Card: Welding Jar — Artifact {0} (Mirrodin).
///   "Sacrifice Welding Jar: Regenerate target artifact."
///
/// Covers:
/// - Identity (Artifact, {0}, no Equipment subtype).
/// - NamedCardFactory dispatch.
/// - Sacrifice activated ability: sole cost is sacrifice (no mana pip).
/// - Resolution: target artifact gains a one-shot
///   <see cref="RegenerationShieldEffect"/> on its controller's
///   <see cref="ReplacementBus"/>; Welding Jar lands in the graveyard.
/// - Shield consumes the next <see cref="DestroyIntent"/>: target stays
///   on the battlefield, tapped (CR 701.18).
/// </summary>
public class WeldingJarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WeldingJar_Identity()
    {
        var c = WeldingJarFactory.Create(_alice);

        c.Name.Should().Be("Welding Jar");
        c.ManaCost.Should().Be("{0}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeFalse(
            "Welding Jar is a plain artifact, not Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WeldingJar_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Welding Jar", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Welding Jar");
    }

    [Fact]
    public void WeldingJar_SacAbility_HasSacrificeCost_AndNoManaCost()
    {
        var c = WeldingJarFactory.Create(_alice);
        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            ac => ac.CostType == AdditionalCostType.Sacrifice,
            "the sole cost is Sacrifice Welding Jar");
        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the printed activation has no mana pip");
    }

    [Fact]
    public void WeldingJar_OnResolve_SacrificesSelf_AndRegistersRegenShield_OnTargetArtifactsBus()
    {
        // Wire a replacement bus on Alice so the shield has somewhere to land.
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var jar = WeldingJarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jar);
        jar.SetZone(ZoneType.Battlefield);

        // Build a target artifact (another Welding-Jar-style {0} artifact —
        // anything with CardType.Artifact works; use a fresh Artifact instance).
        var target = new Artifact("Mox Pearl", "{0}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(target);

        var ability = jar.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { target } });
        foreach (var effect in ability.Effects) effect.Execute();

        // Welding Jar sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(jar);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(jar);

        // Push a DestroyIntent through the bus — shield should cancel it
        // and tap the target.
        var intent = new DestroyIntent(target);
        var after = bus.Apply(intent);
        after.Should().BeNull("regen shield cancelled the destroy (CR 701.18)");
        target.IsTapped.Should().BeTrue("regen taps the saved permanent");
    }

    [Fact]
    public void WeldingJar_OnResolve_NoBus_StillSacrificesSelf()
    {
        // No replacement bus on the target's controller — sac half still
        // resolves; shield half no-ops (shape-only posture).
        var jar = WeldingJarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jar);
        jar.SetZone(ZoneType.Battlefield);

        var target = new Artifact("Mox Pearl", "{0}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(target);

        var ability = jar.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { target } });
        foreach (var effect in ability.Effects) effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(jar);
    }

    [Fact]
    public void WeldingJar_OnResolve_NonArtifactTarget_NoOpsTheShieldHalf()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var jar = WeldingJarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jar);
        jar.SetZone(ZoneType.Battlefield);

        // Creature is a permanent but NOT an artifact — shield must not register.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var ability = jar.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new object[] { bear } });
        foreach (var effect in ability.Effects) effect.Execute();

        // Jar still sacrificed even though shield target was illegal.
        _alice.Zones.Graveyard.GetCards().Should().Contain(jar);

        // Bus has no shield — destroy intent passes through unchanged.
        var intent = new DestroyIntent(bear);
        var after = bus.Apply(intent);
        after.Should().BeSameAs(intent, "no shield was registered against the creature");
    }
}
