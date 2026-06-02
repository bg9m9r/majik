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
/// Tests for Despark (War of the Spark, {W}{B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Exile target permanent with mana value 4 or greater."
///
/// Covers:
///   - Card identity (Instant, {W}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "target permanent with mana value
///     4 or greater" target, no modes, no variable X, BotIntent.Removal.
///   - Resolve: exiles a creature with mv >= 4 (CR 701.21). No life loss.
///   - Resolve: exiles a high-mv noncreature permanent (artifact).
///   - Resolve: low-mv target (mv < 4) → illegal at resolution, exile fizzles.
///   - Resolve: off-battlefield target → exile fizzles.
///   - Despark CAN target lands (unlike Anguished Unmaking), but a basic land's
///     printed mv is 0 so it is filtered out by the mv >= 4 gate.
/// </summary>
public class DesparkTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Despark_IsInstant_AtCostWB()
    {
        var card = DesparkFactory.Create(_alice);

        card.Name.Should().Be("Despark");
        card.ManaCost.Should().Be("{W}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Despark()
    {
        var card = NamedCardFactory.Create("Despark", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Despark");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{W}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Despark_Definition_HasSingleHighManaValuePermanentTarget()
    {
        var def = DesparkFactory.BuildDefinition(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("mana value 4 or greater");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile (no life loss)
    // -----------------------------------------------------------------------

    [Fact]
    public void Despark_ExilesCreature_WithManaValue4OrGreater()
    {
        // {4}{G}{G} == mana value 6 → legal target.
        var beast = NewControlledCreature(_bob, "Pelakka Wurm", "{4}{G}{G}");
        var aliceLifeBefore = _alice.LifeTotal;

        Resolve(beast);

        beast.Zone.Should().Be(ZoneType.Exile,
            "Despark exiles the targeted permanent with mana value 4 or greater (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(beast);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(beast);

        _alice.LifeTotal.Should().Be(aliceLifeBefore,
            "Despark has no life-loss clause");
    }

    [Fact]
    public void Despark_ExilesHighManaValueArtifact()
    {
        var artifact = new Artifact("Wurmcoil Engine", "{6}")
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
    public void Despark_LowManaValueTarget_FizzlesExile()
    {
        // {R} == mana value 1 < 4 → illegal target (CR 608.2b mv gate).
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var aliceLifeBefore = _alice.LifeTotal;

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Battlefield,
            "Despark cannot exile a permanent with mana value < 4 (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(goblin);

        _alice.LifeTotal.Should().Be(aliceLifeBefore);
    }

    [Fact]
    public void Despark_BasicLand_FizzlesExile_ManaValueZero()
    {
        // Despark CAN target lands (no nonland filter), but a basic land's
        // printed mana value is 0, so the mv >= 4 gate rejects it.
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "a basic land has mana value 0 < 4 (CR 608.2b mv gate)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
    }

    [Fact]
    public void Despark_TargetNotOnBattlefield_FizzlesExile()
    {
        var titan = NewControlledCreature(_bob, "Primeval Titan", "{4}{G}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(titan);
        titan.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(titan);

        Resolve(titan);

        // Exile fizzles — creature stays in graveyard.
        titan.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Exile.GetCards().Should().NotContain(titan);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = DesparkFactory.BuildDefinition(_alice, targetResolver: t => t);
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
