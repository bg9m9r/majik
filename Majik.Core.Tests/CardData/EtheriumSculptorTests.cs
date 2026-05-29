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
/// Unit tests for <see cref="EtheriumSculptorFactory"/>.
///
/// Card: Etherium Sculptor — Artifact Creature — Vedalken Artificer
///   {1}{U}, 1/2 (Conflux / Shards of Alara block).
///   "Artifact spells you cast cost {1} less to cast."
///
/// Covers:
/// - Identity (name, {1}{U}, Artifact + Creature types, Vedalken + Artificer
///   subtypes, 1/2, owner/controller).
/// - NamedCardFactory dispatch returns a Creature shell with the
///   SpellCostReductionAbility rider attached.
/// - Spell-cost reduction rider (CR 117.7):
///     * Artifact spell cast — generic cost reduced by 1.
///     * Artifact creature spell cast — reduced (it's still an Artifact).
///     * Non-artifact spell cast — no reduction.
///     * Off-battlefield Sculptor — no reduction.
///     * Two Sculptors stack — reduction is additive.
///     * Coloured pips untouched + floor-at-zero.
///     * Opponent's Sculptor doesn't discount your spells ("you cast").
/// </summary>
public class EtheriumSculptorTests
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
    public void EtheriumSculptor_Identity()
    {
        var c = EtheriumSculptorFactory.Create(_alice);

        c.Name.Should().Be("Etherium Sculptor");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue("Etherium Sculptor is an Artifact Creature (CR 301.1)");
        c.HasSubtype(CardSubtype.Vedalken).Should().BeTrue("Vedalken is a printed subtype");
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue("Artificer is a printed subtype");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the artifact-spell cost-reduction rider is attached");
    }

    [Fact]
    public void EtheriumSculptor_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Etherium Sculptor", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Etherium Sculptor");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vedalken).Should().BeTrue();
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Cost-reduction rider (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void ArtifactSpellCast_GenericReducedByOne()
    {
        var sculptor = EtheriumSculptorFactory.Create(_alice);
        PutOnBattlefield(_alice, sculptor);

        // A generic-bearing artifact spell — {3} (e.g. a colourless artifact).
        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(2, "{3} generic reduced by 1 → {2}");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void ArtifactCreatureSpellCast_GenericReducedByOne()
    {
        // An artifact creature spell is still an Artifact spell — discounted.
        var sculptor = EtheriumSculptorFactory.Create(_alice);
        PutOnBattlefield(_alice, sculptor);

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
        var sculptor = EtheriumSculptorFactory.Create(_alice);
        PutOnBattlefield(_alice, sculptor);

        // A vanilla creature — {2}{G}. Not an artifact → no discount.
        var creature = new Creature("Test Beast", "{2}{G}", power: 3, toughness: 3);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(2, "non-artifact spell — no Etherium Sculptor discount");
        effective.Green.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var sculptor = EtheriumSculptorFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(sculptor);
        sculptor.SetZone(ZoneType.Hand);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(3, "Sculptor isn't on the battlefield — no discount");
    }

    [Fact]
    public void TwoSculptors_ReductionStacks()
    {
        var s1 = EtheriumSculptorFactory.Create(_alice);
        var s2 = EtheriumSculptorFactory.Create(_alice);
        PutOnBattlefield(_alice, s1);
        PutOnBattlefield(_alice, s2);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(1, "two Sculptors reduce {3} generic → {1}");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void ColourlessArtifact_FloorsAtZero()
    {
        // A {1} artifact (e.g. a 1-mana equipment). Reducer drives generic to
        // 0; floor-at-zero (CR 117.7c) — never negative.
        var sculptor = EtheriumSculptorFactory.Create(_alice);
        PutOnBattlefield(_alice, sculptor);

        var artifact = ArtifactSpell(_alice, "Cheap Artifact", "{1}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(0, "{1} reduced by 1 → {0}; floor-at-zero (CR 117.7c)");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void ColouredArtifactSpell_PipUntouched()
    {
        // An artifact spell with a coloured pip — {2}{U}. Only generic reduces.
        var sculptor = EtheriumSculptorFactory.Create(_alice);
        PutOnBattlefield(_alice, sculptor);

        var artifact = ArtifactSpell(_alice, "Blue Artifact", "{2}{U}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Blue.Should().Be(1, "coloured pip untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void OpponentControlsSculptor_DoesNotDiscountYourSpells()
    {
        // Bob controls a Sculptor; Alice casts an artifact. The rider is
        // scoped to the controller's battlefield ("spells YOU cast"), so
        // Alice gets no discount.
        var bobSculptor = EtheriumSculptorFactory.Create(_bob);
        PutOnBattlefield(_bob, bobSculptor);

        var aliceArtifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(aliceArtifact, _alice);

        effective.Generic.Should().Be(3,
            "Bob's Sculptor doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent");
    }
}
