using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// CR 120.3 — the per-permanent "was dealt damage this turn" tracker
/// (<see cref="Permanent.WasDealtDamageThisTurn"/>), stamped at every damage
/// seam and cleared at cleanup (CR 514.2). Mirrors the player-side tracker
/// (<see cref="Player.WasDealtDamageThisTurn"/>) proven in
/// <c>BloodthirstReplacementTests</c>. Unblocks Needle Drop.
/// </summary>
[Trait("Rules", "120.3")]
public class PermanentWasDealtDamageThisTurnTests
{
    private static Creature Bear(Player owner, int power = 2, int toughness = 2)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Creature_TakeDamage_StampsFlag()
    {
        var alice = new Player("Alice", 20);
        var bear = Bear(alice);

        bear.WasDealtDamageThisTurn.Should().BeFalse("no damage yet");

        bear.TakeDamage(1);

        bear.WasDealtDamageThisTurn.Should().BeTrue("TakeDamage records CR 120.3 damage");
        bear.Damage.Should().Be(1);
    }

    [Fact]
    public void Creature_ZeroDamage_DoesNotStampFlag()
    {
        var alice = new Player("Alice", 20);
        var bear = Bear(alice);

        bear.TakeDamage(0);

        bear.WasDealtDamageThisTurn.Should().BeFalse("0 damage is not damage (CR 120.3)");
    }

    [Fact]
    public void Creature_DamageHealed_FlagPersists()
    {
        // CR 120.3 — "was dealt damage this turn" is distinct from currently-
        // marked damage: removing the marked damage does NOT un-set the flag.
        var alice = new Player("Alice", 20);
        var bear = Bear(alice);

        bear.TakeDamage(2);
        bear.RemoveDamage(2);

        bear.Damage.Should().Be(0, "marked damage removed");
        bear.WasDealtDamageThisTurn.Should().BeTrue("it was still dealt damage this turn");
    }

    [Fact]
    public void Fx_DealDamageAny_ToCreature_StampsFlag()
    {
        var alice = new Player("Alice", 20);
        var bear = Bear(alice);

        Fx.DealDamageAny(bear, 1);

        bear.WasDealtDamageThisTurn.Should().BeTrue();
    }

    [Fact]
    public void Fx_DealDamageAny_ToPlaneswalker_StampsFlag()
    {
        // CR 120.3 / CR 306.7 — a planeswalker dealt damage (loyalty removal)
        // "was dealt damage this turn"; the flag is stamped at the damage seam,
        // not in RemoveLoyalty (which is shared with loyalty-ability costs).
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Test Walker", "{4}", 5);
        pw.SetOwner(alice);
        pw.SetController(alice);
        pw.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(pw);

        Fx.DealDamageAny(pw, 2);

        pw.Loyalty.Should().Be(3, "2 loyalty removed");
        pw.WasDealtDamageThisTurn.Should().BeTrue();
    }

    [Fact]
    public void Planeswalker_LoyaltyAbilityCost_DoesNotStampFlag()
    {
        // RemoveLoyalty is also a loyalty-ability cost (NOT damage) — paying it
        // must NOT set the damage flag (CR 606.5 vs CR 120.3).
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Test Walker", "{4}", 5);

        pw.RemoveLoyalty(1); // simulate a -1 loyalty ability cost

        pw.Loyalty.Should().Be(4);
        pw.WasDealtDamageThisTurn.Should().BeFalse("paying a loyalty cost is not damage");
    }

    [Fact]
    public void Wither_CreatureDamageAsCounters_StillStampsFlag()
    {
        // CR 702.90b — wither redirects the FORM to -1/-1 counters, but the
        // creature was still dealt damage this turn (CR 120.3). Goes through
        // Fx.Fight (noncombat) so the wither counter branch is exercised.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var wither = new Creature("Wither Source", "{R}", 2, 2);
        wither.AddAbility(new Majik.Core.Abilities.KeywordAbility("Wither"));
        wither.SetOwner(alice);
        wither.SetController(alice);
        wither.SetZone(ZoneType.Battlefield);

        var victim = Bear(bob, 0, 4);

        Fx.Fight(wither, victim);

        CombatAbilities.HasWither(wither).Should().BeTrue();
        victim.Counters.Count(CounterType.MinusOneMinusOne).Should().BeGreaterThan(0,
            "wither dealt damage as -1/-1 counters");
        victim.Damage.Should().Be(0, "no marked damage — wither redirected the form");
        victim.WasDealtDamageThisTurn.Should().BeTrue(
            "the creature was still dealt damage this turn (CR 120.3)");
    }

    [Fact]
    public void ClearWasDealtDamageThisTurn_ResetsFlag()
    {
        var alice = new Player("Alice", 20);
        var bear = Bear(alice);

        bear.TakeDamage(1);
        bear.WasDealtDamageThisTurn.Should().BeTrue();

        bear.ClearWasDealtDamageThisTurn();
        bear.WasDealtDamageThisTurn.Should().BeFalse("CR 514.2 cleanup clears the flag");
    }
}
