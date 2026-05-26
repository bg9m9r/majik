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
/// Tests for Pongify (Time Spiral, {U}, Instant).
/// "Destroy target creature. It can't be regenerated. Its controller
///  creates a 3/3 green Ape creature token."
///
/// Covers:
///   - Card identity (Instant, {U}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve destroys the targeted creature (battlefield → graveyard).
///   - Target's controller gets a 3/3 green Ape token.
///   - Illegal target at resolution (creature left battlefield) → neither
///     destroy nor token.
/// </summary>
public class PongifyTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Pongify_Identity_InstantAtU()
    {
        var card = PongifyFactory.Create(_alice);

        card.Name.Should().Be("Pongify");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Pongify()
    {
        var card = NamedCardFactory.Create("Pongify", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Pongify");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest()
    {
        var def = PongifyFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void Pongify_DestroysTarget_AndControllerGets3_3GreenApeToken()
    {
        var bears = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bears);

        // CR 701.7 — destroyed.
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);

        // 3/3 green Ape token under Bob (the destroyed creature's controller).
        var tokens = _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();
        tokens.Should().ContainSingle();
        var ape = tokens[0];
        ape.Name.Should().Be("Ape");
        ape.Power.Should().Be(3);
        ape.Toughness.Should().Be(3);
        ape.Subtypes.Should().Contain(CardSubtype.Ape);
        ape.Controller.Should().BeSameAs(_bob);
        CardColors.GetColors(ape).Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public void Pongify_IllegalTargetAtResolution_NoOp_NoToken()
    {
        // Creature already left the battlefield before Pongify resolves
        // (CR 608.2b — illegal target → spell does nothing, no token).
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bears);

        Resolve(bears);

        // Still only the dead bear in graveyard, no token on battlefield.
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target)
    {
        var def = PongifyFactory.BuildSpellDefinition(resolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
