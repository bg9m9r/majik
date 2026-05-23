using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 305.6 / 305.7 — once Layer 4 has retyped a land to a basic land
/// subtype the printed card didn't have, the land "loses any abilities
/// printed on the card and gains the appropriate mana ability for each
/// new basic land type." <see cref="EffectiveManaAbilities"/> is the
/// derivation point consulted by the mana-payment + bot enumeration
/// surfaces.
/// </summary>
public class EffectiveManaAbilitiesTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>
    /// Baseline: a printed Forest with no Layer 4 effects active. The
    /// helper must return the Forest's printed {T}: Add {G} unchanged.
    /// Construction path mirrors OracleManaBinder.BindBasicLandMana so
    /// the printed ability is the same one production uses.
    /// </summary>
    [Fact]
    public void Forest_PrintedG_NoOverride_ReturnsG()
    {
        var svc = new ContinuousEffectsService();
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest })
        {
            Zone = ZoneType.Battlefield,
        };
        forest.ChangeController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);

        var abilities = EffectiveManaAbilities.For(forest, svc);

        abilities.Should().HaveCount(1);
        abilities[0].ManaGenerated.Green.Should().Be(1);
        abilities[0].ManaGenerated.Red.Should().Be(0);
    }

    /// <summary>
    /// Blood Moon analogue: a printed Forest is in scope of a
    /// <see cref="SetSubtypesEffect"/> rewriting its land subtypes to
    /// {Mountain}. CR 305.6 — the printed {G} ability is replaced by
    /// the Mountain's {R} ability. Confirms the override fully filters
    /// the printed ability out (no leakage of both colours).
    /// </summary>
    [Fact]
    public void Forest_UnderBloodMoonLikeSetSubtypes_ReturnsR_Not_G()
    {
        var svc = new ContinuousEffectsService();
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest })
        {
            Zone = ZoneType.Battlefield,
        };
        forest.ChangeController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);

        // Source stand-in for the Blood-Moon-equivalent enchantment.
        // The Layer 4 effect is registered on the same service the
        // helper consults — scope picks out only the Forest under test.
        var source = new Land("Source") { Zone = ZoneType.Battlefield };
        svc.Register(new SetSubtypesEffect(
            source,
            scope: p => ReferenceEquals(p, forest),
            category: LandSubtypes.All,
            newSubtypes: new HashSet<CardSubtype> { CardSubtype.Mountain }));

        var abilities = EffectiveManaAbilities.For(forest, svc);

        abilities.Should().HaveCount(1);
        abilities[0].ManaGenerated.Red.Should().Be(1);
        abilities[0].ManaGenerated.Green.Should().Be(0, "CR 305.6 strips the printed Forest ability");
    }

    /// <summary>
    /// A non-basic-subtyped land (here: a synthetic "Cave" land with no
    /// basic land subtype) carries a printed {T}: Add {C} mana ability.
    /// No Layer 4 effect rewrites its subtypes → CR 305.6 doesn't fire,
    /// the printed ability passes through untouched.
    ///
    /// Fixture note: <see cref="Land"/> hard-codes <see cref="CardType.Land"/>
    /// and accepts arbitrary subtype lists; using a subtype outside
    /// <see cref="BasicLandManaColors"/> (no CardSubtype enum value
    /// needed — we pass an empty subtype list) is the cleanest way to
    /// hit the "no newly acquired basic subtypes" branch without
    /// dragging in CardSubtype.Cave or similar.
    /// </summary>
    [Fact]
    public void Land_WithoutRetyping_PreservesPrintedAbilities()
    {
        var svc = new ContinuousEffectsService();
        var crypt = new Land("Mishra's Workshop")
        {
            Zone = ZoneType.Battlefield,
        };
        crypt.ChangeController(_alice);
        // Hand-attach a custom printed mana ability — {T}: Add {C}{C}{C}.
        // This stands in for any nonbasic land's bespoke mana ability;
        // the helper should not synthesize over it.
        var printed = new ManaAbility(crypt, _alice, ManaCost.Parse("CCC"));
        crypt.AddAbility(printed);

        var abilities = EffectiveManaAbilities.For(crypt, svc);

        abilities.Should().ContainSingle();
        abilities[0].Should().BeSameAs(printed,
            "no Layer 4 retyping fired → printed ability is returned by reference");
    }
}
