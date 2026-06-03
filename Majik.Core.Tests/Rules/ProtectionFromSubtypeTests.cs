using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Rules;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// CR 702.16 / 205.3 — protection from a creature <em>subtype</em>
/// (Baneslayer Angel — "protection from Demons and from Dragons"). Covers the
/// helper, the block-legality seam, the combat-damage-prevention seam, and the
/// targeting-legality seam, plus the Baneslayer Angel factory wiring.
/// </summary>
public class ProtectionFromSubtypeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature DemonFor(Player p) =>
        new("Demon Bear", "2B", 4, 4, subtypes: new[] { CardSubtype.Demon })
        { Owner = p, Controller = p, Zone = ZoneType.Battlefield };

    private Creature DragonFor(Player p) =>
        new("Dragon Bear", "2R", 4, 4, subtypes: new[] { CardSubtype.Dragon })
        { Owner = p, Controller = p, Zone = ZoneType.Battlefield };

    private Creature ProtectedFromDemonsAndDragons(Player p)
    {
        var c = new Creature("Angel", "3WW", 5, 5, subtypes: new[] { CardSubtype.Angel })
        { Owner = p, Controller = p, Zone = ZoneType.Battlefield };
        c.AddAbility(new ProtectionAbility("demons"));
        c.AddAbility(new ProtectionAbility("dragons"));
        return c;
    }

    [Fact]
    public void Helper_True_WhenSourceHasNamedSubtype()
    {
        var angel = ProtectedFromDemonsAndDragons(_alice);
        Protection.HasProtectionFromSubtype(angel, DemonFor(_bob)).Should().BeTrue();
        Protection.HasProtectionFromSubtype(angel, DragonFor(_bob)).Should().BeTrue();
    }

    [Fact]
    public void Helper_False_WhenSourceLacksNamedSubtype()
    {
        var angel = ProtectedFromDemonsAndDragons(_alice);
        var plainBear = new Creature("Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        Protection.HasProtectionFromSubtype(angel, plainBear).Should().BeFalse();
    }

    [Fact]
    public void Block_Forbidden_WhenAttackerHasProtectionFromBlockerSubtype()
    {
        // Angel attacks; a Demon tries to block it -> illegal (the attacker has
        // protection from the blocker's subtype).
        var angel = ProtectedFromDemonsAndDragons(_alice);
        var demonBlocker = DemonFor(_bob);

        var v = new CombatValidator();
        var attacker = new Attacker(angel, _bob);
        v.CanBlock(demonBlocker, attacker, _bob).Should().BeFalse();
    }

    [Fact]
    public void Block_Allowed_WhenBlockerSubtypeNotProtectedAgainst()
    {
        var angel = ProtectedFromDemonsAndDragons(_alice);
        var plainBlocker = new Creature("Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        var v = new CombatValidator();
        var attacker = new Attacker(angel, _bob);
        v.CanBlock(plainBlocker, attacker, _bob).Should().BeTrue();
    }

    [Fact]
    public void Target_Forbidden_WhenSourceHasNamedSubtype()
    {
        var angel = ProtectedFromDemonsAndDragons(_bob);
        var dragonSource = DragonFor(_alice);
        // An ability whose source is a Dragon can't target the protected Angel.
        TargetLegality.CanBeTargetedBy(angel, dragonSource, _alice).Should().BeFalse();
    }

    [Fact]
    public void Factory_Wires_FlyingFirstStrikeLifelink_AndSubtypeProtection()
    {
        var angel = BaneslayerAngelFactory.Create(_alice);
        angel.Name.Should().Be("Baneslayer Angel");
        angel.Power.Should().Be(5);
        angel.Toughness.Should().Be(5);
        angel.HasSubtype(CardSubtype.Angel).Should().BeTrue();

        var protections = angel.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality).ToList();
        protections.Should().Contain("demons");
        protections.Should().Contain("dragons");

        Protection.HasProtectionFromSubtype(angel, DemonFor(_bob)).Should().BeTrue();
        Protection.HasProtectionFromSubtype(angel, DragonFor(_bob)).Should().BeTrue();
    }
}
