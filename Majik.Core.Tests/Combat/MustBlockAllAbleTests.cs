using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// CR 509.1c / 509.1g — "All creatures able to block ~ do so" block
/// requirement (Lure / Breaker of Armies / Nemesis Mask family). Enforced by
/// the must-block overload of
/// <see cref="CombatValidator.IsValidBlockDeclaration(System.Collections.Generic.IEnumerable{System.ValueTuple{Creature, Attacker}}, Player, System.Collections.Generic.IEnumerable{Attacker}, System.Collections.Generic.IEnumerable{Creature})"/>.
/// </summary>
public class MustBlockAllAbleTests
{
    private readonly CombatValidator _validator = new();

    private static Creature MustBlockAttacker(Player controller, Player defender, out Attacker attacker)
    {
        var c = new Creature("Breaker", "8", 10, 8) { Controller = controller };
        c.SetZone(ZoneType.Battlefield);
        c.AddAbility(new KeywordAbility("MustBeBlockedByAllAble", source: c, controller: controller));
        attacker = new Attacker(c, defender);
        return c;
    }

    private static Creature Blocker(Player defender, bool tapped = false)
    {
        var b = new Creature("Bear", "1G", 2, 2) { Controller = defender };
        b.SetZone(ZoneType.Battlefield);
        if (tapped) b.Tap();
        return b;
    }

    [Fact]
    public void AbleBlockerNotAssigned_IsInvalid()
    {
        var atk = new Player("Alice", 20);
        var def = new Player("Bob", 20);
        MustBlockAttacker(atk, def, out var attacker);
        var bear = Blocker(def);

        // Declaration assigns NO blocker even though the bear is able to block.
        var blocks = new List<(Creature, Attacker)>();

        var result = _validator.IsValidBlockDeclaration(
            blocks, def, new[] { attacker }, new[] { bear });

        result.Should().BeFalse("an able creature must block a must-block attacker (CR 509.1c)");
    }

    [Fact]
    public void AbleBlockerAssigned_IsValid()
    {
        var atk = new Player("Alice", 20);
        var def = new Player("Bob", 20);
        MustBlockAttacker(atk, def, out var attacker);
        var bear = Blocker(def);

        var blocks = new List<(Creature, Attacker)> { (bear, attacker) };

        var result = _validator.IsValidBlockDeclaration(
            blocks, def, new[] { attacker }, new[] { bear });

        result.Should().BeTrue("the able blocker satisfies the requirement");
    }

    [Fact]
    public void TappedCreatureNotAble_IsExempt()
    {
        var atk = new Player("Alice", 20);
        var def = new Player("Bob", 20);
        MustBlockAttacker(atk, def, out var attacker);
        var tappedBear = Blocker(def, tapped: true);

        // Tapped creature can't block (CR 509.1c "able to block") — exempt.
        var blocks = new List<(Creature, Attacker)>();

        var result = _validator.IsValidBlockDeclaration(
            blocks, def, new[] { attacker }, new[] { tappedBear });

        result.Should().BeTrue("a tapped creature is not able to block and is exempt");
    }

    [Fact]
    public void OneAbleBlocksAnotherDoesNot_IsInvalid()
    {
        var atk = new Player("Alice", 20);
        var def = new Player("Bob", 20);
        MustBlockAttacker(atk, def, out var attacker);
        var bear1 = Blocker(def);
        var bear2 = Blocker(def);

        // Only one of two able blockers is assigned; both must block.
        var blocks = new List<(Creature, Attacker)> { (bear1, attacker) };

        var result = _validator.IsValidBlockDeclaration(
            blocks, def, new[] { attacker }, new[] { bear1, bear2 });

        result.Should().BeFalse("every able creature must block the must-block attacker");
    }

    [Fact]
    public void AllAbleBlock_IsValid()
    {
        var atk = new Player("Alice", 20);
        var def = new Player("Bob", 20);
        MustBlockAttacker(atk, def, out var attacker);
        var bear1 = Blocker(def);
        var bear2 = Blocker(def);

        var blocks = new List<(Creature, Attacker)> { (bear1, attacker), (bear2, attacker) };

        var result = _validator.IsValidBlockDeclaration(
            blocks, def, new[] { attacker }, new[] { bear1, bear2 });

        result.Should().BeTrue();
    }

    [Fact]
    public void NoMustBlockAttacker_FallsBackToLegalityOnly()
    {
        var atk = new Player("Alice", 20);
        var def = new Player("Bob", 20);
        var plain = new Creature("Vanilla", "2", 3, 3) { Controller = atk };
        plain.SetZone(ZoneType.Battlefield);
        var attacker = new Attacker(plain, def);
        var bear = Blocker(def);

        // No must-block attacker; not assigning the able bear is legal.
        var blocks = new List<(Creature, Attacker)>();

        var result = _validator.IsValidBlockDeclaration(
            blocks, def, new[] { attacker }, new[] { bear });

        result.Should().BeTrue();
    }

    [Fact]
    public void TwoMustBlockAttackers_EachAbleBlocksOne_IsValid()
    {
        var atk = new Player("Alice", 20);
        var def = new Player("Bob", 20);
        MustBlockAttacker(atk, def, out var attacker1);
        MustBlockAttacker(atk, def, out var attacker2);
        var bear1 = Blocker(def);
        var bear2 = Blocker(def);

        // CR 509.1g — a creature satisfies the requirement by blocking ANY
        // one must-block attacker it is able to block.
        var blocks = new List<(Creature, Attacker)> { (bear1, attacker1), (bear2, attacker2) };

        var result = _validator.IsValidBlockDeclaration(
            blocks, def, new[] { attacker1, attacker2 }, new[] { bear1, bear2 });

        result.Should().BeTrue();
    }

    [Fact]
    public void BreakerOfArmies_Factory_CarriesMustBlockMarker()
    {
        var owner = new Player("Alice", 20);
        var breaker = Majik.Core.CardData.Factories.BreakerOfArmiesFactory.Create(owner);

        breaker.Power.Should().Be(10);
        breaker.Toughness.Should().Be(8);
        breaker.HasSubtype(Majik.Core.Cards.Types.CardSubtype.Eldrazi).Should().BeTrue();
        CombatAbilities.MustBeBlockedByAllAble(breaker).Should().BeTrue();
    }
}
