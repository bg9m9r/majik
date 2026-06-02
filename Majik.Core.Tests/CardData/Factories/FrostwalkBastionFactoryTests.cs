using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FrostwalkBastionFactory"/> (Kaldheim, snow manland).
/// Snow Land. Oracle text (verified Scryfall 2026-05-29):
///   "{T}: Add {C}.
///    {1}{S}: Until end of turn, this land becomes a 2/3 Construct artifact
///    creature. It's still a land. ({S} can be paid with one mana from a snow
///    source.)
///    Whenever this land deals combat damage to a creature, tap that creature
///    and it doesn't untap during its controller's next untap step."
///
/// Covers:
/// - Identity (Snow Land supertype, name, owner/controller).
/// - JSON-backed {T}: Add {C} mana ability.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({1}{S}, instant speed) + Layer 4 / Layer 7b
///   continuous effects:
///     * Adds Creature + Artifact types + Construct subtype on Layer 4.
///     * Printed Land type stays ("It's still a land").
///     * Records 2/3 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// - Combat-damage-to-a-creature trigger: taps the damaged creature and
///   skips its next untap step (CR 502.1 / 611.2b).
/// </summary>
[Trait("Color", "C")]
public class FrostwalkBastionFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => UntapStepRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostwalkBastion_Identity()
    {
        var land = FrostwalkBastionFactory.Create(_alice);

        land.Name.Should().Be("Frostwalk Bastion");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Frostwalk Bastion is a Snow Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Frostwalk Bastion is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FrostwalkBastion_HasManaAndAnimateAbilities()
    {
        var land = FrostwalkBastionFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} mana ability is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{1}{S} animate ability is wired");
    }
    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostwalkBastion_AnimateAbility_HasPrintedManaCost1S()
    {
        var land = FrostwalkBastionFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({1}{S})");
        animate.IsSorcerySpeed.Should().BeFalse("animate is instant-speed per oracle");
    }

    [Fact]
    public void FrostwalkBastion_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = FrostwalkBastionFactory.Create(_alice, effects, eventBus: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature, "Layer 4 adds Creature");
        chars.Types.Should().Contain(CardType.Artifact, "Layer 4 adds Artifact");
        chars.Subtypes.Should().Contain(CardSubtype.Construct, "Construct subtype added");
    }

    [Fact]
    public void FrostwalkBastion_AnimateEffect_AppliesTypesAndSubtype()
    {
        var land = FrostwalkBastionFactory.Create(_alice);
        var effect = new FrostwalkBastionAnimateEffect(land);

        var chars = new PermanentCharacteristics();
        chars.Types.Add(CardType.Land); // printed
        effect.Apply(chars);

        chars.Types.Should().Contain(CardType.Creature, "creature type added");
        chars.Types.Should().Contain(CardType.Artifact, "artifact type added");
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays — \"It's still a land\"");
        chars.Subtypes.Should().Contain(CardSubtype.Construct, "Construct subtype added");
        effect.ExpiresAtEndOfTurn.Should().BeTrue("animation lifts at cleanup (CR 514.2)");
    }

    [Fact]
    public void FrostwalkBastion_BecomesPTEffect_SetsBase2_3()
    {
        var land = FrostwalkBastionFactory.Create(_alice);
        var effect = new FrostwalkBastionBecomesPTEffect(land, 2, 3);

        effect.NewPower.Should().Be(2);
        effect.NewToughness.Should().Be(3);
        effect.Layer.Should().Be(Layer.PT_SetBase);
        effect.ExpiresAtEndOfTurn.Should().BeTrue();

        var chars = new CreatureCharacteristics();
        effect.Apply(chars);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Combat-damage-to-a-creature trigger
    // -----------------------------------------------------------------------

    // NOTE on the manland combat-math gap (shared with Cave of the Frost
    // Dragon): the engine's combat pipeline and CombatDamageDealtEvent are
    // typed on Creature, and an animated Frostwalk Bastion is a Land runtime
    // instance, so the event's SourceCard is set from a Creature. The rider's
    // worker (FrostwalkBastionRider.Apply) is therefore exercised directly
    // below — the same load-bearing tap + skip-untap behaviour the bus handler
    // performs — until ContinuousEffectsService.Compute surfaces animated
    // lands as combat creatures (the deferred gap documented on the factory).

    [Fact]
    public void FrostwalkBastion_CombatDamageRider_TapsAndSkipsUntap()
    {
        var bus = new EventBus();

        // Bob's untapped creature that the Bastion damages in combat.
        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);
        victim.IsTapped.Should().BeFalse("victim starts untapped");

        // The Bastion (animated) deals combat damage to the victim creature.
        FrostwalkBastionFactory.ApplyCombatRider(victim, bus);

        victim.IsTapped.Should().BeTrue(
            "combat damage to a creature taps that creature (CR 701.21a)");
        UntapStepRestrictions.ShouldSkipUntap(victim, _bob).Should().BeTrue(
            "the damaged creature skips its controller's next untap step (CR 502.1)");
    }

    [Fact]
    public void FrostwalkBastion_CombatDamageRider_AlreadyTappedVictim_LeavesTapped()
    {
        var bus = new EventBus();

        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);
        victim.Tap(); // already tapped (e.g. it attacked)

        // Must not double-tap (Permanent.Tap throws on a tapped permanent).
        var act = () => FrostwalkBastionFactory.ApplyCombatRider(victim, bus);
        act.Should().NotThrow();
        victim.IsTapped.Should().BeTrue("stays tapped");
        UntapStepRestrictions.ShouldSkipUntap(victim, _bob).Should().BeTrue(
            "skip-untap still registered for an already-tapped victim");
    }

    [Fact]
    public void FrostwalkBastion_CombatDamage_FromOtherSource_DoesNotTrigger()
    {
        var bus = new EventBus();
        var land = FrostwalkBastionFactory.Create(_alice, effects: null, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var attacker = new Creature("Some Attacker", "{1}{R}", 3, 3);
        attacker.SetOwner(_alice);
        attacker.SetController(_alice);

        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        // Combat damage dealt by a DIFFERENT creature — the Bastion's bus
        // handler is keyed on "this land" as SourceCard, so it must not fire.
        bus.Publish(new CombatDamageDealtEvent(source: attacker, target: victim, amount: 3));

        victim.IsTapped.Should().BeFalse("the Bastion was not the damage source");
        UntapStepRestrictions.ShouldSkipUntap(victim, _bob).Should().BeFalse(
            "no untap-skip registered for damage from another source");
    }
}
