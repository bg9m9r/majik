using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 205.4 / CR 613.1d — Layer 4 supertype-grant effect (Deferral #4). The
/// supertype analogue of the Layer-5 colour-grant family: the printed
/// supertype set is seeded, then <see cref="GrantSupertypeEffect"/> unions a
/// supertype onto it. <see cref="ContinuousEffectsService.EffectiveSupertypes"/>
/// (and <see cref="Permanent.HasEffectiveSupertype"/>) read the result, and
/// the legend-rule SBA consults that effective set.
/// </summary>
public class GrantSupertypeEffectTests
{
    [Fact]
    public void Compute_NoGrant_SeedsPrintedSupertypes()
    {
        var svc = new ContinuousEffectsService();
        var legend = new Creature("Isamaru", "W", 2, 2,
            supertypes: new[] { CardSupertype.Legendary }) { Zone = ZoneType.Battlefield };

        var chars = svc.Compute((Permanent)legend);

        chars.Supertypes.Should().BeEquivalentTo(new[] { CardSupertype.Legendary });
    }

    [Fact]
    public void Compute_GrantSupertype_UnionsOntoPrintedSupertypes()
    {
        var svc = new ContinuousEffectsService();
        // Printed non-legendary creature.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Zone = ZoneType.Battlefield };
        bear.ActiveEffects = svc;

        bear.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeFalse();

        svc.Register(GrantSupertypeEffect.ForPermanent(bear, CardSupertype.Legendary));

        // CR 613.1d — the grant adds Legendary to the effective supertype set.
        svc.EffectiveSupertypes(bear).Should().Contain(CardSupertype.Legendary);
        bear.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Grant_Inactive_WhenSourceLeavesBattlefield()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Zone = ZoneType.Battlefield };
        bear.ActiveEffects = svc;
        svc.Register(GrantSupertypeEffect.ForPermanent(bear, CardSupertype.Legendary));

        bear.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();

        // Source leaves the battlefield → IsActive() false → grant drops out.
        bear.Zone = ZoneType.Graveyard;

        bear.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeFalse(
            "the grant is anchored to the source's battlefield presence (CR 613)");
    }

    [Fact]
    public void GetEffectiveSupertypes_FallsBackToPrinted_WhenNoService()
    {
        // No ActiveEffects wired → printed supertypes are returned directly.
        var legend = new Creature("Isamaru", "W", 2, 2,
            supertypes: new[] { CardSupertype.Legendary }) { Zone = ZoneType.Battlefield };

        legend.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();
        legend.HasEffectiveSupertype(CardSupertype.Snow).Should().BeFalse();
    }
}
