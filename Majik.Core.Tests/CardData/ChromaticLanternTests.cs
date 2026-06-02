using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Chromatic Lantern (Guilds of Ravnica, {3} Artifact).
///
/// Oracle text (verified against Scryfall):
///   "Lands you control have "{T}: Add one mana of any color."
///    {T}: Add one mana of any color."
///
/// Validates the CR 613.1f Layer-6 ability-adding group static
/// (<see cref="GrantAbilityToGroupStaticEffect"/> /
/// <see cref="GrantAbilityToGroupLifecycle"/>): the any-color mana ability
/// is granted to EVERY land the controller controls, live-membership
/// recomputed as lands enter / leave (CR 611.2c), and surfaces through
/// <see cref="EffectiveManaAbilities"/> so each land can be tapped for any
/// colour.
/// </summary>
public class ChromaticLanternTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public ChromaticLanternTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
    }

    private Land Forest()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.ChangeOwner(_alice);
        forest.ChangeController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);
        return forest;
    }

    /// <summary>
    /// Put a freshly-built card onto the battlefield via the real zone flow:
    /// seed it into the owner's Library first so
    /// <see cref="Majik.Core.Zones.ZoneManager.MoveCard"/> can move it (and the
    /// owner's Battlefield zone is genuinely populated — the group grant
    /// enumerates that zone).
    /// </summary>
    private void PutOnBattlefield(ICard card)
    {
        _alice.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);
    }

    private static bool ProducesColor(IManaAbility a, char wubrg)
    {
        var m = a.ManaGenerated;
        return wubrg switch
        {
            'W' => m.White == 1,
            'U' => m.Blue == 1,
            'B' => m.Black == 1,
            'R' => m.Red == 1,
            'G' => m.Green == 1,
            _ => false,
        };
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ChromaticLantern_IsThreeManaArtifact()
    {
        var lantern = ChromaticLanternFactory.Create(_alice);

        lantern.Name.Should().Be("Chromatic Lantern");
        lantern.HasType(CardType.Artifact).Should().BeTrue();
        lantern.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ChromaticLantern()
    {
        var lantern = NamedCardFactory.Create("Chromatic Lantern", _alice);

        lantern.Should().BeOfType<Artifact>();
        lantern.Name.Should().Be("Chromatic Lantern");
    }

    // -----------------------------------------------------------------------
    // The lantern's OWN {T}: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void ChromaticLantern_OwnAnyColorManaAbility_FiveColors()
    {
        var lantern = ChromaticLanternFactory.Create(_alice);

        var own = lantern.Abilities.OfType<IManaAbility>().ToList();
        own.Should().HaveCount(5, "the lantern's own {T}: Add any color = five single-colour abilities");
        foreach (var c in "WUBRG")
            own.Should().Contain(a => ProducesColor(a, c), $"lantern itself taps for {c}");
    }

    // -----------------------------------------------------------------------
    // Grant to every land the controller controls
    // -----------------------------------------------------------------------

    [Fact]
    public void ChromaticLantern_OnBattlefield_ForestTapsForAnyColor()
    {
        var forest = Forest();
        PutOnBattlefield(forest);

        var lantern = ChromaticLanternFactory.Create(_alice, _effects, _bus);
        PutOnBattlefield(lantern);

        var abilities = EffectiveManaAbilities.For(forest, _effects, _alice);

        // Printed {G} preserved; granted any-colour adds W/U/B/R (G already
        // present). The Forest can now be tapped for {U}.
        abilities.Should().Contain(a => ProducesColor(a, 'U'),
            "Chromatic Lantern grants the Forest {T}: Add one mana of any color");
        foreach (var c in "WUBRG")
            abilities.Should().Contain(a => ProducesColor(a, c), $"Forest taps for {c} under the lantern");
    }

    [Fact]
    public void ChromaticLantern_LandEnteringLater_AlsoGainsAnyColor()
    {
        var lantern = ChromaticLanternFactory.Create(_alice, _effects, _bus);
        PutOnBattlefield(lantern);

        // Forest enters AFTER the lantern — live membership must pick it up.
        var forest = Forest();
        PutOnBattlefield(forest);

        var abilities = EffectiveManaAbilities.For(forest, _effects, _alice);

        abilities.Should().Contain(a => ProducesColor(a, 'U'),
            "a land entering after the lantern still gains the any-colour ability (CR 611.2c)");
    }

    [Fact]
    public void ChromaticLantern_DoesNotGrantToNonLands()
    {
        var lantern = ChromaticLanternFactory.Create(_alice, _effects, _bus);
        PutOnBattlefield(lantern);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.ChangeOwner(_alice);
        bear.ChangeController(_alice);
        PutOnBattlefield(bear);

        bear.Abilities.OfType<IManaAbility>().Should().BeEmpty(
            "the grant scope is 'lands you control' — a creature gains nothing");
    }

    [Fact]
    public void ChromaticLantern_Leaves_ForestLosesAnyColor()
    {
        var forest = Forest();
        PutOnBattlefield(forest);

        var lantern = ChromaticLanternFactory.Create(_alice, _effects, _bus);
        PutOnBattlefield(lantern);

        // Sanity: granted while on battlefield.
        EffectiveManaAbilities.For(forest, _effects, _alice)
            .Should().Contain(a => ProducesColor(a, 'U'));

        // Lantern leaves — revoke the grant.
        _zones.MoveCard(lantern, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        _effects.Prune();

        var after = EffectiveManaAbilities.For(forest, _effects, _alice);
        after.Should().NotContain(a => ProducesColor(a, 'U'),
            "with the lantern gone the Forest is back to {G} only");
        after.Should().OnlyContain(a => ProducesColor(a, 'G'),
            "only the printed Forest {G} ability remains");
    }
}
