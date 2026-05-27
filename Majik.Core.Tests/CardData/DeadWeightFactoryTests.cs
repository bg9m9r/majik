using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DeadWeightFactory"/>.
///
/// Card: Dead Weight — Enchantment — Aura {B} (Innistrad).
///   "Enchant creature."
///   "Enchanted creature gets -2/-2."
///
/// Covers:
///   - Identity: {B} Enchantment — Aura.
///   - Named-card dispatcher routes to this factory.
///   - Static -2/-2 via AttachedBoostEffect (Layer 7c):
///       2/2 becomes 0/0.
///       3/3 becomes 1/1.
///   - Effect is inert while the aura is unattached.
///   - BuildSpellDefinition: legal candidates are creatures only.
/// </summary>
public class DeadWeightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DeadWeight_Identity()
    {
        var c = DeadWeightFactory.Create(_alice);

        c.Name.Should().Be("Dead Weight");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DeadWeight()
    {
        var card = NamedCardFactory.Create("Dead Weight", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Dead Weight");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static -2/-2 boost
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_MinusTwoMinusTwo_TwoTwoBecomeZeroZero()
    {
        var effects = new ContinuousEffectsService();
        var dw = DeadWeightFactory.Create(_alice, effects);
        PlaceOnBattlefield(dw, _alice);

        var bear = MakeBattlefieldCreature("Bear", 2, 2);

        dw.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(0, "2 + (-2) = 0");
        chars.Toughness.Should().Be(0, "2 + (-2) = 0");
    }

    [Fact]
    public void Static_MinusTwoMinusTwo_ThreeThreeBecomesOneOne()
    {
        var effects = new ContinuousEffectsService();
        var dw = DeadWeightFactory.Create(_alice, effects);
        PlaceOnBattlefield(dw, _alice);

        var bear = MakeBattlefieldCreature("Bear", 3, 3);

        dw.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(1, "3 + (-2) = 1");
        chars.Toughness.Should().Be(1, "3 + (-2) = 1");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var dw = DeadWeightFactory.Create(_alice, effects);
        PlaceOnBattlefield(dw, _alice);

        var bear = MakeBattlefieldCreature("Bear", 2, 2);
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2, "unattached — no effect");
        chars.Toughness.Should().Be(2, "unattached — no effect");
    }

    // -----------------------------------------------------------------------
    // BuildSpellDefinition — candidate filter
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OnlyCreaturesAreLegalTargets()
    {
        var dw = DeadWeightFactory.Create(_alice);

        var creature = MakeBattlefieldCreature("Bear", 2, 2);
        var land = new Land("Swamp");
        var artifact = new Artifact("Mox Jet", "{0}");

        var battlefield = new Permanent[] { creature, land, artifact };
        var def = DeadWeightFactory.BuildSpellDefinition(dw, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(creature);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(artifact);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature MakeBattlefieldCreature(string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}{G}", power, toughness);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
