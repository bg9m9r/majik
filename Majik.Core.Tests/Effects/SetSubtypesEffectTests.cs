using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 613.1d / 205.3 — Layer 4 subtype-setting effect (e.g. Blood Moon,
/// Spreading Seas, Magus of the Moon, Conversion). The effect rewrites
/// only subtypes in its category, leaving out-of-category subtypes
/// alone, and respects its scope predicate.
/// </summary>
public class SetSubtypesEffectTests
{
    private static readonly IReadOnlySet<CardSubtype> MountainOnly =
        new HashSet<CardSubtype> { CardSubtype.Mountain };

    /// <summary>
    /// Blood Moon hitting a dual land: Bayou (Forest, Swamp) becomes a
    /// Mountain — both prior land subtypes are wiped and replaced.
    /// </summary>
    [Fact]
    public void Replaces_LandSubtypeWithMountain()
    {
        var svc = new ContinuousEffectsService();
        var bayou = new Land("Bayou",
            subtypes: new[] { CardSubtype.Forest, CardSubtype.Swamp })
        {
            Zone = ZoneType.Battlefield,
        };
        // Source stand-in: a battlefield permanent (acts as the
        // Blood-Moon-equivalent enchantment hosting the effect). The
        // effect only requires its Source to be on the battlefield to
        // be active; it doesn't care about Source's type here.
        var source = new Land("Source",
            subtypes: new[] { CardSubtype.Plains })
        {
            Zone = ZoneType.Battlefield,
        };

        svc.Register(new SetSubtypesEffect(
            source,
            scope: p => p.CardTypes.Contains(CardType.Land) && !ReferenceEquals(p, source),
            category: LandSubtypes.All,
            newSubtypes: MountainOnly));

        var chars = svc.Compute((Permanent)bayou);

        chars.Subtypes.Should().Contain(CardSubtype.Mountain);
        chars.Subtypes.Should().NotContain(CardSubtype.Forest);
        chars.Subtypes.Should().NotContain(CardSubtype.Swamp);
    }

    /// <summary>
    /// Dryad-Arbor-style creature-land: a Permanent that is both a
    /// Creature and a Land, carrying both a creature subtype (Dryad)
    /// and a land subtype (Forest). Blood Moon rewrites land subtypes
    /// only — Dryad (out of <see cref="LandSubtypes.All"/>) must survive.
    ///
    /// Fixture choice: <see cref="Land"/> hard-codes CardType.Land and
    /// <see cref="Creature"/> hard-codes CardType.Creature, so neither
    /// can host both. Drop down to constructing a raw <see cref="Permanent"/>
    /// directly with both card types and both subtypes — sufficient for
    /// exercising the Layer 4 subtype-category logic, which is all this
    /// test cares about.
    /// </summary>
    [Fact]
    public void Preserves_OutOfCategorySubtypes()
    {
        var svc = new ContinuousEffectsService();
        var dryadArbor = new Permanent(
            "Dryad Arbor",
            manaCost: "",
            cardTypes: new[] { CardType.Creature, CardType.Land },
            subtypes: new[] { CardSubtype.Dryad, CardSubtype.Forest })
        {
            Zone = ZoneType.Battlefield,
        };
        var source = new Land("Source") { Zone = ZoneType.Battlefield };

        svc.Register(new SetSubtypesEffect(
            source,
            scope: p => p.CardTypes.Contains(CardType.Land) && !ReferenceEquals(p, source),
            category: LandSubtypes.All,
            newSubtypes: MountainOnly));

        var chars = svc.Compute((Permanent)dryadArbor);

        // Creature subtype outside the land category — must survive.
        chars.Subtypes.Should().Contain(CardSubtype.Dryad);
        // Land subtype inside the category — replaced.
        chars.Subtypes.Should().NotContain(CardSubtype.Forest);
        chars.Subtypes.Should().Contain(CardSubtype.Mountain);
    }

    /// <summary>
    /// Scope predicate returns false → the effect is registered but
    /// doesn't apply to this land. Its printed subtypes survive
    /// unchanged.
    /// </summary>
    [Fact]
    public void OutOfScope_LandIsUntouched()
    {
        var svc = new ContinuousEffectsService();
        var forest = new Land("Forest",
            subtypes: new[] { CardSubtype.Forest })
        {
            Zone = ZoneType.Battlefield,
        };
        var source = new Land("Source") { Zone = ZoneType.Battlefield };

        svc.Register(new SetSubtypesEffect(
            source,
            scope: _ => false,
            category: LandSubtypes.All,
            newSubtypes: MountainOnly));

        var chars = svc.Compute((Permanent)forest);

        chars.Subtypes.Should().Contain(CardSubtype.Forest);
        chars.Subtypes.Should().NotContain(CardSubtype.Mountain);
    }
}
