using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GiselaBladeOfGoldnightFactory"/>.
///
/// Card: Gisela, Blade of Goldnight — Legendary Creature — Angel
/// {4}{R}{W}{W} 5/5 (Avacyn Restored).
///   "Flying, first strike, lifelink.
///    If a source would deal damage to an opponent or a permanent an
///    opponent controls, that source deals double that damage to that
///    player or permanent instead.
///    If a source would deal damage to you or a permanent you control,
///    prevent half that damage, rounded up."
///
/// Covers:
///   - Identity (name, type, supertypes/subtypes, mana cost,
///     power/toughness, owner/controller).
///   - NamedCardFactory dispatcher entry.
///   - Keyword markers (Flying, First Strike, Lifelink).
///   - Single-arg shape path: no bus interaction.
///   - Doubling clause: damage to opponent / opponent's creature /
///     opponent's planeswalker doubles.
///   - Halving clause: damage to controller / controller's creature /
///     controller's planeswalker halves (rounded up).
///   - Rounding: 1→1, 3→2, 5→3, 7→4.
///   - Battlefield gate: both clauses suspend off-battlefield.
///   - Multi-source: Furnace of Rath + Gisela together quadruple
///     opponent damage and net 2x for controller (Furnace 2x then
///     Gisela halve rounded up).
/// </summary>
public class GiselaBladeOfGoldnightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------------
    // Identity + dispatch + keywords
    // ---------------------------------------------------------------------

    [Fact]
    public void Gisela_Identity()
    {
        var c = GiselaBladeOfGoldnightFactory.Create(_alice);

        c.Name.Should().Be("Gisela, Blade of Goldnight");
        c.ManaCost.Should().Be("{4}{R}{W}{W}");
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(5);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Gisela_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Gisela, Blade of Goldnight", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Gisela, Blade of Goldnight");
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
    }

    [Fact]
    public void Gisela_HasFlyingFirstStrikeLifelinkKeywords()
    {
        var c = GiselaBladeOfGoldnightFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("First Strike");
        keywords.Should().Contain("Lifelink");
    }

    [Fact]
    public void SingleArgPath_DoesNotRegisterReplacements()
    {
        var bus = new ReplacementBus();
        _ = GiselaBladeOfGoldnightFactory.Create(_alice);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(src, 4, TargetPlayer: _alice))!
            .Amount.Should().Be(4,
                "single-arg dispatcher path never registers on the bus");
    }

    // ---------------------------------------------------------------------
    // Doubling clause — damage to opponent / opponent's permanent
    // ---------------------------------------------------------------------

    [Fact]
    public void DoublesDamage_ToOpponent()
    {
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(6);
    }

    [Fact]
    public void DoublesDamage_ToOpponentsCreature()
    {
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _alice, Controller = _alice };
        var bobsCreature = new Creature("victim", "{G}", 2, 2)
            { Owner = _bob, Controller = _bob };

        bus.Apply(new DamageIntent(src, 2, TargetCreature: bobsCreature) { IsCombatDamage = true })!
            .Amount.Should().Be(4);
    }

    [Fact]
    public void DoublesDamage_ToOpponentsPlaneswalker()
    {
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _alice, Controller = _alice };
        var bobsWalker = new Planeswalker("Liliana", "{2}{B}{B}", 4)
            { Owner = _bob, Controller = _bob };

        bus.Apply(new DamageIntent(src, 3, TargetPlaneswalker: bobsWalker))!
            .Amount.Should().Be(6);
    }

    // ---------------------------------------------------------------------
    // Halving clause — damage to controller / controller's permanent
    // ---------------------------------------------------------------------

    [Fact]
    public void HalvesDamage_ToController_RoundedUp()
    {
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var bobsCreature = new Creature("src", "{R}", 5, 5)
            { Owner = _bob, Controller = _bob };

        // 5 damage → ceil(5/2) = 3.
        bus.Apply(new DamageIntent(bobsCreature, 5, TargetPlayer: _alice) { IsCombatDamage = true })!
            .Amount.Should().Be(3);
    }

    [Fact]
    public void HalvesDamage_ToControllersCreature()
    {
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var bobsCreature = new Creature("attacker", "{R}", 4, 4)
            { Owner = _bob, Controller = _bob };
        var alicesCreature = new Creature("blocker", "{W}", 3, 3)
            { Owner = _alice, Controller = _alice };

        // 4 damage → ceil(4/2) = 2.
        bus.Apply(new DamageIntent(bobsCreature, 4, TargetCreature: alicesCreature) { IsCombatDamage = true })!
            .Amount.Should().Be(2);
    }

    [Fact]
    public void HalvesDamage_ToControllersPlaneswalker()
    {
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var bobsCreature = new Creature("src", "{R}", 3, 3)
            { Owner = _bob, Controller = _bob };
        var alicesWalker = new Planeswalker("Elspeth", "{2}{W}{W}", 4)
            { Owner = _alice, Controller = _alice };

        // 3 damage → ceil(3/2) = 2.
        bus.Apply(new DamageIntent(bobsCreature, 3, TargetPlaneswalker: alicesWalker))!
            .Amount.Should().Be(2);
    }

    [Theory]
    [InlineData(1, 1)]   // ceil(1/2) = 1
    [InlineData(2, 1)]   // ceil(2/2) = 1
    [InlineData(3, 2)]   // ceil(3/2) = 2
    [InlineData(4, 2)]   // ceil(4/2) = 2
    [InlineData(5, 3)]   // ceil(5/2) = 3
    [InlineData(7, 4)]   // ceil(7/2) = 4
    [InlineData(10, 5)]  // ceil(10/2) = 5
    public void HalvingClause_RoundsUp(int incoming, int expected)
    {
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(src, incoming, TargetPlayer: _alice))!
            .Amount.Should().Be(expected);
    }

    // ---------------------------------------------------------------------
    // Battlefield gate
    // ---------------------------------------------------------------------

    [Fact]
    public void DoesNotApply_WhenNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        _ = GiselaBladeOfGoldnightFactory.Create(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2)
            { Owner = _bob, Controller = _bob };

        // Off-battlefield: neither doubling nor halving fires.
        bus.Apply(new DamageIntent(src, 4, TargetPlayer: _alice))!.Amount.Should().Be(4);
        bus.Apply(new DamageIntent(src, 4, TargetPlayer: _bob))!.Amount.Should().Be(4);
    }

    // ---------------------------------------------------------------------
    // Negative: doubling clause ignores own-side targets
    // ---------------------------------------------------------------------

    [Fact]
    public void DoublingDoesNotApply_ToControllerSidedTargets()
    {
        // Sanity: when target is Alice's creature, only halving should
        // fire — doubling's gate fails. Reuses the halving assertion.
        var bus = new ReplacementBus();
        _ = GiselaOnBattlefield(_alice, bus);

        var bobsCreature = new Creature("attacker", "{R}", 4, 4)
            { Owner = _bob, Controller = _bob };
        var alicesCreature = new Creature("blocker", "{W}", 3, 3)
            { Owner = _alice, Controller = _alice };

        bus.Apply(new DamageIntent(bobsCreature, 4, TargetCreature: alicesCreature) { IsCombatDamage = true })!
            .Amount.Should().Be(2,
                "halving applies once (4 -> 2); doubling clause's gate fails for own-side targets");
    }

    // ---------------------------------------------------------------------
    // Multi-source interactions
    // ---------------------------------------------------------------------

    [Fact]
    public void StacksWithFurnace_ToOpponent_Quadruples()
    {
        // Furnace doubles symmetrically (3 -> 6), Gisela's doubling
        // then fires (6 -> 12). Halving clause's gate fails (target is
        // opponent).
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);
        _ = GiselaOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(12);
    }

    [Fact]
    public void StacksWithFurnace_ToController_NetsApproximatelyOriginal()
    {
        // Furnace doubles (4 -> 8), Gisela's halving rounds up (8 -> 4).
        // Doubling clause's gate fails (target = controller).
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);
        _ = GiselaOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 4, 4) { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(src, 4, TargetPlayer: _alice) { IsCombatDamage = true })!
            .Amount.Should().Be(4,
                "Furnace doubles then Gisela halves rounded up — net 1x on controller-side");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static Creature GiselaOnBattlefield(Player owner, ReplacementBus bus)
    {
        var c = GiselaBladeOfGoldnightFactory.Create(owner, bus);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Enchantment FurnaceOnBattlefield(Player owner, ReplacementBus bus)
    {
        var c = FurnaceOfRathFactory.Create(owner, bus);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
