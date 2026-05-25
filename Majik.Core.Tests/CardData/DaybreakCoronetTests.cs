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
/// Unit tests for <see cref="DaybreakCoronetFactory"/>.
///
/// Card: Daybreak Coronet — Enchantment — Aura {W}{W} (Future Sight).
///   "Enchant creature with another Aura attached to it"
///   "Enchanted creature gets +3/+3 and has first strike, vigilance,
///    and lifelink."
///
/// Covers:
///   - Identity / dispatch.
///   - Aura subtype.
///   - +3/+3 boost via AttachedBoostEffect (Layer 7c).
///   - Granted keywords: First Strike, Vigilance, Lifelink.
///   - Target predicate: only creatures with an existing aura attached
///     are legal candidates.
///   - Predicate rejects creatures with no auras, equipment-only
///     attachments, non-creatures.
/// </summary>
public class DaybreakCoronetTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DaybreakCoronet_Identity()
    {
        var c = DaybreakCoronetFactory.Create(_alice);

        c.Name.Should().Be("Daybreak Coronet");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DaybreakCoronet()
    {
        var card = NamedCardFactory.Create("Daybreak Coronet", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Daybreak Coronet");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static boost — +3/+3 + first strike + vigilance + lifelink
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_PlusThreePlusThree_AppliesToAttachedCreature()
    {
        var effects = new ContinuousEffectsService();
        var coronet = DaybreakCoronetFactory.Create(_alice, effects);
        PlaceOnBattlefield(coronet, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        coronet.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(5, "2 + 3 = 5");
        chars.Toughness.Should().Be(5, "2 + 3 = 5");
    }

    [Fact]
    public void Static_GrantsFirstStrikeVigilanceLifelink()
    {
        var effects = new ContinuousEffectsService();
        var coronet = DaybreakCoronetFactory.Create(_alice, effects);
        PlaceOnBattlefield(coronet, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        coronet.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("First Strike");
        chars.Keywords.Should().Contain("Vigilance");
        chars.Keywords.Should().Contain("Lifelink");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var coronet = DaybreakCoronetFactory.Create(_alice, effects);
        PlaceOnBattlefield(coronet, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // Don't attach.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Lifelink");
    }

    // -----------------------------------------------------------------------
    // Target predicate — "creature with another Aura attached"
    // -----------------------------------------------------------------------

    [Fact]
    public void HasAnotherAuraAttached_RejectsCreatureWithNoAttachments()
    {
        var bear = NewCreature("Bear");
        DaybreakCoronetFactory.HasAnotherAuraAttached(bear).Should().BeFalse();
    }

    [Fact]
    public void HasAnotherAuraAttached_AcceptsCreatureWithAura()
    {
        var bear = NewCreature("Bear");
        var aura = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });
        aura.AttachTo(bear);

        DaybreakCoronetFactory.HasAnotherAuraAttached(bear).Should().BeTrue();
    }

    [Fact]
    public void HasAnotherAuraAttached_RejectsCreatureWithEquipmentOnly()
    {
        var bear = NewCreature("Bear");
        var hammer = new Artifact("Hammer", "{1}",
            subtypes: new[] { CardSubtype.Equipment });
        hammer.AttachTo(bear);

        DaybreakCoronetFactory.HasAnotherAuraAttached(bear).Should().BeFalse(
            "equipment is not an aura");
    }

    [Fact]
    public void HasAnotherAuraAttached_RejectsLand()
    {
        var land = new Land("Plains");
        var aura = new Enchantment("Spreading Seas", "{1}{U}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });
        aura.AttachTo(land);

        DaybreakCoronetFactory.HasAnotherAuraAttached(land).Should().BeFalse(
            "the printed clause is 'creature' — non-creatures fail");
    }

    [Fact]
    public void BuildSpellDefinition_FiltersOnlyEligibleTargets()
    {
        var coronet = DaybreakCoronetFactory.Create(_alice);

        var enchantedBear = NewCreature("Enchanted Bear");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });
        pacifism.AttachTo(enchantedBear);

        var plainBear = NewCreature("Plain Bear");
        var land = new Land("Plains");

        var battlefield = new Permanent[] { enchantedBear, plainBear, land };
        var def = DaybreakCoronetFactory.BuildSpellDefinition(coronet, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(enchantedBear);
        candidates.Should().NotContain(plainBear);
        candidates.Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreature(string name)
    {
        return new Creature(name, "{1}{G}", 2, 2);
    }

    private static void PlaceOnBattlefield(Enchantment coronet, Player owner)
    {
        coronet.SetOwner(owner);
        coronet.SetController(owner);
        owner.Zones.Battlefield.AddCard(coronet);
        coronet.SetZone(ZoneType.Battlefield);
    }
}
