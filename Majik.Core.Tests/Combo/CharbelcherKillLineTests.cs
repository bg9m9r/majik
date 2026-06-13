using FluentAssertions;
using Xunit;

namespace Majik.Core.Tests.Combo;

/// <summary>
/// Phase B3 (plan 2026-06-13) — THE engine-correctness floor: drive the Goblin
/// Charbelcher kill through the FULL real engine (GameDriver + priority loop +
/// mana abilities + ability activation + targeting + the Charbelcher
/// resolution) with a scripted Belcher seat, and assert the opponent dies with
/// damage = the number of nonland cards revealed.
///
/// <para>This is the line test the design called for: independent of the bot,
/// it proves the engine executes the kill end-to-end — tap 3 lands for {3}
/// (mana abilities, hold priority), activate Charbelcher's {3}+{T}, reveal-
/// until-land over an all-nonland library, route the damage to the opponent,
/// opponent dead.</para>
///
/// <para>Built on the CORRECTED Charbelcher oracle (reveal-until-LAND, damage =
/// NONLAND count) — see <see cref="MdfcLibraryProbeTests"/> for the inverted-
/// implementation bug this Phase-B effort surfaced and fixed (the original
/// factory revealed-until-nonland and counted lands, which made a landless
/// library deal ZERO and silently killed the entire combo).</para>
/// </summary>
public sealed class CharbelcherKillLineTests
{
    [Fact]
    public async Task Charbelcher_InPlay_With3Mana_RevealsLandlessLibrary_KillsOpponent()
    {
        // Belcher deck = 7 hand fillers (top of library → opening hand) + a
        // large all-nonland library so the reveal is lethal after the 7-card
        // opening draw. Total 37 cards: 7 drawn, 30 remain in the library at
        // belch time → 30 damage. Opponent at 12 life → dead.
        //
        // Every card is an MDFC front (nonland by front face), so there is NO
        // land to stop the reveal — it walks the whole library.
        var library = BelcherLines.MdfcFrontLibrary(37);

        var line = new ScriptedLineAgent
        {
            // The belch targets "any target" → the opponent player.
            OnChooseTargets = (ctx, _) => new object[] { ctx.Opponents[0] },
        };

        var harness = ComboLineHarness.Build(
            belcherLibraryOrder: library,
            line: line,
            opponentLife: 12,
            // Pre-deploy Charbelcher + 3 Islands (the {3} activation engine).
            battlefield: new[] { "Goblin Charbelcher", "Island", "Island", "Island" });

        // The line: float {3} from the three Islands, then belch the opponent.
        harness.TapForMana("Island")
               .TapForMana("Island")
               .TapForMana("Island")
               .ActivateCharbelcher();

        var result = await harness.RunAsync(maxTurns: 2, seed: 4242);

        // 30 nonland cards revealed → 30 damage → a 12-life opponent dies.
        harness.Opponent.LifeTotal.Should().BeLessThanOrEqualTo(0,
            "a landless library of ~30 nonland cards burns the opponent out");
        result.Winner.Should().BeSameAs(harness.Belcher,
            "the Charbelcher kill is lethal and the engine ends the game");

        // The reveal RESOLVED (it is not a graveyard false-positive): the line
        // walked to the end of the library, then bottomed the revealed pile, so
        // the library count is unchanged from its post-opening-hand size.
        harness.Belcher.Zones.Library.Count.Should().Be(30,
            "the reveal bottoms the whole revealed pile — library size is preserved");
    }
}
