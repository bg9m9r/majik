using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RamunapRuinsFactory"/>.
///
/// Ramunap Ruins — Land — Desert (Hour of Devastation).
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}, Pay 1 life: Add {R}.
///    {2}{R}{R}, {T}, Sacrifice a Desert: This land deals 2 damage to each
///    opponent."
///
/// Covers:
/// - Identity (Land, Desert subtype, non-Basic, non-Legendary, name,
///   owner/controller) + dispatcher routing through
///   <see cref="NamedCardFactory"/>.
/// - {T}: Add {C} — the colourless mana ability (CR 605.1) from the JSON
///   definition; {C} stored as generic (engine has no dedicated colourless
///   bucket).
/// - {T}, Pay 1 life: Add {R} — second mana ability producing {R}; activation
///   loses 1 life and taps the land; gated on life &gt; 1 (CR 119.4).
/// - {2}{R}{R}, {T}, Sacrifice a Desert: deal 2 damage to each opponent —
///   activated ability with mana + tap + sacrifice costs; resolution deals 2
///   to each opponent (resolver-injected) and sacrifices this land.
/// </summary>
[Trait("Color", "C")]
public class RamunapRuinsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RamunapRuins_IsLand_Desert_WithCorrectName()
    {
        var land = RamunapRuinsFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("printed type is Land — Desert");
        land.Name.Should().Be("Ramunap Ruins");
    }

    [Fact]
    public void RamunapRuins_OwnerAndControllerAreSet()
    {
        var land = RamunapRuinsFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RamunapRuins_IsNotBasic_AndNotLegendary()
    {
        var land = RamunapRuinsFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void RamunapRuins_RoutesThroughDispatcher()
    {
        var land = (Land)NamedCardFactory.Create("Ramunap Ruins", _alice);
        land.Name.Should().Be("Ramunap Ruins");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add {C} and {T}, Pay 1 life: Add {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void RamunapRuins_HasTwoManaAbilities_ColourlessAndRed()
    {
        var land = RamunapRuinsFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "{T}: Add {C} and {T}, Pay 1 life: Add {R}");

        // {C} is stored as generic (no dedicated colourless bucket).
        manaAbilities.Should().Contain(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.Red == 0,
            "{T}: Add {C} produces one colourless mana (modeled as generic)");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1,
            "{T}, Pay 1 life: Add {R} produces one red mana");
    }

    [Fact]
    public void RamunapRuins_RedManaAbility_Activation_LosesOneLifeAndTaps()
    {
        var land = RamunapRuinsFactory.Create(_alice);
        var redMana = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1);

        redMana.Activate();

        _alice.LifeTotal.Should().Be(19, "tapping for {R} costs Pay 1 life (CR 119.4)");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void RamunapRuins_ColourlessManaAbility_Activation_DoesNotLoseLife()
    {
        var land = RamunapRuinsFactory.Create(_alice);
        var colourless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.Red == 0);

        colourless.Activate();

        _alice.LifeTotal.Should().Be(20, "{T}: Add {C} has no life cost");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void RamunapRuins_RedManaAbility_CannotActivateAtOneLife()
    {
        var lowLife = new Player("LowLife", 1);
        var land = RamunapRuinsFactory.Create(lowLife);
        var redMana = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1);

        redMana.CanActivate().Should().BeFalse(
            "CR 119.4 — can't pay 1 life with only 1 life remaining");
    }

    // -----------------------------------------------------------------------
    // Sacrifice ability — {2}{R}{R}, {T}, Sacrifice a Desert: 2 dmg to each opp
    // -----------------------------------------------------------------------

    [Fact]
    public void RamunapRuins_HasExactlyOneActivatedAbility_WithManaTapAndSacrificeCosts()
    {
        var land = RamunapRuinsFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Red.Should().Be(2, "{2}{R}{R} charges two red");
        manaCost.Generic.Should().Be(2, "{2}{R}{R} charges two generic");
    }

    [Fact]
    public void RamunapRuins_SacAbility_DealsTwoDamageToEachOpponent_AndSacrificesSelf()
    {
        var land = RamunapRuinsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);

        // Re-create with opponent resolver wired so the damage half resolves.
        var land2 = RamunapRuinsFactory.Create(
            _alice,
            opponentResolver: () => new[] { bob, carol });
        _alice.Zones.Battlefield.AddCard(land2);
        land2.SetZone(ZoneType.Battlefield);

        var sac = land2.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        bob.LifeTotal.Should().Be(18, "each opponent takes 2 damage (CR 800.4)");
        carol.LifeTotal.Should().Be(18, "each opponent takes 2 damage (CR 800.4)");
        land2.Zone.Should().Be(ZoneType.Graveyard,
            "Ramunap Ruins sacrifices a Desert (itself) as part of resolution");
    }

    [Fact]
    public void RamunapRuins_SacAbility_WithoutResolver_NoOpsDamage_ButStillSacrifices()
    {
        var land = RamunapRuinsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        // Shape-only path: no opponent resolver, damage half no-ops, but the
        // sacrifice still happens (same posture as Electrostatic Field's
        // resolver-injected damage half).
        land.Zone.Should().Be(ZoneType.Graveyard);
    }
}
