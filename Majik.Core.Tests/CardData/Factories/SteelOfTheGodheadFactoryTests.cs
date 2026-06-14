using System.Linq;
using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SteelOfTheGodheadFactory"/>.
///
/// Card: Steel of the Godhead — Enchantment — Aura {2}{W/U} (Shadowmoor).
///   "Enchant creature
///    As long as enchanted creature is white, it gets +1/+1 and has lifelink.
///    As long as enchanted creature is blue, it gets +1/+1 and can't be
///    blocked."
///
/// Covers the card's UNIQUE behaviour (the two colour-conditional clauses)
/// plus a single identity assert. NamedCardFactory dispatch + well-formedness
/// are covered automatically by CardFactoryContractTests.
///
/// Multicolour ({W/U} hybrid → both W and U) → [Trait("Color", "M")].
/// </summary>
[Trait("Color", "M")]
public class SteelOfTheGodheadFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SteelOfTheGodhead_Identity()
    {
        var steel = SteelOfTheGodheadFactory.Create(_alice);

        steel.Name.Should().Be("Steel of the Godhead");
        steel.ManaCost.Should().Be("{2}{W/U}");
        steel.HasType(CardType.Enchantment).Should().BeTrue();
        steel.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        steel.IsAura.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (Enchantment steel, Creature bearer, ContinuousEffectsService fx) Attach(
        string bearerManaCost)
    {
        var fx = new ContinuousEffectsService();
        var steel = SteelOfTheGodheadFactory.Create(_alice, continuousEffects: fx);

        var bearer = new Creature("Bear", bearerManaCost, 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = fx,
        };
        _bob.Zones.Battlefield.AddCard(bearer);

        steel.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(steel);
        steel.AttachTo(bearer);

        return (steel, bearer, fx);
    }

    // -----------------------------------------------------------------------
    // White clause — +1/+1 and lifelink (CR 613 / 702.15)
    // -----------------------------------------------------------------------

    [Fact]
    public void WhiteCreature_GetsPlusOnePlusOneAndLifelink()
    {
        var (_, bearer, _) = Attach("{W}");

        bearer.Power.Should().Be(3, "white clause grants +1/+1");
        bearer.Toughness.Should().Be(3, "white clause grants +1/+1");
        bearer.HasEffectiveKeyword("Lifelink").Should().BeTrue("white clause grants lifelink");
    }

    [Fact]
    public void WhiteCreature_IsNotUnblockable()
    {
        var (_, bearer, fx) = Attach("{W}");

        fx.HasRestriction(bearer, CombatRestriction.CannotBeBlocked)
            .Should().BeFalse("the can't-be-blocked rider is the BLUE clause, not white");
    }

    // -----------------------------------------------------------------------
    // Blue clause — +1/+1 and can't be blocked (CR 613 / 509.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void BlueCreature_GetsPlusOnePlusOneAndCantBeBlocked()
    {
        var (_, bearer, fx) = Attach("{U}");

        bearer.Power.Should().Be(3, "blue clause grants +1/+1");
        bearer.Toughness.Should().Be(3, "blue clause grants +1/+1");
        fx.HasRestriction(bearer, CombatRestriction.CannotBeBlocked)
            .Should().BeTrue("blue clause grants can't be blocked (CR 509.1c)");
        bearer.HasEffectiveKeyword("Lifelink").Should().BeFalse("lifelink is the WHITE clause");
    }

    // -----------------------------------------------------------------------
    // Both colours — both clauses apply independently (CR 613)
    // -----------------------------------------------------------------------

    [Fact]
    public void WhiteAndBlueCreature_GetsPlusTwoPlusTwoLifelinkAndUnblockable()
    {
        var (_, bearer, fx) = Attach("{W}{U}");

        bearer.Power.Should().Be(4, "both clauses apply: +1/+1 twice");
        bearer.Toughness.Should().Be(4, "both clauses apply: +1/+1 twice");
        bearer.HasEffectiveKeyword("Lifelink").Should().BeTrue("white clause");
        fx.HasRestriction(bearer, CombatRestriction.CannotBeBlocked)
            .Should().BeTrue("blue clause");
    }

    // -----------------------------------------------------------------------
    // Neither colour — no clause applies
    // -----------------------------------------------------------------------

    [Fact]
    public void NonWhiteNonBlueCreature_GetsNothing()
    {
        var (_, bearer, fx) = Attach("{G}");

        bearer.Power.Should().Be(2, "neither clause applies to a green creature");
        bearer.Toughness.Should().Be(2, "neither clause applies to a green creature");
        bearer.HasEffectiveKeyword("Lifelink").Should().BeFalse();
        fx.HasRestriction(bearer, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CR 611.2 — the conditional effects lapse when the aura is unattached
    // -----------------------------------------------------------------------

    [Fact]
    public void Unattached_NoBoostNoRestriction()
    {
        var fx = new ContinuousEffectsService();
        var steel = SteelOfTheGodheadFactory.Create(_alice, continuousEffects: fx);

        var bearer = new Creature("Bear", "{W}{U}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = fx,
        };
        _bob.Zones.Battlefield.AddCard(bearer);

        steel.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(steel);
        // No AttachTo — aura on the battlefield but not attached.

        bearer.Power.Should().Be(2, "no boost without attachment");
        bearer.Toughness.Should().Be(2, "no boost without attachment");
        fx.HasRestriction(bearer, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // BuildSpellDefinition — candidate filter (CR 702.5b)
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OnlyCreaturesAreLegalTargets()
    {
        var steel = SteelOfTheGodheadFactory.Create(_alice);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);

        var land = new Land("Plains");
        var artifact = new Artifact("Black Lotus", "{0}");

        var battlefield = new Permanent[] { creature, land, artifact };
        var def = SteelOfTheGodheadFactory.BuildSpellDefinition(steel, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(creature, "creatures are legal Enchant-creature targets");
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(artifact);
    }
}
