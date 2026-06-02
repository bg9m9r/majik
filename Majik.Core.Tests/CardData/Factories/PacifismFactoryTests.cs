using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PacifismFactory"/>.
///
/// Card: Pacifism — Enchantment — Aura {1}{W} (Tempest et al.).
///   "Enchant creature."
///   "Enchanted creature can't attack or block."
///
/// Covers:
///   - Identity: {1}{W} Enchantment — Aura.
///   - NamedCardFactory dispatch.
///   - Static lockout via <see cref="PacifismLifecycle"/>:
///       * Bearer is CannotAttack + CannotBlock while aura is on the
///         battlefield and attached (CR 508.1c / 509.1c).
///       * Aura LTB removes both restrictions.
///       * Unattached aura registers nothing.
///   - BuildSpellDefinition: legal candidates are creatures only
///     (CR 702.5b — "Enchant creature").
/// </summary>
[Trait("Color", "W")]
public class PacifismFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Pacifism_Identity()
    {
        var p = PacifismFactory.Create(_alice);

        p.Name.Should().Be("Pacifism");
        p.ManaCost.Should().Be("{1}{W}");
        p.HasType(CardType.Enchantment).Should().BeTrue();
        p.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        p.IsAura.Should().BeTrue();
        p.Owner.Should().BeSameAs(_alice);
        p.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Static lockout (CR 508.1c / 509.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Attached_RegistersBothRestrictions_OnEnchantedCreature()
    {
        var pacifism = PacifismFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);

        pacifism.AttachTo(bear);

        // Re-sync via a fresh lifecycle (aura was already on the
        // battlefield before AttachTo, so we poke Sync() directly —
        // same pattern as LeylineBindingTests / BoundInGoldFactoryTests).
        var lifecycle = new PacifismLifecycle(pacifism, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        bear.ActiveEffects!.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeTrue("enchanted creature can't attack (CR 508.1c)");
        bear.ActiveEffects.HasRestriction(bear, CombatRestriction.CannotBlock)
            .Should().BeTrue("enchanted creature can't block (CR 509.1c)");
    }

    [Fact]
    public void Attached_DoesNotRestrictActivations()
    {
        // Pacifism only says "can't attack or block" — no activation
        // restriction (contrast Leyline Binding / Bound in Gold).
        var pacifism = PacifismFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);
        pacifism.AttachTo(bear);

        var lifecycle = new PacifismLifecycle(pacifism, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        bear.ActiveEffects!.HasActivationRestriction(bear)
            .Should().BeFalse("Pacifism does not restrict ability activations");
    }

    [Fact]
    public void Aura_LTB_RemovesBothRestrictions()
    {
        var pacifism = PacifismFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);
        pacifism.AttachTo(bear);

        var lifecycle = new PacifismLifecycle(pacifism, _bus);
        lifecycle.Attach();
        lifecycle.Sync();
        lifecycle.IsActive.Should().BeTrue();

        // Aura LTB — move aura to graveyard.
        _alice.Zones.Battlefield.RemoveCard(pacifism);
        _alice.Zones.Graveyard.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Graveyard);
        lifecycle.Sync();

        bear.ActiveEffects!.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeFalse("restrictions unregister when aura LTBs");
        bear.ActiveEffects.HasRestriction(bear, CombatRestriction.CannotBlock)
            .Should().BeFalse("restrictions unregister when aura LTBs");
        lifecycle.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Unattached_NoRestrictions()
    {
        var pacifism = PacifismFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Battlefield);

        var lifecycle = new PacifismLifecycle(pacifism, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        lifecycle.IsActive.Should().BeFalse(
            "without an AttachedTo target, no restrictions are registered");
    }

    // -----------------------------------------------------------------------
    // BuildSpellDefinition — candidate filter
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OnlyCreaturesAreLegalTargets()
    {
        var pacifism = PacifismFactory.Create(_alice);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);

        var land = new Land("Plains");
        var artifact = new Artifact("Mox Pearl", "{0}");

        var battlefield = new Permanent[] { creature, land, artifact };
        var def = PacifismFactory.BuildSpellDefinition(pacifism, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(creature, "creatures are legal Enchant-creature targets");
        candidates.Should().NotContain(land, "lands are not creatures");
        candidates.Should().NotContain(artifact, "artifacts are not creatures");
    }
}
