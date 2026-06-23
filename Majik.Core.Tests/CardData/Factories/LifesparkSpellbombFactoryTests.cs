using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="LifesparkSpellbombFactory"/> — Artifact {1} (Fifth
/// Dawn) with two sacrifice-self activated abilities:
///   "{G}, Sacrifice this artifact: Until end of turn, target land becomes a
///    3/3 creature that's still a land."
///   "{1}, Sacrifice this artifact: Draw a card."
///   (Oracle verified against Scryfall 2026-06-23.)
///
/// The {1}, sac: draw mode is the shared spellbomb cantrip (see
/// <see cref="AetherSpellbombFactory"/> / <see cref="NecrogenSpellbombFactory"/>);
/// the UNIQUE behaviour exercised here is the {G}, sac: animate-target-land
/// mode, which registers the generic <see cref="AnimateLandEffect"/> primitive
/// (Layer 4 Creature grant + Layer 7b 3/3 set-base, until EOT) on the chosen
/// target land while it stays a land (CR 701.59a).
/// </summary>
[Trait("Color", "C")]
public class LifesparkSpellbombFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void LifesparkSpellbomb_Identity()
    {
        var bomb = LifesparkSpellbombFactory.Create(_alice);

        bomb.Name.Should().Be("Lifespark Spellbomb");
        bomb.HasType(CardType.Artifact).Should().BeTrue();
        bomb.ManaCost.Should().Be("{1}");
        bomb.Owner.Should().BeSameAs(_alice);
        bomb.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void LifesparkSpellbomb_HasTwoActivatedAbilities()
    {
        var bomb = LifesparkSpellbombFactory.Create(_alice);

        bomb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void AnimateAbility_HasG_AndSacrifice_AndOneLandTarget()
    {
        var bomb = LifesparkSpellbombFactory.Create(_alice);

        var animate = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        animate.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("G"),
                "the animate mode costs {G}");
        animate.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the animate mode sacrifices the spellbomb");

        animate.TargetRequests[0].MinTargets.Should().Be(1);
        animate.TargetRequests[0].MaxTargets.Should().Be(1);
        animate.TargetRequests[0].Description.Should().Contain("land");
    }

    [Fact]
    public void DrawAbility_Has1Generic_AndSacrifice_AndNoTargets()
    {
        var bomb = LifesparkSpellbombFactory.Create(_alice);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "the cantrip mode costs {1}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip mode sacrifices the spellbomb");
    }

    // -----------------------------------------------------------------------
    // {G}, sac: target land becomes a 3/3 creature (still a land) until EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Animate_TargetLandBecomes3x3Creature_StillALand_AndSacrificesBomb()
    {
        var effects = new ContinuousEffectsService();

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var bomb = LifesparkSpellbombFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var animate = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
        animate.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { forest },
        });

        animate.Resolve();

        // CR 701.59a — "still a land": the printed Land type stays, Creature
        // is added, and base P/T becomes 3/3.
        var chars = effects.Compute((Permanent)forest);
        chars.Types.Should().Contain(CardType.Land,
            "the animated land is still a land (CR 701.59a)");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        // CR 613.1c — the Layer-4 grant drives the Compute creature-row
        // upgrade, so the Layer-7b set-base P/T lands on a creature row.
        chars.Should().BeOfType<CreatureCharacteristics>();
        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(3);
        cc.Toughness.Should().Be(3);

        // Spellbomb has been sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Animate_AnimationExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var bomb = LifesparkSpellbombFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var animate = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
        animate.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { forest },
        });
        animate.Resolve();

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        var chars = effects.Compute((Permanent)forest);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature,
            "the animation is until end of turn (CR 514.2)");
    }

    [Fact]
    public void Activate_Animate_NoEffectsService_NoOp_ButStillSacrifices()
    {
        // Shape-only path (no continuous-effects service): the animate
        // registration is a no-op, but the sacrifice cost is still paid.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var bomb = LifesparkSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var animate = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
        animate.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { forest },
        });

        var resolve = () => animate.Resolve();
        resolve.Should().NotThrow();

        forest.HasType(CardType.Creature).Should().BeFalse();
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // {1}, sac: draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Cantrip_DrawsACard_AndSacrificesBomb()
    {
        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bomb = LifesparkSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }
}
