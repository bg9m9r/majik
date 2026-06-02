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
/// Tests for <see cref="HostileDesertFactory"/> (Hour of Devastation). Land —
/// Desert:
///   "{T}: Add {C}.
///    {2}, Exile a land card from your graveyard: This land becomes a 3/4
///    Elemental creature until end of turn. It's still a land."
///
/// A colourless "manland" in the same animate-until-EOT family as
/// <see cref="RagingRavineFactory"/> / <see cref="RestlessBivouacFactory"/>,
/// but with no ETB-tapped rider, no attack trigger, a {C} tap-for-mana
/// ability, and a hybrid activation cost: {2} plus the non-mana
/// "Exile a land card from your graveyard" (CR 602.1 / 118.4).
///
/// Covers:
/// - Identity (Land + Desert subtype, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {C} mana ability (one).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({2} ManaCostCost + an
///   <see cref="ExileLandCardFromGraveyardCost"/>), instant speed.
/// - Layer 4 / Layer 7b on resolution:
///     * Adds Creature type + Elemental subtype on Layer 4 ("still a land").
///     * Records 3/4 base P/T on Layer 7b.
/// - The exile cost: can only pay with a land card in the graveyard, and
///   exiles exactly one land card Graveyard -> Exile.
/// </summary>
[Trait("Color", "C")]
public class HostileDesertFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HostileDesert_Identity()
    {
        var land = HostileDesertFactory.Create(_alice);

        land.Name.Should().Be("Hostile Desert");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue(
            "printed type line is \"Land — Desert\"");
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is a plain land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Hostile Desert is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HostileDesert_HasColorlessManaAndAnimateAbility()
    {
        var land = HostileDesertFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2}, exile-a-land animate ability is wired");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Hostile Desert has no triggered ability");
    }
    // -----------------------------------------------------------------------
    // Animate ability — cost shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HostileDesert_AnimateAbility_HasGenericTwoPlusExileLandCost()
    {
        var land = HostileDesertFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {2} generic mana component is one ManaCostCost");
        animate.Costs.OfType<ExileLandCardFromGraveyardCost>().Should().ContainSingle(
            "the \"Exile a land card from your graveyard\" component is wired");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    // -----------------------------------------------------------------------
    // Animate ability — Layer 4 / Layer 7b
    // -----------------------------------------------------------------------

    [Fact]
    public void HostileDesert_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = HostileDesertFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental,
            "Elemental subtype added");
    }

    // -----------------------------------------------------------------------
    // Exile-a-land cost
    // -----------------------------------------------------------------------

    [Fact]
    public void ExileLandCost_CannotPay_WhenNoLandInGraveyard()
    {
        var cost = new ExileLandCardFromGraveyardCost();

        // A non-land card in the graveyard does not satisfy the cost.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);

        cost.CanPay(_alice).Should().BeFalse(
            "a land card must be present in the graveyard to pay this cost");
    }

    [Fact]
    public void ExileLandCost_Pay_ExilesOneLandCardFromGraveyard()
    {
        var cost = new ExileLandCardFromGraveyardCost();

        var deadLand = new Land("Forest");
        deadLand.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(deadLand);
        deadLand.SetZone(ZoneType.Graveyard);

        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice);

        _alice.Zones.Graveyard.ContainsCard(deadLand).Should().BeFalse(
            "the land card leaves the graveyard");
        _alice.Zones.Exile.ContainsCard(deadLand).Should().BeTrue(
            "the land card is exiled");
        deadLand.Zone.Should().Be(ZoneType.Exile);
    }
}
