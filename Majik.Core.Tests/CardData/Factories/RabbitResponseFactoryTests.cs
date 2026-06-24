using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RabbitResponseFactory"/>.
///
/// Rabbit Response (Bloomburrow, {2}{W}{W}, Instant): "Creatures you control
/// get +2/+1 until end of turn. If you control a Rabbit, scry 2."
///
/// Covers the card's UNIQUE behaviour — the +2/+1 team pump scoped to "you",
/// its end-of-turn expiry (CR 514.2), and the "if you control a Rabbit" scry-2
/// rider (CR 701.20 / CR 205.3m) firing only when a Rabbit is controlled —
/// plus a single identity assert for the printed {2}{W}{W} / white instant
/// shape. Dispatch + well-formedness are covered for every implemented card by
/// <see cref="Majik.Core.Tests.CardData.CardFactoryContractTests"/>.
/// </summary>
[Trait("Color", "W")]
[Collection(nameof(StaticRegistryCollection))]
public class RabbitResponseFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_White_TwoWW()
    {
        var c = RabbitResponseFactory.Create(_alice);

        c.HasType(CardType.Instant).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{W}{W}");
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Team pump — +2/+1 to YOUR creatures only, expires EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PumpsYourCreaturesPlus2Plus1_NotOpponents_ExpiresEndOfTurn()
    {
        var effects = new ContinuousEffectsService();

        var myBear  = NewCreature(_alice, "Grizzly Bears", 2, 2, effects);
        var foeBear = NewCreature(_bob,   "Runeclaw Bear", 2, 2, effects);

        RabbitResponseFactory.BuildResolveEffect(_alice).Single().Execute();

        // Caster's creature: +2/+1.
        myBear.GetPower().Should().Be(4);
        myBear.GetToughness().Should().Be(3);

        // Opponent's creature: untouched ("creatures you control" only).
        foeBear.GetPower().Should().Be(2);
        foeBear.GetToughness().Should().Be(2);

        // CR 514.2 — the rider expires at the cleanup step.
        effects.ExpireEndOfTurn();
        myBear.GetPower().Should().Be(2, "the pump expires at end of turn");
        myBear.GetToughness().Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Conditional scry rider — "If you control a Rabbit, scry 2."
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ControllingRabbit_Scry2_AgentDecisionApplied()
    {
        // Library top → [a, b, c]. Scry 2 sees [a, b]; the agent bottoms `a`
        // and keeps `b` on top → final library [b, c, a].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        // A Rabbit on the controller's battlefield gates the scry on.
        var effects = new ContinuousEffectsService();
        NewCreature(_alice, "Pawpatch Recruit", 2, 2, effects, CardSubtype.Rabbit);

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { a },
            TopOrder: new ICard[] { b }));
        AgentRegistry.Set(_alice, agent);

        RabbitResponseFactory.BuildResolveEffect(_alice).Single().Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, a },
            because: "scry 2 bottomed A and kept B on top (CR 701.20)");
    }

    [Fact]
    public void Resolve_NoRabbitControlled_NoScry_LibraryUntouched()
    {
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        // No Rabbit on the battlefield — only a plain Bear.
        var effects = new ContinuousEffectsService();
        NewCreature(_alice, "Grizzly Bears", 2, 2, effects);

        // Register an agent whose scry decision would reorder the library — it
        // must NOT be consulted because no Rabbit is controlled.
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { a, b },
            TopOrder: Array.Empty<ICard>()));
        AgentRegistry.Set(_alice, agent);

        RabbitResponseFactory.BuildResolveEffect(_alice).Single().Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c },
            because: "no Rabbit controlled → the scry rider is skipped");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card SeedLibraryCard(string name)
    {
        var card = new Card(name, "");
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    private static Creature NewCreature(
        Player owner, string name, int power, int toughness,
        ContinuousEffectsService effects, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "{1}{G}", power, toughness, supertypes: null, subtypes: subtypes)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
