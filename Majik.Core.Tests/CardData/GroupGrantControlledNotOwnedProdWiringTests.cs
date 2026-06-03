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
/// Production-wiring tests for the controlled-but-not-owned cross-battlefield
/// enumeration of a CR 613.1f Layer-6 group ability-grant
/// (<see cref="GrantAbilityToGroupStaticEffect"/>).
///
/// <para>The whole-board gatherer (<see cref="BattlefieldGroupGatherer.WholeBattlefield"/>)
/// + the factories' explicit players-provider overload were already covered by
/// <see cref="ChromaticLanternTests"/> / <see cref="KatakiWarsWageTests"/>. The
/// remaining gap (the deferral) was the PRODUCTION path: the source-generated
/// instance-swap dispatch only calls a factory's
/// <c>Create(Player, ContinuousEffectsService)</c> overload — it threads no
/// players provider — so a real match fell back to walking only the
/// controller's own battlefield zone and MISSED a permanent the controller
/// controls but an opponent owns (a stolen permanent lives in the OWNER's
/// battlefield zone collection, CR 110.2 / 700.6).</para>
///
/// <para>The fix: the live game graph wires
/// <see cref="ContinuousEffectsService.PlayersProvider"/> (GameFacade / Game),
/// and the factories' effects-aware overload derives the whole-battlefield
/// gatherer from it. These tests drive exactly that production entry point —
/// <c>NamedCardFactory.Create(name, owner, effects)</c> with the roster wired on
/// the service, NO explicit per-factory players provider — and assert the
/// stolen permanent is enumerated.</para>
/// </summary>
public class GroupGrantControlledNotOwnedProdWiringTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public GroupGrantControlledNotOwnedProdWiringTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
        // Mirror the production game graph (GameFacade / Game): the live player
        // roster is wired onto the effects service so a controller-scoped group
        // grant can enumerate BOTH battlefields.
        _effects.PlayersProvider = () => new[] { _alice, _bob };
    }

    private void PutOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
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

    private Land BobForestAliceControls()
    {
        // Bob owns + enters the Forest; Alice then steals control. The card stays
        // in Bob's battlefield zone collection (control change is not a zone
        // move), but Permanent.Controller now points at Alice.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.ChangeOwner(_bob);
        forest.ChangeController(_bob);
        forest.ActiveEffects = _effects; // prod wires this on every battlefield permanent
        OracleManaBinder.BindBasicLandMana(forest, _bob);
        PutOnBattlefield(forest, _bob);
        forest.ChangeController(_alice);
        return forest;
    }

    // -----------------------------------------------------------------------
    // Chromatic Lantern — "Lands you control have '{T}: Add any color.'"
    // -----------------------------------------------------------------------

    /// <summary>
    /// RED before the fix: built through the PRODUCTION effects-aware dispatch
    /// (<c>NamedCardFactory.Create(name, owner, effects)</c>) with the roster on
    /// the service but NO explicit players provider, a Bob-owned Forest that
    /// Alice controls must still be granted the any-colour ability — it is one
    /// of Alice's "lands you control" (CR 110.2 / 700.6 / 611.2c).
    /// </summary>
    [Fact]
    public void ChromaticLantern_ProdDispatch_GrantsToStolenLand_AliceControlsBobOwns()
    {
        var stolen = BobForestAliceControls();

        // Production seam: the routed instance-swap build calls this exact
        // overload. No per-factory players provider is passed.
        var lantern = NamedCardFactory.Create("Chromatic Lantern", _alice, _effects);
        PutOnBattlefield(lantern, _alice);

        var abilities = EffectiveManaAbilities.For(stolen, _effects, _alice);
        abilities.Should().Contain(a => ProducesColor(a, 'U'),
            "the stolen land Alice controls is one of 'lands you control' and gains any-colour " +
            "mana even via the production dispatch, though it lives in Bob's battlefield zone");
        foreach (var c in "WUBRG")
            abilities.Should().Contain(a => ProducesColor(a, c));
    }

    /// <summary>
    /// Flip side via the production dispatch: a land Alice OWNS but Bob now
    /// CONTROLS (it still sits in Alice's battlefield zone) must NOT be granted
    /// by Alice's lantern — the effective-controller scope excludes it.
    /// </summary>
    [Fact]
    public void ChromaticLantern_ProdDispatch_DoesNotGrantToLandAliceOwnsButBobControls()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.ChangeOwner(_alice);
        forest.ChangeController(_alice);
        forest.ActiveEffects = _effects;
        OracleManaBinder.BindBasicLandMana(forest, _alice);
        PutOnBattlefield(forest, _alice);
        forest.ChangeController(_bob); // Bob steals it; stays in Alice's zone.

        var lantern = NamedCardFactory.Create("Chromatic Lantern", _alice, _effects);
        PutOnBattlefield(lantern, _alice);

        var abilities = EffectiveManaAbilities.For(forest, _effects, _bob);
        abilities.Should().NotContain(a => ProducesColor(a, 'U'),
            "Bob controls the land, so it is not one of Alice's 'lands you control'");
        abilities.Should().OnlyContain(a => ProducesColor(a, 'G'),
            "only the printed Forest {G} remains for the land Alice owns but does not control");
    }

    // -----------------------------------------------------------------------
    // Kataki, War's Wage — "All artifacts have '… upkeep tax'" (symmetric)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Kataki's grant is SYMMETRIC ("All artifacts"). Built through the
    /// production effects-aware dispatch with the roster on the service, the
    /// grant must reach an OPPONENT's artifact (on Bob's battlefield), not just
    /// the controller's own — before the fix the prod path walked only Alice's
    /// battlefield zone and missed every opponent artifact entirely.
    /// </summary>
    [Fact]
    public void Kataki_ProdDispatch_TaxesOpponentArtifactAcrossBattlefields()
    {
        var bobArtifact = new Artifact("Mox Ruby", "{0}");
        bobArtifact.ChangeOwner(_bob);
        bobArtifact.ChangeController(_bob);
        bobArtifact.ActiveEffects = _effects;
        PutOnBattlefield(bobArtifact, _bob);

        // Production seam — no explicit membership provider / triggers.
        var kataki = NamedCardFactory.Create("Kataki, War's Wage", _alice, _effects);
        PutOnBattlefield(kataki, _alice);

        bobArtifact.Abilities.OfType<ITriggeredAbility>().Should().NotBeEmpty(
            "Kataki's 'All artifacts have …' grant reaches Bob's artifact on the OTHER " +
            "battlefield via the whole-board gatherer wired from the service roster");
    }

    /// <summary>
    /// And the controlled-but-not-owned case for the symmetric grant: a stolen
    /// artifact (Bob owns, Alice controls, lives in Bob's zone) is still in the
    /// "all artifacts" group through the production dispatch.
    /// </summary>
    [Fact]
    public void Kataki_ProdDispatch_TaxesStolenArtifact_AliceControlsBobOwns()
    {
        var stolen = new Artifact("Mox Sapphire", "{0}");
        stolen.ChangeOwner(_bob);
        stolen.ChangeController(_bob);
        stolen.ActiveEffects = _effects;
        PutOnBattlefield(stolen, _bob);
        stolen.ChangeController(_alice);

        var kataki = NamedCardFactory.Create("Kataki, War's Wage", _alice, _effects);
        PutOnBattlefield(kataki, _alice);

        stolen.Abilities.OfType<ITriggeredAbility>().Should().NotBeEmpty(
            "the stolen artifact Alice controls but Bob owns is enumerated by the " +
            "whole-board 'all artifacts' grant even though it lives in Bob's zone");
    }
}
