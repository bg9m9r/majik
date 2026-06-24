using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Savor (Bloomburrow, {1}{B}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Target creature gets -2/-2 until end of turn. Create a Food token."
///
/// Savor is the -2/-2 cousin of Disfigure (PumpUntilEndOfTurnEffect(-2,-2))
/// bolted to a Food-token mint borrowed from Bake into a Pie. The
/// card-identity + dispatch well-formedness is covered globally by
/// CardFactoryContractTests; this suite asserts only the card's unique resolve
/// behaviour (-2/-2 + Food mint) and the SpellDefinition shape.
/// </summary>
[Trait("Color", "B")]
public class SavorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Savor_Definition_HasSingleCreatureTarget()
    {
        var def = SavorFactory.BuildDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — applies -2/-2 AND mints a Food token
    // -----------------------------------------------------------------------

    [Fact]
    public void Savor_AppliesMinus2Minus2_AndCreatesFood()
    {
        // Wire the target with a ContinuousEffectsService so the
        // PumpUntilEndOfTurnEffect can register (mirrors Disfigure test).
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(bear);

        // CR 613 Layer 7c — -2/-2 takes Bears (2/2) to 0/0.
        bear.Power.Should().Be(0, "Grizzly Bears 2/2 with -2/-2 → 0/0");
        bear.Toughness.Should().Be(0);

        // Food half (CR 111.10) — one Food token for the caster (Alice).
        AliceFoodTokens().Should().HaveCount(1,
            "Savor creates one Food token for its controller (CR 111.10)");
    }

    [Fact]
    public void Savor_FoodToken_HasCorrectShape()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(bear);

        var food = AliceFoodTokens().Single();
        food.IsToken.Should().BeTrue();
        food.HasType(CardType.Artifact).Should().BeTrue();
        food.HasSubtype(CardSubtype.Food).Should().BeTrue();
        // {2}, {T}, Sacrifice this token: You gain 3 life.
        food.Abilities.Should().NotBeEmpty(
            "the Food token carries its gain-3-life activated ability (CR 111.10)");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal/missing pump target still mints Food
    // -----------------------------------------------------------------------

    [Fact]
    public void Savor_TargetNotOnBattlefield_DoesNotPump_ButStillCreatesFood()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(bear);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        Resolve(bear);

        // CR 608.2b — illegal target → -2/-2 is a no-op, stats unchanged.
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);

        // The second sentence (Food mint) is independent of the pump half.
        AliceFoodTokens().Should().HaveCount(1,
            "the Food token is created even when the -2/-2 target is gone");
    }

    [Fact]
    public void Savor_NoActiveEffectsService_DoesNotThrow_AndStillCreatesFood()
    {
        // Shape-only path: target on battlefield but no
        // ContinuousEffectsService wired. The pump half must silently no-op
        // (Disfigure-style guard); the Food mint still fires.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var act = () => Resolve(bear);
        act.Should().NotThrow();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        AliceFoodTokens().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private System.Collections.Generic.List<Artifact> AliceFoodTokens() =>
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Food))
            .ToList();

    private void Resolve(object targetToken)
    {
        var def = SavorFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(
        Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
