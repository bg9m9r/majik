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
/// Tests for Anguished Unmaking (Shadows over Innistrad, {1}{W}{B}, Instant).
///
/// Oracle text: "Exile target nonland permanent. You lose 3 life."
///
/// Covers:
///   - Card identity (Instant, {1}{W}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "nonland permanent" target,
///     no modes, no variable X, BotIntent.Removal.
///   - Resolve: exiles a creature + caster loses 3 life.
///   - Resolve: exiles a noncreature permanent (artifact / enchantment).
///   - Resolve: land target → illegal at resolution, exile fizzles, but
///     caster still loses 3 life (printed wording is two sentences).
///   - Resolve: off-battlefield target → exile fizzles, caster still loses
///     3 life.
/// </summary>
public class AnguishedUnmakingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AnguishedUnmaking_IsInstant_AtCost1WB()
    {
        var card = AnguishedUnmakingFactory.Create(_alice);

        card.Name.Should().Be("Anguished Unmaking");
        card.ManaCost.Should().Be("{1}{W}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AnguishedUnmaking()
    {
        var card = NamedCardFactory.Create("Anguished Unmaking", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Anguished Unmaking");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void AnguishedUnmaking_Definition_HasSingleNonlandPermanentTarget()
    {
        var def = AnguishedUnmakingFactory.BuildDefinition(_alice, o => o);

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
    // Resolve — exile + life loss
    // -----------------------------------------------------------------------

    [Fact]
    public void AnguishedUnmaking_ExilesCreature_AndCasterLoses3Life()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var aliceLifeBefore = _alice.LifeTotal;

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Exile,
            "Anguished Unmaking exiles the targeted nonland permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 3,
            "caster loses 3 life on resolve (CR 119.3)");
    }

    [Fact]
    public void AnguishedUnmaking_ExilesArtifact()
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
    // Resolve — illegal targets (exile fizzles, life loss still fires)
    // -----------------------------------------------------------------------

    [Fact]
    public void AnguishedUnmaking_LandTarget_FizzlesExile_ButStillLoses3Life()
    {
        // Pure Land — illegal target (CR 608.2b nonland filter). Printed
        // wording is two consecutive sentences with no conditional gate,
        // so the caster still pays 3 life — same posture as Swift End.
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var aliceLifeBefore = _alice.LifeTotal;

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "Anguished Unmaking cannot exile lands (CR 608.2b nonland filter)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 3,
            "life loss is unconditional per printed wording (CR 119.3)");
    }

    [Fact]
    public void AnguishedUnmaking_TargetNotOnBattlefield_FizzlesExile_ButStillLoses3Life()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        var aliceLifeBefore = _alice.LifeTotal;

        Resolve(creature);

        // Exile fizzles — creature stays in graveyard.
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Exile.GetCards().Should().NotContain(creature);

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = AnguishedUnmakingFactory.BuildDefinition(_alice, targetResolver: t => t);
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
