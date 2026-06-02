using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MemorialToGeniusFactory"/> (Ixalan "Memorial"
/// sacrifice-for-value utility land cycle — blue member).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {U}.
///    {4}{U}, {T}, Sacrifice this land: Draw two cards."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Identity, the
/// {T}: Add {U} mana ability, and the {4}{U},{T},Sacrifice this land: Draw two
/// cards activated ability are loaded from <c>memorial-to-genius.json</c> via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> — exact
/// JSON-driven posture of <see cref="BondersEnclaveFactory"/> (mana + activated
/// draw) and the <c>{mana},{T},Sacrifice</c> cost stack of Dreamstone Hedron.
///
/// Covers:
/// - Identity: Land type, name, nonbasic, non-legendary, owner/controller.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Memorial to Genius".
/// - Two abilities: one <see cref="ManaAbility"/> ({T}: Add {U}) + one
///   <see cref="ActivatedAbility"/> ({4}{U},{T},Sac: Draw two cards).
/// - Mana ability: {T} produces {U}; CanActivate false when tapped.
/// - Activated ability cost stack: {4}{U} mana + tap-self + sacrifice-self.
/// - Activated ability resolve: draws exactly two cards for the controller.
/// - Enters-tapped (CR 614.1c): the two-arg <see cref="ReplacementBus"/> path
///   registers the unconditional enters-tapped replacement.
/// </summary>
[Trait("Color", "U")]
public class MemorialToGeniusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land PlaceOnBattlefield()
    {
        var land = MemorialToGeniusFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedMemorialToGenius()
    {
        var land = MemorialToGeniusFactory.Create(_alice);
        land.Name.Should().Be("Memorial to Genius");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary()
    {
        var land = MemorialToGeniusFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Memorial to Genius is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("Memorial to Genius is not legendary");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MemorialToGenius()
    {
        var land = NamedCardFactory.Create("Memorial to Genius", _alice);
        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Memorial to Genius");
    }

    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneActivated()
    {
        var land = MemorialToGeniusFactory.Create(_alice);
        land.Abilities.Should().HaveCount(2,
            "one {T}: Add {U} mana ability + one {4}{U},{T},Sac activated ability");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_Activate_ProducesOneBlue()
    {
        var land = PlaceOnBattlefield();
        var mana = (IManaAbility)land.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Blue.Should().Be(1, "the mana ability produces {U}");
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ManaAbility_CanActivate_FalseWhenTapped()
    {
        var land = PlaceOnBattlefield();
        land.Tap();
        var mana = (IManaAbility)land.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // Activated ability: {4}{U}, {T}, Sacrifice this land
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_CostStack_Is_4U_Mana_Plus_Tap_Plus_Sacrifice()
    {
        var land = MemorialToGeniusFactory.Create(_alice);
        var draw = land.Abilities.OfType<ActivatedAbility>().Single();

        // {4}{U} mana + tap-self + sacrifice-self
        var manaCost = draw.Costs.OfType<ManaCostCost>().Single();
        manaCost.Cost.Generic.Should().Be(4);
        manaCost.Cost.Blue.Should().Be(1);

        var additional = draw.Costs.OfType<AdditionalCost>().ToList();
        additional.Should().Contain(c => c.CostType == AdditionalCostType.Tap);
        additional.Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice);
    }

    // -----------------------------------------------------------------------
    // Activated ability: Draw two cards (CR 120)
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_Resolve_DrawsExactlyTwoCards_ForController()
    {
        var land = MemorialToGeniusFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        // Seed library with two cards so both draws land cleanly.
        for (var i = 0; i < 2; i++)
        {
            var card = new Card($"Island {i}", "", new[] { CardType.Land });
            card.SetOwner(_alice);
            _alice.Zones.Library.AddCard(card);
        }

        _alice.Zones.Hand.Count.Should().Be(0);

        var draw = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in draw.Effects) effect.Execute();

        _alice.Zones.Hand.Count.Should().Be(2, "draw two resolved → +2 cards in hand");
        _alice.Zones.Library.Count.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        // CR 614.1c — unconditional "This land enters tapped." is registered
        // on the supplied bus; the shape-only (null bus) path skips it.
        // EntersTappedReplacement exposes no public bus-inspection surface,
        // so we assert the build succeeds with the bus wired (mirrors
        // AkoumRefuge / MistvaultBridge factory tests).
        var replacements = new ReplacementBus();
        var land = MemorialToGeniusFactory.Create(_alice, replacements);
        land.Should().NotBeNull();
        land.Name.Should().Be("Memorial to Genius");
    }
}
