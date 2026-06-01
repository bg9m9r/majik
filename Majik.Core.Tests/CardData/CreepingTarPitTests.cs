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
/// Unit tests for <see cref="CreepingTarPitFactory"/> (Worldwake).
///
/// Covers:
///   - Card identity (Land, no subtypes, no supertype, name, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch produces the same shape.
///   - {T}: Add {U} and {T}: Add {B} — two <see cref="ManaAbility"/> instances;
///     activating each produces the correct mana colour.
///   - {1}{U}{B} activated ability: resolution registers Layer 4
///     (<see cref="CreepingTarPitAnimateEffect"/>), Layer 7b
///     (<see cref="CreepingTarPitBecomesPTEffect"/>), and Layer 6
///     (<see cref="CreepingTarPitShroudEffect"/>) effects on the
///     <see cref="ContinuousEffectsService"/>, all flagged
///     <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>.
///   - Compute after activation: Creature type + Elemental subtype granted,
///     Land type retained, P/T = 3/2, Shroud in keyword set.
///   - End-of-turn expiry lifts all three effects; Compute drops back to
///     plain Land identity.
/// </summary>
public class CreepingTarPitTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CreepingTarPit_IsLand_NoSubtypes_NoSupertypes()
    {
        var land = CreepingTarPitFactory.Create(_alice);

        land.Name.Should().Be("Creeping Tar Pit");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed Creeping Tar Pit is just a Land until activated");
        land.Subtypes.Should().BeEmpty("Creeping Tar Pit has no printed subtypes");
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CreepingTarPit_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Creeping Tar Pit", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Creeping Tar Pit");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {U} and {T}: Add {B}");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "{1}{U}{B}: animate ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U}  /  {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void CreepingTarPit_TapProducesBlue()
    {
        var land = CreepingTarPitFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        var blueAbility = manaAbilities.FirstOrDefault(a =>
        {
            var m = a.Activate();
            land.Untap(); // reset after probe activation
            return m.Blue > 0;
        });

        blueAbility.Should().NotBeNull("{T}: Add {U} must be present");
    }

    [Fact]
    public void CreepingTarPit_TapProducesBlack()
    {
        var land = CreepingTarPitFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        var blackAbility = manaAbilities.FirstOrDefault(a =>
        {
            var m = a.Activate();
            land.Untap(); // reset after probe activation
            return m.Black > 0;
        });

        blackAbility.Should().NotBeNull("{T}: Add {B} must be present");
    }

    // -----------------------------------------------------------------------
    // {1}{U}{B}: animate ability — shape + resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void CreepingTarPit_HasSingleActivatedAnimateAbility_AlongsideTwoManaAbilities()
    {
        var land = CreepingTarPitFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {U} and {T}: Add {B}");

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();

        activated.Should().HaveCount(1, "exactly one {1}{U}{B} animate ability");
        activated[0].Effects.Should().HaveCount(1);
        activated[0].TargetRequests.Should().BeEmpty("no targets");
    }

    [Fact]
    public void Activate_RegistersLayer4_Layer7b_Layer6_EotExpiring()
    {
        var effects = new ContinuousEffectsService();
        var land = CreepingTarPitFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Resolve();

        var registered = GetRegisteredEffects(effects).ToList();

        // Layer 4 — Creature type + Elemental subtype
        var animateEffect = registered.OfType<CreepingTarPitAnimateEffect>().SingleOrDefault();
        animateEffect.Should().NotBeNull("Layer 4 animate effect must be registered");
        animateEffect!.Target.Should().BeSameAs(land);
        animateEffect.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        animateEffect.IsActive().Should().BeTrue();

        // Layer 7b — P/T 3/2
        var ptEffect = registered.OfType<CreepingTarPitBecomesPTEffect>().SingleOrDefault();
        ptEffect.Should().NotBeNull("Layer 7b set-base P/T effect must be registered");
        ptEffect!.NewPower.Should().Be(3);
        ptEffect.NewToughness.Should().Be(2);
        ptEffect.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();

        // Layer 6 — Shroud
        var shroudEffect = registered.OfType<CreepingTarPitShroudEffect>().SingleOrDefault();
        shroudEffect.Should().NotBeNull("Layer 6 Shroud effect must be registered");
        shroudEffect!.Target.Should().BeSameAs(land);
        shroudEffect.Layer.Should().Be(Layer.Abilities);
        shroudEffect.ExpiresAtEndOfTurn.Should().BeTrue();
    }

    [Fact]
    public void Compute_AfterActivate_AddCreatureTypeAndElementalSubtype_KeepsLand_GrantsShroud()
    {
        var effects = new ContinuousEffectsService();
        var land = CreepingTarPitFactory.Create(_alice, effects, replacements: null);
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
            "CR 613.1c — types are added; 'that's still a land'");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental, "Layer 4 adds Elemental subtype");
        chars.Keywords.Should().Contain("Shroud",
            "Layer 6 grants Shroud until end of turn (CR 702.18)");

        // CR 613.1c / 613.7b — the Compute creature-row upgrade now surfaces
        // the animated 3/2 through the layer system (manland combat math).
        chars.Should().BeOfType<CreatureCharacteristics>(
            "the Layer-4 Creature grant upgrades the Land's row to a creature row");
        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(3, "Creeping Tar Pit becomes a 3/2");
        cc.Toughness.Should().Be(2);
    }

    [Fact]
    public void EndOfTurn_ExpiresAllThreeEffects_LandRevertsToPlainIdentity()
    {
        var effects = new ContinuousEffectsService();
        var land = CreepingTarPitFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        activated.Resolve();

        // Sanity — mid-turn effects are live.
        GetRegisteredEffects(effects).OfType<CreepingTarPitAnimateEffect>().Should().HaveCount(1);
        GetRegisteredEffects(effects).OfType<CreepingTarPitBecomesPTEffect>().Should().HaveCount(1);
        GetRegisteredEffects(effects).OfType<CreepingTarPitShroudEffect>().Should().HaveCount(1);
        effects.Compute(land).Types.Should().Contain(CardType.Creature);

        // CR 514.2 — cleanup step lifts "until end of turn" effects.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<CreepingTarPitAnimateEffect>().Should().BeEmpty(
            "Layer 4 animate effect is end-of-turn-expirable");
        GetRegisteredEffects(effects).OfType<CreepingTarPitBecomesPTEffect>().Should().BeEmpty(
            "Layer 7b P/T effect is end-of-turn-expirable");
        GetRegisteredEffects(effects).OfType<CreepingTarPitShroudEffect>().Should().BeEmpty(
            "Layer 6 Shroud effect is end-of-turn-expirable");

        var afterChars = effects.Compute(land);
        afterChars.Types.Should().NotContain(CardType.Creature,
            "creature-ness lifts at end of turn");
        afterChars.Types.Should().Contain(CardType.Land);
        afterChars.Subtypes.Should().NotContain(CardSubtype.Elemental);
        afterChars.Keywords.Should().NotContain("Shroud");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
