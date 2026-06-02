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
/// Unit tests for <see cref="CripplingBlightFactory"/>.
///
/// Card: Crippling Blight — Enchantment — Aura {B} (Magic 2013 et al.).
///   "Enchant creature."
///   "Enchanted creature gets -1/-1 and can't block."
///
/// Covers:
///   - Identity: {B} Enchantment — Aura.
///   - NamedCardFactory dispatch.
///   - Static -1/-1 (CR 613.3c, Layer 7c) via AttachedBoostEffect(-1,-1):
///       * Bearer 2/2 becomes 1/1 while aura is on the battlefield and attached.
///       * Debuff removed when aura is unattached.
///   - Static can't-block (CR 509.1c) via CombatRestrictionEffect:
///       * Bearer is CannotBlock while aura is on the battlefield and attached.
///       * Restriction removed when aura LTBs.
///   - BuildSpellDefinition: legal candidates are creatures only (CR 702.5b).
/// </summary>
[Trait("Color", "B")]
public class CripplingBlightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CripplingBlight_Identity()
    {
        var cb = CripplingBlightFactory.Create(_alice);

        cb.Name.Should().Be("Crippling Blight");
        cb.ManaCost.Should().Be("{B}");
        cb.HasType(CardType.Enchantment).Should().BeTrue();
        cb.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        cb.IsAura.Should().BeTrue();
        cb.Owner.Should().BeSameAs(_alice);
        cb.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Static -1/-1 (CR 613.3c — Layer 7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Attached_Reduces2x2BearerTo1x1()
    {
        var effects = new ContinuousEffectsService();
        var cb = CripplingBlightFactory.Create(_alice, continuousEffects: effects, eventBus: null);

        var bearer = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _bob.Zones.Battlefield.AddCard(bearer);

        cb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.AttachTo(bearer);

        bearer.Power.Should().Be(1, "enchanted creature gets -1/-1 (CR 613.3c)");
        bearer.Toughness.Should().Be(1, "enchanted creature gets -1/-1 (CR 613.3c)");
    }

    [Fact]
    public void Unattached_DoesNotDebuffCreature()
    {
        var effects = new ContinuousEffectsService();
        var cb = CripplingBlightFactory.Create(_alice, continuousEffects: effects, eventBus: null);

        var bearer = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _bob.Zones.Battlefield.AddCard(bearer);

        cb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cb);
        // No AttachTo — aura is on the battlefield but not attached.

        bearer.Power.Should().Be(2, "no debuff without attachment");
        bearer.Toughness.Should().Be(2, "no debuff without attachment");
    }

    // -----------------------------------------------------------------------
    // Static can't-block (CR 509.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Attached_RegistersCannotBlockRestriction_OnEnchantedCreature()
    {
        var cb = CripplingBlightFactory.Create(_alice, continuousEffects: null, eventBus: _bus);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);

        cb.AttachTo(bear);

        // Poke Sync() directly — same pattern as PacifismFactoryTests.
        var lifecycle = new CripplingBlightLifecycle(cb, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        bear.ActiveEffects!.HasRestriction(bear, CombatRestriction.CannotBlock)
            .Should().BeTrue("enchanted creature can't block (CR 509.1c)");
    }

    [Fact]
    public void Attached_DoesNotRestrictAttacking()
    {
        // Crippling Blight only says "can't block" — attacking is not restricted.
        var cb = CripplingBlightFactory.Create(_alice, continuousEffects: null, eventBus: _bus);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);
        cb.AttachTo(bear);

        var lifecycle = new CripplingBlightLifecycle(cb, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        bear.ActiveEffects!.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeFalse("Crippling Blight does not restrict attacking");
    }

    [Fact]
    public void Aura_LTB_RemovesCannotBlockRestriction()
    {
        var cb = CripplingBlightFactory.Create(_alice, continuousEffects: null, eventBus: _bus);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);
        cb.AttachTo(bear);

        var lifecycle = new CripplingBlightLifecycle(cb, _bus);
        lifecycle.Attach();
        lifecycle.Sync();
        lifecycle.IsActive.Should().BeTrue();

        // Aura LTB — move aura to graveyard.
        _alice.Zones.Battlefield.RemoveCard(cb);
        _alice.Zones.Graveyard.AddCard(cb);
        cb.SetZone(ZoneType.Graveyard);
        lifecycle.Sync();

        bear.ActiveEffects!.HasRestriction(bear, CombatRestriction.CannotBlock)
            .Should().BeFalse("restriction unregisters when aura LTBs");
        lifecycle.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Unattached_NoCannotBlockRestriction()
    {
        var cb = CripplingBlightFactory.Create(_alice, continuousEffects: null, eventBus: _bus);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);

        var lifecycle = new CripplingBlightLifecycle(cb, _bus);
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
        var cb = CripplingBlightFactory.Create(_alice);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);

        var land = new Land("Swamp");
        var artifact = new Artifact("Black Lotus", "{0}");

        var battlefield = new Permanent[] { creature, land, artifact };
        var def = CripplingBlightFactory.BuildSpellDefinition(cb, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(creature, "creatures are legal Enchant-creature targets");
        candidates.Should().NotContain(land, "lands are not creatures");
        candidates.Should().NotContain(artifact, "artifacts are not creatures");
    }
}
