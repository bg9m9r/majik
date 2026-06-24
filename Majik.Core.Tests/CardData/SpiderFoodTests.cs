using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Spider Food (Bloomburrow, {2}{G}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Destroy up to one target artifact, enchantment, or creature with flying.
///    Create a Food token."
///
/// Spider Food is the green "up to one"-destroy cousin of Bake into a Pie
/// (destroy + Food mint), with the destroy made OPTIONAL (CR 115.1a) and the
/// target filter widened to "artifact, enchantment, or creature with flying".
/// Card-identity + dispatch well-formedness is covered globally by
/// CardFactoryContractTests; this suite asserts only the unique resolve
/// behaviour (filtered destroy + unconditional Food mint) and the
/// SpellDefinition shape.
/// </summary>
[Trait("Color", "G")]
public class SpiderFoodTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpiderFood_Definition_HasOptionalArtifactEnchantmentOrFlierTarget()
    {
        var def = SpiderFoodFactory.BuildDefinition(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        // "up to one" — CR 115.1a, optional.
        tr.MinTargets.Should().Be(0);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("artifact, enchantment, or creature with flying");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys each legal target class AND mints a Food token
    // -----------------------------------------------------------------------

    [Fact]
    public void SpiderFood_DestroysArtifact_AndCreatesFood()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            "Spider Food destroys the targeted artifact (CR 701.7)");
        AliceFoodTokens().Should().HaveCount(1,
            "Spider Food mints one Food token for its controller (CR 111.10)");
    }

    [Fact]
    public void SpiderFood_DestroysEnchantment_AndCreatesFood()
    {
        var enchantment = new Enchantment("Oblivion Ring", "{2}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            "Spider Food destroys the targeted enchantment (CR 701.7)");
        AliceFoodTokens().Should().HaveCount(1);
    }

    [Fact]
    public void SpiderFood_DestroysFlyingCreature_AndCreatesFood()
    {
        var flier = NewControlledCreature(_bob, "Serra Angel", "{3}{W}{W}");
        flier.AddAbility(new KeywordAbility("Flying", flier, _bob));

        Resolve(flier);

        flier.Zone.Should().Be(ZoneType.Graveyard,
            "Spider Food destroys a creature with flying (CR 702.9 / CR 701.7)");
        AliceFoodTokens().Should().HaveCount(1);
    }

    [Fact]
    public void SpiderFood_FoodToken_HasCorrectShape()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        var food = AliceFoodTokens().Single();
        food.IsToken.Should().BeTrue();
        food.HasType(CardType.Artifact).Should().BeTrue();
        food.HasSubtype(CardSubtype.Food).Should().BeTrue();
        // {2}, {T}, Sacrifice this token: You gain 3 life.
        food.Abilities.Should().NotBeEmpty(
            "the Food token carries its gain-3-life activated ability (CR 111.10)");
    }

    // -----------------------------------------------------------------------
    // Resolve — non-flying creature is illegal; destroy is a no-op, Food still mints
    // -----------------------------------------------------------------------

    [Fact]
    public void SpiderFood_NonFlyingCreature_DoesNotDestroy_ButStillCreatesFood()
    {
        // A creature WITHOUT flying is not a legal target for the destroy half.
        var ground = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(ground);

        ground.Zone.Should().Be(ZoneType.Battlefield,
            "Spider Food only destroys creatures that have flying (CR 608.2b)");
        AliceFoodTokens().Should().HaveCount(1,
            "the Food token is created even when the destroy target is illegal");
    }

    // -----------------------------------------------------------------------
    // Resolve — "up to one": zero chosen targets still mints Food (CR 115.1a)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpiderFood_NoTargetChosen_StillCreatesFood()
    {
        ResolveNoTarget();

        AliceFoodTokens().Should().HaveCount(1,
            "with zero targets chosen (CR 115.1a) the destroy is a no-op but the "
            + "Food token is still created");
    }

    [Fact]
    public void SpiderFood_TargetNotOnBattlefield_DoesNotDestroy_ButStillCreatesFood()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            "the destroy half is a no-op against an absent target (CR 608.2b)");
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
        var def = SpiderFoodFactory.BuildDefinition(_alice, targetResolver: t => t);
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

    private void ResolveNoTarget()
    {
        var def = SpiderFoodFactory.BuildDefinition(_alice, targetResolver: t => t);
        // "Up to one" — the optional target request collected zero targets.
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { System.Array.Empty<object>() },
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
