using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TimelyReinforcementsFactory"/>.
///
/// Oracle text ({2}{W} Sorcery, verified against Scryfall):
///   "If you have less life than an opponent, you gain 6 life. If you control
///    fewer creatures than an opponent, create three 1/1 white Soldier
///    creature tokens."
///
/// Covers:
/// - Card identity (Sorcery, {2}{W}, white, CMC 3, owner/controller).
/// - SpellDefinition shape — no modes, no X, no target requests.
/// - Life clause: gains 6 only when strictly behind an opponent (CR 119.3).
/// - Token clause: mints three 1/1 white Soldiers only when controlling
///   strictly fewer creatures than an opponent (CR 111 / 111.4).
/// - Clause independence (CR 608.2): both / either / neither may fire.
/// </summary>
[Trait("Color", "W")]
public class TimelyReinforcementsFactoryTests
{
    private static Player NewPlayer(string name, int life = 20) => new(name, life);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TimelyReinforcements_HasSorceryShape_White_AtCost2W()
    {
        var alice = NewPlayer("Alice");
        var card = TimelyReinforcementsFactory.Create(alice);

        card.Name.Should().Be("Timely Reinforcements");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TimelyReinforcements_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var alice = NewPlayer("Alice");
        var bob = NewPlayer("Bob");

        var def = TimelyReinforcementsFactory.BuildSpellDefinition(
            alice, new[] { bob });

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Life clause — CR 119.3
    // -----------------------------------------------------------------------

    [Fact]
    public void LifeClause_Fires_WhenStrictlyBehindAnOpponent()
    {
        var alice = NewPlayer("Alice", life: 10);
        var bob = NewPlayer("Bob", life: 20);

        TimelyReinforcementsFactory.HasLessLifeThanAnOpponent(alice, new[] { bob })
            .Should().BeTrue();

        TimelyReinforcementsFactory
            .BuildResolveEffect(alice, new[] { bob })
            .Single().Execute();

        alice.LifeTotal.Should().Be(16, "10 + 6 = 16 — strictly behind Bob");
    }

    [Fact]
    public void LifeClause_DoesNotFire_WhenTiedOrAhead()
    {
        var alice = NewPlayer("Alice", life: 20);
        var bobTied = NewPlayer("Bob", life: 20);
        var bobBehind = NewPlayer("Bob2", life: 5);

        TimelyReinforcementsFactory
            .HasLessLifeThanAnOpponent(alice, new[] { bobTied })
            .Should().BeFalse("tied is not 'less than'");
        TimelyReinforcementsFactory
            .HasLessLifeThanAnOpponent(alice, new[] { bobBehind })
            .Should().BeFalse("ahead is not 'less than'");

        TimelyReinforcementsFactory
            .BuildResolveEffect(alice, new[] { bobTied, bobBehind })
            .Single().Execute();

        alice.LifeTotal.Should().Be(20, "no life gained when not strictly behind");
    }

    // -----------------------------------------------------------------------
    // Token clause — CR 111 / 111.4
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenClause_CreatesThreeWhiteSoldiers_WhenBehindOnCreatures()
    {
        var alice = NewPlayer("Alice");
        var bob = NewPlayer("Bob");

        // Bob controls one creature; Alice controls none → strictly fewer.
        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobCreature.SetOwner(bob);
        bobCreature.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobCreature);

        TimelyReinforcementsFactory
            .ControlsFewerCreaturesThanAnOpponent(alice, new[] { bob })
            .Should().BeTrue();

        alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        TimelyReinforcementsFactory
            .BuildResolveEffect(alice, new[] { bob })
            .Single().Execute();

        var tokens = alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(3, "creates three Soldier tokens");
        foreach (var token in tokens)
        {
            token.Name.Should().Be("Soldier");
            token.IsToken.Should().BeTrue();
            token.BasePower.Should().Be(1);
            token.BaseToughness.Should().Be(1);
            token.HasSubtype(CardSubtype.Soldier).Should().BeTrue(
                "CR 111.4 — Soldier creature subtype");
            token.Controller.Should().BeSameAs(alice);
            CardColors.GetColors(token).Should().Contain(ManaColor.White,
                "CR 111.4 — the token is explicitly white");
        }
    }

    [Fact]
    public void TokenClause_DoesNotFire_WhenTiedOrAheadOnCreatures()
    {
        var alice = NewPlayer("Alice");
        var bob = NewPlayer("Bob");

        // Both control one creature → tied, clause is false.
        foreach (var p in new[] { alice, bob })
        {
            var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
            c.SetOwner(p);
            c.SetController(p);
            p.Zones.Battlefield.AddCard(c);
        }

        TimelyReinforcementsFactory
            .ControlsFewerCreaturesThanAnOpponent(alice, new[] { bob })
            .Should().BeFalse("tied is not 'fewer'");

        TimelyReinforcementsFactory
            .BuildResolveEffect(alice, new[] { bob })
            .Single().Execute();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(c => c.IsToken)
            .Should().Be(0, "no tokens when not strictly behind");
    }

    // -----------------------------------------------------------------------
    // Clause independence — CR 608.2
    // -----------------------------------------------------------------------

    [Fact]
    public void BothClauses_FireIndependently_WhenBehindOnBoth()
    {
        var alice = NewPlayer("Alice", life: 8);
        var bob = NewPlayer("Bob", life: 20);

        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobCreature.SetOwner(bob);
        bobCreature.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobCreature);

        TimelyReinforcementsFactory
            .BuildResolveEffect(alice, new[] { bob })
            .Single().Execute();

        alice.LifeTotal.Should().Be(14, "8 + 6 — behind on life");
        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(c => c.IsToken)
            .Should().Be(3, "behind on creatures");
    }

    [Fact]
    public void NeitherClause_Fires_WhenNoOpponents()
    {
        var alice = NewPlayer("Alice", life: 1);

        TimelyReinforcementsFactory
            .BuildResolveEffect(alice, Array.Empty<Player>())
            .Single().Execute();

        alice.LifeTotal.Should().Be(1, "no opponent to be behind — vacuously false");
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
