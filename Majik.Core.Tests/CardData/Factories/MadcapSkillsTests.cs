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
/// Unit tests for <see cref="MadcapSkillsFactory"/>.
///
/// Card: Madcap Skills — Enchantment — Aura {1}{R} (Shadowmoor).
///   "Enchant creature
///    Enchanted creature gets +3/+0 and has menace."
///
/// Covers:
///   - Identity / dispatch / Aura subtype (loaded from JSON).
///   - Static +3/+0 + Menace grant while attached (CR 613 Layer 7c / 6,
///     CR 702.111).
///   - Inert while unattached.
/// </summary>
[Trait("Color", "R")]
public class MadcapSkillsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MadcapSkills_Identity()
    {
        var c = MadcapSkillsFactory.Create(_alice);

        c.Name.Should().Be("Madcap Skills");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void Attached_GetsPlusThreePlusZero_AndMenace()
    {
        var effects = new ContinuousEffectsService();
        var aura = MadcapSkillsFactory.Create(_alice, continuousEffects: effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreature("Bear", _alice, 2, 2);
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(5, "2 + 3 = 5 while enchanted");
        chars.Toughness.Should().Be(2, "+0 toughness");
        chars.Keywords.Should().Contain("Menace");
    }

    [Fact]
    public void Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = MadcapSkillsFactory.Create(_alice, continuousEffects: effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreature("Bear", _alice, 2, 2);
        // Don't attach.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Menace");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreature(string name, Player owner, int power, int toughness)
    {
        var c = new Creature(name, "{1}{G}", power, toughness)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        aura.SetOwner(owner);
        aura.SetController(owner);
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
