using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="EatenAliveFactory"/> — Sorcery {B} (Innistrad:
/// Midnight Hunt).
///
/// Oracle text:
///   "As an additional cost to cast this spell, sacrifice a creature or pay {3}{B}.
///    Exile target creature or planeswalker."
///
/// Eaten Alive is the EXILE sibling of Spark Harvest (same {B} cost, same
/// sacrifice-or-pay-{3}{B} additional cost, same target shape). These tests
/// cover ONLY Eaten Alive's UNIQUE behaviour — exile (CR 701.21) of a creature
/// or planeswalker, the disjunctive additional-cost shape, and a single
/// identity assert. NamedCardFactory dispatch + well-formedness are covered for
/// every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "B")]
public class EatenAliveTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity (single assert — exact mana cost for the non-vanilla body)
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeManaCost()
    {
        var card = EatenAliveFactory.Create(_alice);

        card.Name.Should().Be("Eaten Alive");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — disjunctive additional cost + target shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacOrPayManaCost_AndCreatureOrPlaneswalkerTarget()
    {
        var def = EatenAliveFactory.BuildDefinition(t => t);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeCreatureOrPayManaAdditionalCost>(
                "Eaten Alive prints 'As an additional cost, sacrifice a creature or pay {3}{B}.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — exiles creature / planeswalker (CR 701.21), NOT destroy
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ExilesTargetCreature()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Exile,
            "Eaten Alive exiles the target creature (CR 701.21) — it does NOT go to the graveyard");
        _bob.Zones.Exile.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_ExilesTargetPlaneswalker()
    {
        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        liliana.SetOwner(_bob);
        liliana.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        Resolve(liliana);

        liliana.Zone.Should().Be(ZoneType.Exile,
            "Eaten Alive exiles the target planeswalker (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(liliana);
    }

    [Fact]
    public void Resolve_TargetNotOnBattlefield_DoesNothing()
    {
        // Target already left the battlefield before resolution (CR 608.2b).
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 1, 1);
        goyf.SetOwner(_bob);
        goyf.SetController(_bob);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        Resolve(goyf);

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "Eaten Alive is a no-op when the target is no longer on the battlefield (CR 608.2b)");
        _bob.Zones.Exile.GetCards().Should().NotContain(goyf);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = EatenAliveFactory.BuildDefinition(targetResolver: t => t);
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
