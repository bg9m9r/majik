using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bake into a Pie (Throne of Eldraine, {2}{B}{B}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-14):
///   "Destroy target creature. Create a Food token."
///
/// Bake into a Pie is the destroy-creature cousin of Bedevil / Hero's
/// Downfall (Destroy resolve at instant timing, narrowed to creature) plus a
/// Food-token mint borrowed from Witch's Oven. The card-identity + dispatch
/// well-formedness is covered globally by CardFactoryContractTests; this suite
/// asserts only the card's unique resolve behaviour (destroy + Food mint) and
/// the SpellDefinition shape.
/// </summary>
[Trait("Color", "B")]
public class BakeIntoAPieTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BakeIntoAPie_Definition_HasSingleCreatureTarget()
    {
        var def = BakeIntoAPieFactory.BuildDefinition(_alice, o => o);

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
    // Resolve — destroys the creature AND mints a Food token
    // -----------------------------------------------------------------------

    [Fact]
    public void BakeIntoAPie_DestroysCreature_AndCreatesFood()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        // Destroy half (CR 701.7).
        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Bake into a Pie destroys the targeted creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);

        // Food half (CR 111.10) — one Food token for the caster (Alice).
        AliceFoodTokens().Should().HaveCount(1,
            "Bake into a Pie creates one Food token for its controller (CR 111.10)");
    }

    [Fact]
    public void BakeIntoAPie_FoodToken_HasCorrectShape()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        var food = AliceFoodTokens().Single();
        food.IsToken.Should().BeTrue();
        food.HasType(CardType.Artifact).Should().BeTrue();
        food.HasSubtype(CardSubtype.Food).Should().BeTrue();
        // {2}, {T}, Sacrifice this token: You gain 3 life.
        food.Abilities.Should().NotBeEmpty(
            "the Food token carries its gain-3-life activated ability (CR 111.10)");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal/missing destroy target still mints Food
    // -----------------------------------------------------------------------

    [Fact]
    public void BakeIntoAPie_NonCreatureTarget_DoesNotDestroy_ButStillCreatesFood()
    {
        // Pure artifact (not a creature) — illegal for the destroy half.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "Bake into a Pie only destroys creatures (CR 608.2b illegal target)");

        // The second sentence (Food mint) is independent of the destroy half.
        AliceFoodTokens().Should().HaveCount(1,
            "the Food token is created even when the destroy target is illegal");
    }

    [Fact]
    public void BakeIntoAPie_TargetNotOnBattlefield_DoesNotDestroy_ButStillCreatesFood()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged by the resolve — CR 608.2b illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
        AliceFoodTokens().Should().HaveCount(1,
            "the Food token is created even when the destroy target is gone");
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
        var def = BakeIntoAPieFactory.BuildDefinition(_alice, targetResolver: t => t);
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

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
