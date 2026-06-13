using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// PROD-WIRING regression for the
/// <c>context-aware-mana-ability-payer-grove-burnwillows</c> deferral
/// (v1-deferrals #3b residual b).
///
/// Lands are NEVER routed through their <c>[CardName]</c> factory on the
/// production build — <c>DeckCardBuilder</c> binds them via the binder chain
/// (<see cref="OracleManaBinder"/>). The Grove factory's build-time
/// <c>opponentResolver</c> was therefore inert in real games: the coloured
/// modes produced {R}/{G} but the "Each opponent gains 1 life" rider never
/// fired (there was no live opponent set reachable from a mana ability, which
/// resolves immediately with no ResolutionContext — CR 605.3).
///
/// The fix threads the live player set through an ambient
/// <see cref="GamePlayersRegistry"/> (mirroring AgentRegistry /
/// EventBusRegistry) that the driver/facade populate at game start. A
/// context-aware <see cref="ManaAbility"/> payer reads opponents off that
/// registry at activation, so the binder-built coloured modes give each
/// opponent (excluding the controller, CR 102.4) 1 life (CR 119.3).
/// </summary>
public class GroveOfTheBurnwillowsProdWiringTests
{
    private const string Oracle =
        "{T}: Add {C}.\n{T}: Add {R} or {G}. Each opponent gains 1 life.";

    private static (Land grove, Player alice, Player bob) BuildViaBinder()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var grove = new Land("Grove of the Burnwillows") { Owner = alice, Controller = alice };
        var entity = new CardEntity
        {
            Name = "Grove of the Burnwillows",
            TypeLine = "Land",
            OracleText = Oracle,
        };

        OracleManaBinder.Bind(grove, entity, alice);
        return (grove, alice, bob);
    }

    private static ManaAbility FindColored(Land land, string color)
    {
        var match = ManaCost.Parse(color);
        return land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green &&
            (m.ManaGenerated.Red + m.ManaGenerated.Green) == 1 &&
            m.ManaGenerated.White == 0 && m.ManaGenerated.Blue == 0 &&
            m.ManaGenerated.Black == 0 && m.ManaGenerated.Generic == 0);
    }

    [Fact]
    public void Binder_BindsThreeManaAbilities()
    {
        var (grove, _, _) = BuildViaBinder();
        grove.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one {C} + one {R} + one {G} — same fan-out the painland cycle uses");
    }

    [Fact]
    public void Binder_ColoredActivation_GivesEachOpponentOneLife_FromAmbientPlayers()
    {
        var (grove, alice, bob) = BuildViaBinder();

        using var _ = GamePlayersRegistry.PushScope();
        GamePlayersRegistry.Set(new[] { alice, bob });

        var red = FindColored(grove, "R");
        var produced = red.Activate();

        produced.Red.Should().Be(1, "tapping still yields {R}");
        bob.LifeTotal.Should().Be(21, "the opponent gains 1 life on the prod binder path");
        alice.LifeTotal.Should().Be(20, "the controller is never its own opponent (CR 102.4)");
        grove.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Binder_GreenActivation_GivesEachOpponentOneLife()
    {
        var (grove, alice, bob) = BuildViaBinder();

        using var _ = GamePlayersRegistry.PushScope();
        GamePlayersRegistry.Set(new[] { alice, bob });

        FindColored(grove, "G").Activate();

        bob.LifeTotal.Should().Be(21);
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Binder_ColorlessActivation_NoLifeGain()
    {
        var (grove, alice, bob) = BuildViaBinder();

        using var _ = GamePlayersRegistry.PushScope();
        GamePlayersRegistry.Set(new[] { alice, bob });

        var colorless = grove.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.Generic == 1 && m.ManaGenerated.Red == 0 && m.ManaGenerated.Green == 0);
        colorless.Activate();

        bob.LifeTotal.Should().Be(20, "the {C} mode carries no opponent-lifegain rider");
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Binder_ColoredActivation_NoAmbientPlayers_IsNoOpButStillTaps()
    {
        // No game scope installed (shape-only path) → empty opponent set →
        // safe no-op, mana + tap still fire.
        var (grove, alice, _) = BuildViaBinder();

        var red = FindColored(grove, "R");
        var produced = red.Activate();

        produced.Red.Should().Be(1);
        alice.LifeTotal.Should().Be(20);
        grove.IsTapped.Should().BeTrue();
    }
}
