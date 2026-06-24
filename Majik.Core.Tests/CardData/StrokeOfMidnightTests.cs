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
/// Tests for Stroke of Midnight (Throne of Eldraine, {2}{W}, Instant).
///
/// Oracle text: "Destroy target nonland permanent. Its controller creates a
/// 1/1 white Human creature token."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: Instant, {2}{W} (single _Identity assert).
///   - Definition shape — single 1..1 "target nonland permanent" request.
///   - Resolve: destroys a nonland permanent (CR 701.7) AND the target's
///     controller gains a 1/1 white Human token (CR 111.4).
///   - Resolve: the token is created under the TARGET's controller, not the
///     caster (printed "its controller", CR 109.5).
///   - Resolve: off-battlefield target → destroy fizzles, but the token rider
///     still fires under the target's controller (CR 608.2b).
///
/// Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests, so this file does not re-assert them.
/// </summary>
[Trait("Color", "W")]
public class StrokeOfMidnightTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void StrokeOfMidnight_IsInstant_AtCost2W()
    {
        var card = StrokeOfMidnightFactory.Create(_alice);

        card.Name.Should().Be("Stroke of Midnight");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StrokeOfMidnight_Definition_HasSingleNonlandPermanentTarget()
    {
        var def = StrokeOfMidnightFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("nonland permanent");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void StrokeOfMidnight_DestroysCreature_AndItsControllerGetsHumanToken()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        // Destroy half (CR 701.7).
        goblin.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);

        // Token rider — created under the destroyed permanent's controller (Bob),
        // NOT the caster.
        var token = SingleNewToken(_bob);
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.HasType(CardType.Creature).Should().BeTrue();
        token.HasSubtype(CardSubtype.Human).Should().BeTrue();
        CardColors.GetColors(token).Should().Contain(ManaColor.White);
        CardColors.GetColors(token).Should().HaveCount(1);
        token.IsToken.Should().BeTrue();

        // Caster (Alice) gets no token.
        _alice.Zones.Battlefield.GetCards().OfType<Permanent>()
            .Where(c => c.IsToken).Should().BeEmpty();
    }

    [Fact]
    public void StrokeOfMidnight_TokenGoesToTargetController_NotCaster()
    {
        // Alice casts Stroke of Midnight at Bob's artifact; the token is Bob's.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard);
        SingleNewToken(_bob).IsToken.Should().BeTrue();
        _alice.Zones.Battlefield.GetCards().OfType<Permanent>()
            .Where(c => c.IsToken).Should().BeEmpty();
    }

    [Fact]
    public void StrokeOfMidnight_TargetNotOnBattlefield_DestroyFizzles_TokenStillCreated()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Target leaves the battlefield before resolution (CR 608.2b).
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Destroy half no-ops (already gone); token rider still fires under the
        // target's controller (printed wording — independent sentence).
        SingleNewToken(_bob).IsToken.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = StrokeOfMidnightFactory.BuildDefinition(targetResolver: t => t);
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

    private static Creature SingleNewToken(Player controller)
    {
        var tokens = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();
        tokens.Should().HaveCount(1, "exactly one 1/1 Human token is created");
        return tokens[0];
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
