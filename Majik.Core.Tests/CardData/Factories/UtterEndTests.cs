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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Utter End (Commander 2014, {2}{W}{B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Exile target nonland permanent."
///
/// Covers:
///   - Card identity (Instant, {2}{W}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "nonland permanent" target,
///     no modes, no variable X, BotIntent.Removal.
///   - Resolve: exiles a creature (CR 701.21). No life loss.
///   - Resolve: exiles a noncreature permanent (artifact).
///   - Resolve: land target → illegal at resolution, exile fizzles (CR 305 /
///     608.2b nonland filter). Unlike Despark there is no mv gate — lands are
///     simply never legal targets.
///   - Resolve: off-battlefield target → exile fizzles.
/// </summary>
public class UtterEndTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void UtterEnd_IsInstant_AtCost2WB()
    {
        var card = UtterEndFactory.Create(_alice);

        card.Name.Should().Be("Utter End");
        card.ManaCost.Should().Be("{2}{W}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_UtterEnd()
    {
        var card = NamedCardFactory.Create("Utter End", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Utter End");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{W}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void UtterEnd_Definition_HasSingleNonlandPermanentTarget()
    {
        var def = UtterEndFactory.BuildDefinition(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("nonland permanent");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile (no life loss)
    // -----------------------------------------------------------------------

    [Fact]
    public void UtterEnd_ExilesCreature_NoLifeLoss()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var aliceLifeBefore = _alice.LifeTotal;

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Exile,
            "Utter End exiles the targeted nonland permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);

        _alice.LifeTotal.Should().Be(aliceLifeBefore,
            "Utter End has no life-loss clause");
    }

    [Fact]
    public void UtterEnd_ExilesArtifact()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(artifact);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(artifact);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets (exile fizzles)
    // -----------------------------------------------------------------------

    [Fact]
    public void UtterEnd_LandTarget_FizzlesExile()
    {
        // Pure Land — illegal target (CR 305 / 608.2b nonland filter). Unlike
        // Despark there is no mana-value gate; lands are simply never legal.
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "Utter End cannot exile lands (CR 305 / 608.2b nonland filter)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
    }

    [Fact]
    public void UtterEnd_TargetNotOnBattlefield_FizzlesExile()
    {
        var goyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(goyf);

        Resolve(goyf);

        // Exile fizzles — creature stays in graveyard.
        goyf.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Exile.GetCards().Should().NotContain(goyf);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = UtterEndFactory.BuildDefinition(_alice, targetResolver: t => t);
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
