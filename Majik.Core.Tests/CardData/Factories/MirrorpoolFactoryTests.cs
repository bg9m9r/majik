using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MirrorpoolFactory"/> (Oath of the Gatewatch).
///
/// Oracle text (verified against the embedded Modern seed 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {C}.
///    {2}{C}, {T}, Sacrifice this land: Copy target instant or sorcery spell
///      you control. You may choose new targets for the copy.
///    {4}{C}, {T}, Sacrifice this land: Create a token that's a copy of target
///      creature you control."
///
/// Scryfall type line: Land.
///
/// Covers ONLY the card's unique behaviour (the contract test
/// (<see cref="Majik.Core.Tests.CardData.CardFactoryContractTests"/>) already
/// asserts dispatch + well-formedness):
/// - Three abilities: one {T}: Add {C} <see cref="ManaAbility"/> + two
///   sac-this-land <see cref="ActivatedAbility"/> copy abilities.
/// - {T}: Add {C} produces one colourless (folded into Generic).
/// - The copy-spell ability carries cost {2}{C} + {T} + Sacrifice and a 1..1
///   "target instant or sorcery spell you control" request.
/// - The token-copy ability carries cost {4}{C} + {T} + Sacrifice and a 1..1
///   "target creature you control" request; resolving it mints one token that
///   copies the targeted creature under the source's controller.
/// </summary>
[Trait("Color", "C")]
public class MirrorpoolFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land PlaceOnBattlefield()
    {
        var land = MirrorpoolFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return land;
    }

    private static ManaAbility ColorlessAbility(Land land)
        => land.Abilities.OfType<ManaAbility>().Single();

    private static ActivatedAbility CopySpellAbility(Land land)
        => land.Abilities.OfType<ActivatedAbility>().First();

    private static ActivatedAbility CopyCreatureAbility(Land land)
        => land.Abilities.OfType<ActivatedAbility>().Last();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsPlainLand_NamedMirrorpool()
    {
        var land = MirrorpoolFactory.Create(_alice);
        land.Name.Should().Be("Mirrorpool");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Mirrorpool is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("Mirrorpool is not legendary");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasExactlyThreeAbilities()
    {
        var land = MirrorpoolFactory.Create(_alice);
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the {T}: Add {C} mana ability");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the two sac-this-land copy abilities");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void ColorlessManaAbility_Produces_OneGenericColorless()
    {
        var land = PlaceOnBattlefield();
        var colorless = ColorlessAbility(land);

        colorless.CanActivate().Should().BeTrue();
        var produced = colorless.Activate();

        produced.Generic.Should().Be(1, "{C} folds into Generic");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    // -----------------------------------------------------------------------
    // {2}{C}, {T}, Sacrifice this land: Copy target instant or sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CopySpellAbility_HasManaTapSacCosts_AndOneSpellTarget()
    {
        var land = MirrorpoolFactory.Create(_alice);
        var ability = CopySpellAbility(land);

        ability.Costs.Should().HaveCount(3, "{2}{C} + {T} + Sacrifice");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<AdditionalCost>().Should().HaveCount(2, "tap + sacrifice");

        ability.TargetRequests.Should().ContainSingle();
        var req = ability.TargetRequests.Single();
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery spell");
    }

    // -----------------------------------------------------------------------
    // {4}{C}, {T}, Sacrifice this land: token copy of target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void CopyCreatureAbility_HasManaTapSacCosts_AndOneCreatureTarget()
    {
        var land = MirrorpoolFactory.Create(_alice);
        var ability = CopyCreatureAbility(land);

        ability.Costs.Should().HaveCount(3, "{4}{C} + {T} + Sacrifice");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<AdditionalCost>().Should().HaveCount(2, "tap + sacrifice");

        ability.TargetRequests.Should().ContainSingle();
        var req = ability.TargetRequests.Single();
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature you control");
    }

    [Fact]
    public void CopyCreatureAbility_Resolve_MintsTokenCopyOfTargetCreature()
    {
        var land = PlaceOnBattlefield();

        // A creature you control to copy.
        var original = new Creature(
            name: "Grizzly Bears",
            manaCost: "{1}{G}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Bear });
        original.SetOwner(_alice);
        original.SetController(_alice);
        original.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(original);

        var ability = CopyCreatureAbility(land);
        ability.SetChosenTargets(new[] { new object[] { original } });

        ability.Resolve();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1, "one token copy is created");
        var token = tokens.Single();
        token.Name.Should().Be("Grizzly Bears");
        token.BasePower.Should().Be(2);
        token.BaseToughness.Should().Be(2);
        token.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        token.HasType(CardType.Creature).Should().BeTrue();
    }
}
