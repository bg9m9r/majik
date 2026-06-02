using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the generalized enters-as-a-copy mechanism (CR 706.2 / 706.9 /
/// 707.2) closing Phyrexian Metamorph, Vesuva, and Spark Double.
///
/// The shared <see cref="EntersAsCopyReplacement"/> generalized path registers
/// a <see cref="CopyCharacteristicsEffect"/> (so the copy source may be a
/// non-creature artifact, a land, or a planeswalker) plus optional riders:
/// "is an Artifact in addition" (Layer 4), "not legendary" (CR 706.2 strip),
/// and a conditional entry counter (CR 706.9b).
/// </summary>
public class EntersAsCopyGeneralizedTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land AddLandToBattlefield(Player controller, string name,
        IEnumerable<CardSubtype> subtypes)
    {
        var land = new Land(name, supertypes: null, subtypes: subtypes);
        land.SetOwner(controller);
        land.SetController(controller);
        controller.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    private static Artifact AddArtifactToBattlefield(Player controller, string name)
    {
        var artifact = new Artifact(name, "{2}");
        artifact.SetOwner(controller);
        artifact.SetController(controller);
        controller.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);
        return artifact;
    }

    // -----------------------------------------------------------------------
    // Phyrexian Metamorph — copy ANY artifact OR creature; is an Artifact too.
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexianMetamorph_Identity_ArtifactCreature_PhyrexianShapeshifter()
    {
        var mm = PhyrexianMetamorphFactory.Create(_alice);

        mm.Name.Should().Be("Phyrexian Metamorph");
        mm.HasType(CardType.Artifact).Should().BeTrue();
        mm.HasType(CardType.Creature).Should().BeTrue();
        mm.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        mm.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        mm.BasePower.Should().Be(0, "printed 0/0 per CR 706.9 — copy overwrites P/T at ETB");
        mm.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void PhyrexianMetamorph_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Phyrexian Metamorph", _alice);
        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void PhyrexianMetamorph_EntersAsCopyOfNoncreatureArtifact_BecomesThatArtifact_StillArtifact()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // A noncreature artifact the OPPONENT controls — but the metamorph's
        // controller can see the whole battlefield in the v1 PickSource. Put
        // it on the controller's battlefield (AnyBattlefield pool, v1 sees the
        // controller's view).
        var golem = AddArtifactToBattlefield(_alice, "Ornithopter");

        var mm = PhyrexianMetamorphFactory.Create(_alice, replacements: bus, effects: effects);
        mm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mm);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(mm, ZoneType.Hand, ZoneType.Battlefield, _alice);

        var chars = effects.Compute(mm);
        // CR 707.2 — copied the artifact's characteristics (its name surfaces
        // via the copy effect; type line is now the source's).
        chars.Types.Should().Contain(CardType.Artifact,
            "Ornithopter is an Artifact and CR 706.9c re-adds Artifact regardless");
        // CR 706.9c — "it's an artifact in addition to its other types".
        mm.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void PhyrexianMetamorph_EntersAsCopyOfNonArtifactCreature_IsArtifactInAddition()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // A plain (non-artifact) creature.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var mm = PhyrexianMetamorphFactory.Create(_alice, replacements: bus, effects: effects);
        mm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mm);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(mm, ZoneType.Hand, ZoneType.Battlefield, _alice);

        var chars = effects.Compute(mm);
        chars.Types.Should().Contain(CardType.Creature, "copied the Bear (a creature)");
        chars.Types.Should().Contain(CardType.Artifact,
            "CR 706.9c — it's an artifact in addition to its other types");
        mm.Power.Should().Be(2, "copied the Bear's printed power");
        mm.Toughness.Should().Be(2);
    }

    [Fact]
    public void PhyrexianMetamorph_NoCopyCandidate_EntersAsPrintedArtifactCreature()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        var mm = PhyrexianMetamorphFactory.Create(_alice, replacements: bus, effects: effects);
        mm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mm);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(mm, ZoneType.Hand, ZoneType.Battlefield, _alice);

        mm.HasType(CardType.Artifact).Should().BeTrue();
        mm.HasType(CardType.Creature).Should().BeTrue();
        mm.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Vesuva — copy any LAND, enter tapped, not legendary.
    // -----------------------------------------------------------------------

    [Fact]
    public void Vesuva_Identity_Land()
    {
        var v = VesuvaFactory.Create(_alice);
        v.Name.Should().Be("Vesuva");
        v.HasType(CardType.Land).Should().BeTrue();
        v.Should().BeOfType<Land>();
    }

    [Fact]
    public void Vesuva_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Vesuva", _alice);
        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Vesuva");
    }

    [Fact]
    public void Vesuva_EntersAsCopyOfDualLand_ProducesThatLandsMana()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // A "dual" land with Island + Mountain basic subtypes → CR 305.6 grants
        // intrinsic {U} and {R} mana abilities.
        var dual = AddLandToBattlefield(_alice, "Volcanic Island",
            new[] { CardSubtype.Island, CardSubtype.Mountain });

        var vesuva = VesuvaFactory.Create(_alice, replacements: bus, effects: effects);
        vesuva.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vesuva);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(vesuva, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CR 707.2 — copied the dual land's subtypes.
        var chars = effects.Compute(vesuva);
        chars.Subtypes.Should().Contain(CardSubtype.Island);
        chars.Subtypes.Should().Contain(CardSubtype.Mountain);

        // CR 305.6 — the copied basic subtypes synthesize blue + red mana.
        var mana = EffectiveManaAbilities.For(vesuva, effects, _alice);
        var produced = mana.SelectMany(a => new[] { a.ManaGenerated }).ToList();
        produced.Sum(m => m.Blue).Should().BeGreaterThan(0, "copied Island → {U}");
        produced.Sum(m => m.Red).Should().BeGreaterThan(0, "copied Mountain → {R}");
    }

    [Fact]
    public void Vesuva_EntersTapped()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        AddLandToBattlefield(_alice, "Forest", new[] { CardSubtype.Forest });

        var vesuva = VesuvaFactory.Create(_alice, replacements: bus, effects: effects);
        vesuva.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vesuva);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(vesuva, ZoneType.Hand, ZoneType.Battlefield, _alice);

        vesuva.IsTapped.Should().BeTrue("Vesuva enters tapped as a copy");
    }

    [Fact]
    public void Vesuva_CopyOfLegendaryLand_IsNotLegendary()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        var legendary = new Land("Gaea's Cradle",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        legendary.SetOwner(_alice);
        legendary.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(legendary);
        legendary.SetZone(ZoneType.Battlefield);

        var vesuva = VesuvaFactory.Create(_alice, replacements: bus, effects: effects);
        vesuva.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vesuva);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(vesuva, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CR 706.2 — "it's not legendary if that land is legendary".
        vesuva.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeFalse(
            "CR 706.2 strips Legendary from the copy");
    }

    [Fact]
    public void Vesuva_NoLandToCopy_EntersAsPlainLand()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // Only a creature available — not a legal copy source for Vesuva.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var vesuva = VesuvaFactory.Create(_alice, replacements: bus, effects: effects);
        vesuva.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vesuva);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(vesuva, ZoneType.Hand, ZoneType.Battlefield, _alice);

        vesuva.HasType(CardType.Land).Should().BeTrue();
        var chars = effects.Compute(vesuva);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature, "no land to copy → stays a plain land");
    }

    // -----------------------------------------------------------------------
    // Spark Double — copy a creature/planeswalker you control; +1/+1 if
    // creature; not legendary.
    // -----------------------------------------------------------------------

    [Fact]
    public void SparkDouble_Identity_Illusion_0_0_Blue3U()
    {
        var sd = SparkDoubleFactory.Create(_alice);
        sd.Name.Should().Be("Spark Double");
        sd.HasType(CardType.Creature).Should().BeTrue();
        sd.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        sd.ManaCost.Should().Be("{3}{U}");
        sd.BasePower.Should().Be(0);
        sd.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void SparkDouble_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Spark Double", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spark Double");
    }

    [Fact]
    public void SparkDouble_CopiesYourCreature_EntersWithExtraPlusOneCounter()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var sd = SparkDoubleFactory.Create(_alice, replacements: bus, effects: effects);
        sd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sd);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(sd, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CR 706.9b — copied a creature → enters with an extra +1/+1 counter.
        sd.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne)
            .Should().Be(1, "CR 706.9b — extra +1/+1 counter when copying a creature");
        // Base 2/2 from the copy + 1/1 from the counter (Layer 7c).
        sd.Power.Should().Be(3, "2/2 copy + 1/1 counter");
        sd.Toughness.Should().Be(3);
    }

    [Fact]
    public void SparkDouble_CopyOfLegendaryCreature_IsNotLegendary()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        var legend = new Creature("Emrakul", "{15}", 15, 15,
            supertypes: new[] { CardSupertype.Legendary });
        legend.SetOwner(_alice);
        legend.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(legend);
        legend.SetZone(ZoneType.Battlefield);

        var sd = SparkDoubleFactory.Create(_alice, replacements: bus, effects: effects);
        sd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sd);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(sd, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CR 706.2 — "it isn't legendary".
        sd.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SparkDouble_NoCreatureYouControl_EntersAsPrintedZeroZero()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // Opponent's creature is NOT "a creature you control".
        var enemyBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        enemyBear.SetOwner(_bob);
        enemyBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enemyBear);
        enemyBear.SetZone(ZoneType.Battlefield);

        var sd = SparkDoubleFactory.Create(_alice, replacements: bus, effects: effects);
        sd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sd);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(sd, ZoneType.Hand, ZoneType.Battlefield, _alice);

        sd.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne)
            .Should().Be(0, "no copy → no extra counter");
        sd.Power.Should().Be(0, "no copy source → printed 0/0");
        sd.Toughness.Should().Be(0);
    }
}
