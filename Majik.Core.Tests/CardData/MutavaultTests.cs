using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="MutavaultFactory"/> — Land manland.
///
/// Covers:
/// - Card identity (Land, name, no printed supertypes/subtypes).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <c>{T}: Add {C}</c> mana ability — tap produces colorless.
/// - <c>{1}</c> activated ability shape: exactly one
///   <see cref="ActivatedAbility"/> alongside the mana ability.
/// - Activate registers a Layer 4 + Layer 7b pair flagged
///   <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>.
/// - Compute(Mutavault) reflects Creature type + every-creature-subtype
///   grant while the effects are live.
/// - <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> lifts both
///   effects; Compute drops back to printed Land identity.
/// </summary>
public class MutavaultTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Mutavault_IsLand_NoSubtypes_NoSupertypes()
    {
        var land = MutavaultFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed Mutavault is just a Land — it only becomes a Creature mid-turn");
        land.Subtypes.Should().BeEmpty("Mutavault has no printed subtypes");
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Mutavault");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Mutavault()
    {
        var card = NamedCardFactory.Create("Mutavault", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Mutavault");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void Mutavault_HasColorlessManaAbility_TappingProducesColorless()
    {
        var land = MutavaultFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} is bucketed as Generic +1 in ManaCost.Parse — there's no
        // dedicated colourless slot in the current ValueObjects.ManaPool
        // (mirrors PhyrexianTowerTests).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {1}: animate ability — shape + resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Mutavault_HasSingleActivatedAnimateAbility_AlongsideManaAbility()
    {
        var land = MutavaultFactory.Create(_alice);

        // ManaAbility implements IActivatedAbility but we filter to the
        // bare ActivatedAbility runtime type to isolate the {1} animator.
        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Effects.Should().HaveCount(1);
        activated.TargetRequests.Should().BeEmpty(
            "the animate ability takes no targets");
    }

    [Fact]
    public void Activate_RegistersLayer4AndLayer7b_EotExpiring_OnTheSourceLand()
    {
        var effects = new ContinuousEffectsService();
        var land = MutavaultFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Resolve();

        var registered = GetRegisteredEffects(effects).ToList();

        var animate = registered.OfType<MutavaultAnimateEffect>().SingleOrDefault();
        animate.Should().NotBeNull("Layer 4 animate effect must be registered");
        animate!.Target.Should().BeSameAs(land);
        animate.Layer.Should().Be(Layer.Type);
        animate.ExpiresAtEndOfTurn.Should().BeTrue();
        animate.IsActive().Should().BeTrue();

        var pt = registered.OfType<MutavaultBecomesPTEffect>().SingleOrDefault();
        pt.Should().NotBeNull("Layer 7b set-base P/T effect must be registered");
        pt!.NewPower.Should().Be(2);
        pt.NewToughness.Should().Be(2);
        pt.Layer.Should().Be(Layer.PT_SetBase);
        pt.ExpiresAtEndOfTurn.Should().BeTrue();
        pt.AppliesTo(land).Should().BeTrue();
    }

    [Fact]
    public void Compute_AfterActivate_AddsCreatureType_KeepsLand_AndGrantsEveryModelledCreatureSubtype()
    {
        var effects = new ContinuousEffectsService();
        var land = MutavaultFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        activated.Resolve();

        var chars = effects.Compute(land);

        chars.Types.Should().Contain(CardType.Creature, "Layer 4 adds Creature type");
        chars.Types.Should().Contain(CardType.Land,
            "Layer 4 ADDS — 'It's still a land' (CR 613.1c)");

        // Spot-check a representative slice of "every creature type" — the
        // animate effect grants every CardSubtype currently enumerated as a
        // creature subtype (see MutavaultAnimateEffect.EveryCreatureType).
        chars.Subtypes.Should().Contain(CardSubtype.Goblin);
        chars.Subtypes.Should().Contain(CardSubtype.Elf);
        chars.Subtypes.Should().Contain(CardSubtype.Human);
        chars.Subtypes.Should().Contain(CardSubtype.Wizard);
        chars.Subtypes.Should().Contain(CardSubtype.Eldrazi);

        // Sanity — the entire EveryCreatureType set is granted.
        foreach (var st in MutavaultAnimateEffect.EveryCreatureType)
        {
            chars.Subtypes.Should().Contain(st);
        }
    }

    [Fact]
    public void EndOfTurn_ExpiresBothEffects_AndLandRevertsToPrintedIdentity()
    {
        var effects = new ContinuousEffectsService();
        var land = MutavaultFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        activated.Resolve();

        // Sanity — effects are live mid-turn.
        GetRegisteredEffects(effects).OfType<MutavaultAnimateEffect>().Should().HaveCount(1);
        GetRegisteredEffects(effects).OfType<MutavaultBecomesPTEffect>().Should().HaveCount(1);
        effects.Compute(land).Types.Should().Contain(CardType.Creature);

        // CR 514.2 — cleanup step lifts "until end of turn" effects.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<MutavaultAnimateEffect>().Should().BeEmpty(
            "Layer 4 animate effect is end-of-turn-expirable");
        GetRegisteredEffects(effects).OfType<MutavaultBecomesPTEffect>().Should().BeEmpty(
            "Layer 7b set-base P/T effect is end-of-turn-expirable");

        var afterChars = effects.Compute(land);
        afterChars.Types.Should().NotContain(CardType.Creature,
            "creature-ness lifts at end of turn");
        afterChars.Types.Should().Contain(CardType.Land);
        afterChars.Subtypes.Should().NotContain(CardSubtype.Goblin);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        // Mirror of the reflection helper used in KarnTheGreatCreatorTests —
        // the effects list is private; reading it via reflection keeps the
        // assertion close to the public surface without exposing internals.
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
