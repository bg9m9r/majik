using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

public class LandDropTrackerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>Put a permanent granting <paramref name="grant"/> additional
    /// land plays onto <paramref name="player"/>'s battlefield (CR 720) — the
    /// Azusa / Dryad / Exploration static surface.</summary>
    private static Permanent AddAdditionalLandSource(Player player, int grant)
    {
        var p = new Permanent(
            $"Land Static (+{grant})", "", new[] { Majik.Core.Cards.Types.CardType.Enchantment });
        p.SetOwner(player);
        p.SetController(player);
        p.AdditionalLandPlaysGranted = grant;
        p.SetZone(ZoneType.Battlefield);
        player.Zones.Battlefield.AddCard(p);
        return p;
    }

    [Fact]
    public void DefaultMax_OnePerTurn()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _).Should().BeTrue();
        t.RecordLandPlayed(_alice);
        t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out var reason).Should().BeFalse();
        reason.Should().Contain("already played");
    }

    [Fact]
    public void OnOpponentTurn_Rejected()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _bob, StepStateType.PreCombatMain, true, out var reason).Should().BeFalse();
        reason.Should().Contain("your turn");
    }

    [Fact]
    public void OutsideMain_Rejected()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _alice, StepStateType.End, true, out var reason).Should().BeFalse();
        reason.Should().Contain("main phase");
    }

    [Fact]
    public void StackNotEmpty_Rejected()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, false, out var reason).Should().BeFalse();
        reason.Should().Contain("stack is empty");
    }

    [Fact]
    public void ExtraLandDrops_Honored()
    {
        var t = new LandDropTracker();
        t.SetMaxLandDropsThisTurn(_alice, 3);

        for (var i = 0; i < 3; i++)
        {
            t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _).Should().BeTrue();
            t.RecordLandPlayed(_alice);
        }
        t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _).Should().BeFalse();
    }

    [Fact]
    public void ResetTurn_ClearsCount_AndResetMax()
    {
        var t = new LandDropTracker();
        t.SetMaxLandDropsThisTurn(_alice, 3);
        t.RecordLandPlayed(_alice);
        t.RecordLandPlayed(_alice);

        t.ResetTurn();

        t.DropsUsedThisTurn(_alice).Should().Be(0);
        t.MaxLandDropsThisTurn(_alice).Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // CR 720 — "you may play N additional lands on each of your turns"
    // battlefield static. Summed live from the controller's permanents.
    // ---------------------------------------------------------------------

    [Fact]
    public void BattlefieldStatic_RaisesEffectiveCap()
    {
        var t = new LandDropTracker();
        AddAdditionalLandSource(_alice, 2); // Azusa-style +2

        // Base 1 + static 2 = 3.
        t.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(3);

        for (var i = 0; i < 3; i++)
        {
            t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _)
                .Should().BeTrue($"land {i + 1} of 3 is allowed");
            t.RecordLandPlayed(_alice);
        }

        // 4th land rejected.
        t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out var reason)
            .Should().BeFalse();
        reason.Should().Contain("already played");
    }

    [Fact]
    public void BattlefieldStatic_LeavesBattlefield_RemovesGrant()
    {
        var t = new LandDropTracker();
        var source = AddAdditionalLandSource(_alice, 2);

        t.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(3);

        // Source leaves the battlefield — grant lifts immediately, live.
        source.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(source);

        t.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(1);
    }

    [Fact]
    public void BattlefieldStatics_StackAdditively()
    {
        var t = new LandDropTracker();
        AddAdditionalLandSource(_alice, 2); // two Azusas = +4
        AddAdditionalLandSource(_alice, 2);

        t.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(5);
    }

    [Fact]
    public void BattlefieldStatic_OnlyCountsControllersPermanents()
    {
        var t = new LandDropTracker();
        AddAdditionalLandSource(_bob, 2); // Bob's source

        // Alice gets no benefit from Bob's permanent.
        t.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(1);
        t.EffectiveMaxLandDropsThisTurn(_bob).Should().Be(3);
    }

    [Fact]
    public void BattlefieldStatic_ResetsPerTurn_StaysWhileSourcePresent()
    {
        var t = new LandDropTracker();
        AddAdditionalLandSource(_alice, 1); // Dryad-style +1

        // Use both plays this turn.
        t.RecordLandPlayed(_alice);
        t.RecordLandPlayed(_alice);
        t.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _)
            .Should().BeFalse();

        // New turn — count resets, static persists (source still on field).
        t.ResetTurn();

        t.DropsUsedThisTurn(_alice).Should().Be(0);
        t.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(2);
    }

    [Fact]
    public void BattlefieldStatic_StacksOnOneShotBump()
    {
        var t = new LandDropTracker();
        AddAdditionalLandSource(_alice, 2);     // Azusa static +2
        t.SetMaxLandDropsThisTurn(_alice, 2);   // Explore-style one-shot +1

        // one-shot cap 2 + static 2 = 4.
        t.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(4);
    }
}
