using FluentAssertions;
using Majik.Core.CardData;
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
/// Tests for Vindicate (Apocalypse, {W}{B}{B}, Sorcery).
///
/// Oracle text: "Destroy target permanent."
///
/// Covers:
///   - Card identity (Sorcery, {W}{B}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "target permanent" request,
///     no modes, no variable X, BotIntent.Removal.
///   - Resolve: destroys a creature (CR 701.7).
///   - Resolve: destroys a planeswalker.
///   - Resolve: destroys a noncreature permanent (artifact).
///   - Resolve: destroys a land (Vindicate notably hits lands, unlike
///     Hero's Downfall / Anguished Unmaking).
///   - Resolve: off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class VindicateTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Vindicate_IsSorcery_AtCostWBB()
    {
        var card = VindicateFactory.Create(_alice);

        card.Name.Should().Be("Vindicate");
        card.ManaCost.Should().Be("{W}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Vindicate()
    {
        var card = NamedCardFactory.Create("Vindicate", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Vindicate");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{W}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Vindicate_Definition_HasSinglePermanentTarget()
    {
        var def = VindicateFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("target permanent");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys any permanent type
    // -----------------------------------------------------------------------

    [Fact]
    public void Vindicate_DestroysCreature()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Vindicate destroys the targeted permanent (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void Vindicate_DestroysPlaneswalker()
    {
        var pw = new Planeswalker(
            name: "Liliana, the Last Hope",
            manaCost: "{1}{B}{B}",
            startingLoyalty: 3,
            subtypes: new[] { CardSubtype.Liliana })
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        Resolve(pw);

        pw.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
    }

    [Fact]
    public void Vindicate_DestroysArtifact()
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
            "Vindicate destroys any permanent type, including artifacts");
        _bob.Zones.Graveyard.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void Vindicate_DestroysLand()
    {
        // Vindicate's "any permanent" wording famously includes Lands —
        // this is what separates it from Hero's Downfall / Anguished
        // Unmaking / Beast Within.
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Graveyard,
            "Vindicate destroys any permanent — including lands (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(land);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets
    // -----------------------------------------------------------------------

    [Fact]
    public void Vindicate_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged — CR 608.2b illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = VindicateFactory.BuildDefinition(targetResolver: t => t);
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
