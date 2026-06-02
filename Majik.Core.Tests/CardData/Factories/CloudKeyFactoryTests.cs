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
using Enchantment = Majik.Core.Cards.Enchantment;
using Instant = Majik.Core.Cards.Instant;
using Sorcery = Majik.Core.Cards.Sorcery;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CloudKeyFactory"/>.
///
/// Card: Cloud Key — Artifact — {2} (Future Sight).
///   "As this artifact enters, choose artifact, creature, enchantment,
///    instant, or sorcery.
///    Spells you cast of the chosen type cost {1} less to cast."
///
/// Covers:
/// - Identity (name, {2}, Artifact type, owner/controller).
/// - NamedCardFactory dispatch returns an Artifact shell (no rider, no choice).
/// - As-enters type choice (CR 614.12): chosen type captured + exposed;
///   illegal type rejected.
/// - Chosen-type cost reduction rider (CR 117.7):
///     * Spell of the chosen type — generic reduced by 1.
///     * Spell of a different type — no reduction.
///     * Works for each of the five choosable types.
///     * Off-battlefield Cloud Key — no reduction.
///     * Two Cloud Keys naming the same type stack.
///     * Coloured pips untouched + floor-at-zero.
///     * Opponent's Cloud Key doesn't discount your spells ("you cast").
/// </summary>
public class CloudKeyFactoryTests
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

    private static Creature CreatureSpell(Player owner, string name, string manaCost)
    {
        var c = new Creature(name, manaCost, power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static Enchantment EnchantmentSpell(Player owner, string name, string manaCost)
    {
        var e = new Enchantment(name, manaCost);
        e.SetOwner(owner);
        e.SetController(owner);
        return e;
    }

    private static Instant InstantSpell(Player owner, string name, string manaCost)
    {
        var i = new Instant(name, manaCost);
        i.SetOwner(owner);
        i.SetController(owner);
        return i;
    }

    private static Sorcery SorcerySpell(Player owner, string name, string manaCost)
    {
        var s = new Sorcery(name, manaCost);
        s.SetOwner(owner);
        s.SetController(owner);
        return s;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void CloudKey_Identity()
    {
        var c = CloudKeyFactory.Create(_alice);

        c.Name.Should().Be("Cloud Key");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue("Cloud Key is an Artifact");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CloudKey_ParameterlessOverload_HasNoRiderAndNoChoice()
    {
        var c = CloudKeyFactory.Create(_alice);

        CloudKeyFactory.GetChosenType(c).Should().BeNull(
            "no type was chosen on the parameterless overload");
        c.Abilities.OfType<SpellCostReductionAbility>().Should().BeEmpty(
            "the cost-reduction rider is only attached once a type is chosen");
    }

    [Fact]
    public void CloudKey_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Cloud Key", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Cloud Key");
        c.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // As-enters type choice (CR 614.12)
    // -------------------------------------------------------------------------

    [Fact]
    public void ChosenType_IsCapturedAndExposed()
    {
        var c = CloudKeyFactory.Create(_alice, _ => CardType.Instant);

        CloudKeyFactory.GetChosenType(c).Should().Be(CardType.Instant);
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1,
            "the chosen-type cost-reduction rider is attached once a type is chosen");
    }

    [Fact]
    public void IllegalChosenType_Throws()
    {
        // Land is not one of the five named types — rejected (CR 614.12).
        var act = () => CloudKeyFactory.Create(_alice, _ => CardType.Land);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(CardType.Artifact)]
    [InlineData(CardType.Creature)]
    [InlineData(CardType.Enchantment)]
    [InlineData(CardType.Instant)]
    [InlineData(CardType.Sorcery)]
    public void EachChoosableType_IsAccepted(CardType type)
    {
        var c = CloudKeyFactory.Create(_alice, _ => type);

        CloudKeyFactory.GetChosenType(c).Should().Be(type);
    }

    // -------------------------------------------------------------------------
    // Cost-reduction rider (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void SpellOfChosenType_GenericReducedByOne()
    {
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Instant);
        PutOnBattlefield(_alice, key);

        var instant = InstantSpell(_alice, "Test Bolt", "{3}");

        var effective = CostReduction.GetEffectiveCost(instant, _alice);

        effective.Generic.Should().Be(2, "{3} generic reduced by 1 → {2} (instant chosen)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void SpellOfDifferentType_NoReduction()
    {
        // Cloud Key names instant; an artifact spell is untouched.
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Instant);
        PutOnBattlefield(_alice, key);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(3, "artifact spell — Cloud Key named instant, no discount");
    }

    [Fact]
    public void ArtifactChosen_DiscountsArtifactSpell()
    {
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Artifact);
        PutOnBattlefield(_alice, key);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{4}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(3, "{4} artifact reduced by 1 → {3} (artifact chosen)");
    }

    [Fact]
    public void CreatureChosen_DiscountsCreatureSpell()
    {
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Creature);
        PutOnBattlefield(_alice, key);

        var creature = CreatureSpell(_alice, "Test Beast", "{2}{G}");

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1} (creature chosen)");
        effective.Green.Should().Be(1, "coloured pip untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void EnchantmentChosen_DiscountsEnchantmentSpell()
    {
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Enchantment);
        PutOnBattlefield(_alice, key);

        var enchantment = EnchantmentSpell(_alice, "Test Aura", "{3}");

        var effective = CostReduction.GetEffectiveCost(enchantment, _alice);

        effective.Generic.Should().Be(2, "{3} enchantment reduced by 1 → {2}");
    }

    [Fact]
    public void SorceryChosen_DiscountsSorcerySpell()
    {
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Sorcery);
        PutOnBattlefield(_alice, key);

        var sorcery = SorcerySpell(_alice, "Test Divination", "{2}{U}");

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1} (sorcery chosen)");
        effective.Blue.Should().Be(1, "coloured pip untouched (CR 117.7c)");
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Instant);
        _alice.Zones.Hand.AddCard(key);
        key.SetZone(ZoneType.Hand);

        var instant = InstantSpell(_alice, "Test Bolt", "{3}");

        var effective = CostReduction.GetEffectiveCost(instant, _alice);

        effective.Generic.Should().Be(3, "Cloud Key isn't on the battlefield — no discount");
    }

    [Fact]
    public void TwoCloudKeys_SameType_ReductionStacks()
    {
        var k1 = CloudKeyFactory.Create(_alice, _ => CardType.Sorcery);
        var k2 = CloudKeyFactory.Create(_alice, _ => CardType.Sorcery);
        PutOnBattlefield(_alice, k1);
        PutOnBattlefield(_alice, k2);

        var sorcery = SorcerySpell(_alice, "Test Divination", "{3}");

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(1, "two Cloud Keys naming sorcery reduce {3} → {1}");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void TwoCloudKeys_DifferentTypes_EachDiscountsOnlyItsType()
    {
        var artKey = CloudKeyFactory.Create(_alice, _ => CardType.Artifact);
        var instKey = CloudKeyFactory.Create(_alice, _ => CardType.Instant);
        PutOnBattlefield(_alice, artKey);
        PutOnBattlefield(_alice, instKey);

        var artifact = ArtifactSpell(_alice, "Test Artifact", "{3}");
        var instant = InstantSpell(_alice, "Test Bolt", "{3}");

        CostReduction.GetEffectiveCost(artifact, _alice).Generic.Should().Be(2,
            "only the artifact-naming Cloud Key discounts the artifact spell");
        CostReduction.GetEffectiveCost(instant, _alice).Generic.Should().Be(2,
            "only the instant-naming Cloud Key discounts the instant spell");
    }

    [Fact]
    public void CheapSpell_FloorsAtZero()
    {
        var key = CloudKeyFactory.Create(_alice, _ => CardType.Artifact);
        PutOnBattlefield(_alice, key);

        var artifact = ArtifactSpell(_alice, "Cheap Artifact", "{1}");

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);

        effective.Generic.Should().Be(0, "{1} reduced by 1 → {0}; floor-at-zero (CR 117.7c)");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void OpponentControlsCloudKey_DoesNotDiscountYourSpells()
    {
        // Bob controls a Cloud Key naming instant; Alice casts an instant. The
        // rider is scoped to the controller's battlefield ("spells YOU cast"),
        // so Alice gets no discount.
        var bobKey = CloudKeyFactory.Create(_bob, _ => CardType.Instant);
        PutOnBattlefield(_bob, bobKey);

        var aliceInstant = InstantSpell(_alice, "Test Bolt", "{3}");

        var effective = CostReduction.GetEffectiveCost(aliceInstant, _alice);

        effective.Generic.Should().Be(3,
            "Bob's Cloud Key doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent");
    }
}
