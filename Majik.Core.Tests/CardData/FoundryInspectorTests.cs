using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FoundryInspectorFactory"/>.
///
/// Card: Foundry Inspector — Artifact Creature — Construct
///   {3}, 3/2 (Kaladesh).
///   "Artifact spells you cast cost {1} less to cast."
///
/// Mirrors <see cref="EtheriumSculptorTests"/> (the suggested analogue: same
/// static "Artifact spells you cast cost {1} less" reducer). Covers:
/// - Identity (name, {3}, Artifact + Creature types, Construct subtype, 3/2,
///   owner/controller).
/// - NamedCardFactory dispatch returns a Creature shell with the
///   SpellCostReductionAbility rider attached.
/// - Spell-cost reduction rider (CR 117.7):
///     * Artifact spell cast — generic cost reduced by 1.
///     * Artifact creature spell cast — reduced (it's still an Artifact).
///     * Non-artifact spell cast — no reduction.
///     * Off-battlefield Inspector — no reduction.
///     * Two Inspectors stack — reduction is additive.
///     * Coloured pips untouched + floor-at-zero.
///     * Opponent's Inspector doesn't discount your spells ("you cast").
/// </summary>
public class FoundryInspectorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Artifact ArtifactSpell(Player owner, string name, string manaCost)
    {
        var a = new Artifact(name, manaCost);
        a.SetOwner(owner);
        a.SetController(owner);
        return a;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void FoundryInspector_Identity()
    {
        var c = FoundryInspectorFactory.Create(_alice);

        c.Name.Should().Be("Foundry Inspector");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue("Foundry Inspector is an Artifact Creature (CR 301.1)");
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue("Construct is a printed subtype");
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the artifact-spell cost-reduction rider is attached");
    }

    [Fact]
    public void FoundryInspector_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Foundry Inspector", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Foundry Inspector");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Cost-reduction rider (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void ArtifactSpellCast_GenericReducedByOne()
    {
        var inspector = FoundryInspectorFactory.Create(_alice);
        PutOnBattlefield(_alice, inspector);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(2, "{3} generic reduced by 1 → {2}");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void ArtifactCreatureSpellCast_GenericReducedByOne()
    {
        // An artifact creature spell is still an Artifact spell — discounted.
        var inspector = FoundryInspectorFactory.Create(_alice);
        PutOnBattlefield(_alice, inspector);

        var artCreature = new Creature("Test Golem", "{4}", power: 4, toughness: 4);
        artCreature.AddCardType(CardType.Artifact);
        artCreature.SetOwner(_alice);
        artCreature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(artCreature, _alice);

        effective.Generic.Should().Be(3, "{4} generic reduced by 1 → {3} — it's an Artifact spell");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NonArtifactSpellCast_NoReduction()
    {
        var inspector = FoundryInspectorFactory.Create(_alice);
        PutOnBattlefield(_alice, inspector);

        // A vanilla creature — {2}{G}. Not an artifact → no discount.
        var creature = new Creature("Test Beast", "{2}{G}", power: 3, toughness: 3);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(2, "non-artifact spell — no Foundry Inspector discount");
        effective.Green.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var inspector = FoundryInspectorFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(inspector);
        inspector.SetZone(ZoneType.Hand);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(3, "Inspector isn't on the battlefield — no discount");
    }

    [Fact]
    public void TwoInspectors_ReductionStacks()
    {
        var i1 = FoundryInspectorFactory.Create(_alice);
        var i2 = FoundryInspectorFactory.Create(_alice);
        PutOnBattlefield(_alice, i1);
        PutOnBattlefield(_alice, i2);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(1, "two Inspectors reduce {3} generic → {1}");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void ColourlessArtifact_FloorsAtZero()
    {
        // A {1} artifact. Reducer drives generic to 0; floor-at-zero
        // (CR 117.7c) — never negative.
        var inspector = FoundryInspectorFactory.Create(_alice);
        PutOnBattlefield(_alice, inspector);

        var artifact = ArtifactSpell(_alice, "Cheap Artifact", "{1}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(0, "{1} reduced by 1 → {0}; floor-at-zero (CR 117.7c)");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void ColouredArtifactSpell_PipUntouched()
    {
        // An artifact spell with a coloured pip — {2}{U}. Only generic reduces.
        var inspector = FoundryInspectorFactory.Create(_alice);
        PutOnBattlefield(_alice, inspector);

        var artifact = ArtifactSpell(_alice, "Blue Artifact", "{2}{U}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Blue.Should().Be(1, "coloured pip untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void OpponentControlsInspector_DoesNotDiscountYourSpells()
    {
        // Bob controls an Inspector; Alice casts an artifact. The rider is
        // scoped to the controller's battlefield ("spells YOU cast"), so Alice
        // gets no discount.
        var bobInspector = FoundryInspectorFactory.Create(_bob);
        PutOnBattlefield(_bob, bobInspector);

        var aliceArtifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(aliceArtifact, _alice);

        effective.Generic.Should().Be(3,
            "Bob's Inspector doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent");
    }
}
