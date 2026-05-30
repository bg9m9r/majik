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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PiaNalaarFactory"/> (Kaladesh).
///
/// Oracle (Scryfall, verified):
///   "When Pia Nalaar enters, create a 1/1 colorless Thopter artifact
///    creature token with flying.
///    {1}{R}: Target artifact creature gets +1/+0 until end of turn.
///    {1}, Sacrifice an artifact: Target creature can't block this turn."
///
/// Covers:
/// - Identity ({2}{R}, 2/2, Legendary Human Artificer).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger mints a 1/1 colourless flying Thopter artifact creature token.
/// - {1}{R} pump ability shape + +1/+0-until-EOT resolution on a target
///   artifact creature, with EOT expiry.
/// - {1}, Sacrifice an artifact ability shape + CannotBlock restriction
///   registration on the chosen target.
/// - Resolution guards: non-artifact pump target / null ActiveEffects no-op.
/// </summary>
public class PiaNalaarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaNalaar_Identity_LegendaryHumanArtificer()
    {
        var card = PiaNalaarFactory.Create(_alice);

        card.Name.Should().Be("Pia Nalaar");
        card.ManaCost.ToString().Should().Be("{2}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PiaNalaar_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Pia Nalaar", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Pia Nalaar");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaNalaar_HasExactlyOneEtbTrigger()
    {
        var card = PiaNalaarFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"create a Thopter\" trigger");
    }

    [Fact]
    public void PiaNalaar_HasTwoActivatedAbilities()
    {
        var card = PiaNalaarFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the {1}{R} pump and the {1},Sac-an-artifact can't-block abilities");
    }

    [Fact]
    public void PiaNalaar_PumpAbility_HasManaCostAndNoSacrifice()
    {
        var card = PiaNalaarFactory.Create(_alice);
        var pump = PiaNalaarFactory.GetPumpAbility(card);

        pump.Costs.OfType<SacrificeAnArtifactCost>().Should().BeEmpty(
            "the {1}{R} pump has no sacrifice cost");
        pump.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        pump.TargetRequests.Should().HaveCount(1);
        pump.TargetRequests[0].MinTargets.Should().Be(1);
        pump.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void PiaNalaar_CantBlockAbility_HasManaPlusSacrificeArtifactCost()
    {
        var card = PiaNalaarFactory.Create(_alice);
        var cantBlock = PiaNalaarFactory.GetCantBlockAbility(card);

        cantBlock.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {1} mana portion");
        cantBlock.Costs.OfType<SacrificeAnArtifactCost>().Should().ContainSingle(
            "Sacrifice an artifact (CR 118.5)");
        cantBlock.TargetRequests.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — create a 1/1 flying Thopter artifact creature token
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaNalaar_EtbEffect_MintsFlyingThopterToken()
    {
        var alice = new Player("Alice", 20);
        var card = PiaNalaarFactory.Create(alice);
        // Put Pia on the battlefield so the ETB source-zone check (CR 603.6c)
        // passes.
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var thopter = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.Name == "Thopter");

        thopter.IsToken.Should().BeTrue("CR 111.1 — minted as a token");
        thopter.BasePower.Should().Be(1);
        thopter.BaseToughness.Should().Be(1);
        thopter.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        thopter.HasType(CardType.Creature).Should().BeTrue();
        thopter.HasType(CardType.Artifact).Should().BeTrue(
            "Thopter token is an Artifact Creature (CR 111.1)");
        thopter.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "the printed Thopter token has flying (CR 702.9)");
    }

    [Fact]
    public void PiaNalaar_EtbEffect_NoOpWhenNotOnBattlefield()
    {
        // CR 603.6c — the ETB resolution short-circuits when the source isn't
        // on the battlefield.
        var alice = new Player("Alice", 20);
        var card = PiaNalaarFactory.Create(alice);
        card.SetZone(ZoneType.Hand);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Thopter")
            .Should().BeEmpty("no token when Pia isn't on the battlefield");
    }

    // -----------------------------------------------------------------------
    // {1}{R}: Target artifact creature gets +1/+0 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaNalaar_PumpEffect_GrantsPlusOnePlusZeroUntilEndOfTurn()
    {
        var card = PiaNalaarFactory.Create(_alice);

        var effects = new ContinuousEffectsService();
        var target = new Creature("Ornithopter", "0", 0, 2);
        target.AddCardType(CardType.Artifact);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        target.ActiveEffects = effects;

        var pump = PiaNalaarFactory.GetPumpAbility(card);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        foreach (var effect in pump.Effects) effect.Execute();

        var chars = effects.Compute(target);
        chars.Power.Should().Be(1, "0/2 base + 1/0 pump");
        chars.Toughness.Should().Be(2, "+1/+0 leaves toughness unchanged");

        // CR 514.2 — expires during cleanup.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(target);
        after.Power.Should().Be(0);
        after.Toughness.Should().Be(2);
    }

    [Fact]
    public void PiaNalaar_PumpEffect_NoOp_WhenTargetIsNotArtifact()
    {
        var card = PiaNalaarFactory.Create(_alice);

        var effects = new ContinuousEffectsService();
        // Plain (non-artifact) creature — fails the resolution-time recheck.
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        target.ActiveEffects = effects;

        var pump = PiaNalaarFactory.GetPumpAbility(card);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        foreach (var effect in pump.Effects) effect.Execute();

        var chars = effects.Compute(target);
        chars.Power.Should().Be(2, "no pump — target isn't an artifact (CR 608.2b)");
    }

    [Fact]
    public void PiaNalaar_PumpEffect_NullActiveEffects_DoesNotThrow()
    {
        var card = PiaNalaarFactory.Create(_alice);

        var target = new Creature("Ornithopter", "0", 0, 2);
        target.AddCardType(CardType.Artifact);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        // target.ActiveEffects is null.

        var pump = PiaNalaarFactory.GetPumpAbility(card);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = () => { foreach (var effect in pump.Effects) effect.Execute(); };
        act.Should().NotThrow("the effect body guards on null ActiveEffects");
    }

    // -----------------------------------------------------------------------
    // {1}, Sacrifice an artifact: Target creature can't block this turn
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaNalaar_CantBlockEffect_RegistersCannotBlockOnTarget()
    {
        var card = PiaNalaarFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var effects = new ContinuousEffectsService();
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        target.ActiveEffects = effects;

        var cantBlock = PiaNalaarFactory.GetCantBlockAbility(card);
        cantBlock.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        foreach (var effect in cantBlock.Effects) effect.Execute();

        effects.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeTrue(
            "the rider locks the chosen creature out of blocking this turn (CR 509.1c)");

        // CR 514.2 — restriction expires during cleanup.
        effects.ExpireEndOfTurn();
        effects.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeFalse(
            "the 'this turn' rider expires at cleanup");
    }

    [Fact]
    public void PiaNalaar_CantBlockEffect_NullActiveEffects_DoesNotThrow()
    {
        var card = PiaNalaarFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        // target.ActiveEffects is null.

        var cantBlock = PiaNalaarFactory.GetCantBlockAbility(card);
        cantBlock.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = () => { foreach (var effect in cantBlock.Effects) effect.Execute(); };
        act.Should().NotThrow("the effect body guards on null ActiveEffects");
    }

    // -----------------------------------------------------------------------
    // Sacrifice-an-artifact cost wiring
    // -----------------------------------------------------------------------

    [Fact]
    public void PiaNalaar_CantBlockCost_CannotPayWithoutAnArtifact()
    {
        var alice = new Player("Alice", 20);
        var card = PiaNalaarFactory.Create(alice);
        var sac = PiaNalaarFactory.GetCantBlockAbility(card)
            .Costs.OfType<SacrificeAnArtifactCost>().Single();

        sac.CanPay(alice).Should().BeFalse(
            "no artifact on the battlefield to sacrifice");
    }

    [Fact]
    public void PiaNalaar_CantBlockCost_CanPayWithAnArtifact()
    {
        var alice = new Player("Alice", 20);
        var card = PiaNalaarFactory.Create(alice);

        var fodder = new Artifact("Ornithopter", "0");
        fodder.SetOwner(alice);
        fodder.SetController(alice);
        fodder.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(fodder);

        var sac = PiaNalaarFactory.GetCantBlockAbility(card)
            .Costs.OfType<SacrificeAnArtifactCost>().Single();

        sac.CanPay(alice).Should().BeTrue("an artifact is available to sacrifice");
    }
}
