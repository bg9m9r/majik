using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TinkersToteFactory"/>.
///
/// Tinker's Tote (Bloomburrow, {2}{W}). Artifact. Oracle text:
///   "When this artifact enters, create two 1/1 colorless Gnome artifact
///    creature tokens.
///    {W}, Sacrifice this artifact: You gain 3 life."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity ({2}{W}, Artifact, white, one triggered + one activated ability).
/// - ETB trigger: entering the battlefield creates TWO 1/1 colourless Gnome
///   artifact-creature tokens (CR 603.6a / CR 111).
/// - "{W}, Sacrifice this artifact: You gain 3 life." — the activated ability
///   sacrifices the Tote (no {T}) and the controller gains 3 life (CR 602.1).
/// </summary>
[Trait("Color", "W")]
public class TinkersToteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TinkersTote_Identity()
    {
        var c = TinkersToteFactory.Create(_alice);

        c.Name.Should().Be("Tinker's Tote");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        // The {W} pip in the printed cost makes the artifact white (CR 202.2).
        CardColors.GetColors(c).Should().BeEquivalentTo(new[] { ManaColor.White });
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // One triggered ability ("When this enters") + one activated ability
        // ({W}, Sac: gain 3 life).
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EntersBattlefield_CreatesTwoGnomeArtifactCreatureTokens()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var tote = TinkersToteFactory.Create(_alice, zones, triggers);
        tote.SetOwner(_alice);
        tote.SetController(_alice);
        _alice.Zones.Hand.AddCard(tote);
        tote.SetZone(ZoneType.Hand);

        // ETB move publishes the CardMovedEvent the auto-registered enter
        // trigger fires on (CR 603.6a).
        zones.MoveCardTo(tote, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1, "the enter trigger should be queued");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        var gnomes = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(g => g.IsToken && g.HasSubtype(CardSubtype.Gnome))
            .ToList();
        gnomes.Should().HaveCount(2, "create TWO 1/1 colorless Gnome tokens");
        foreach (var g in gnomes)
        {
            g.BasePower.Should().Be(1);
            g.BaseToughness.Should().Be(1);
            // CR 111.1 — Gnome tokens are artifact creatures.
            g.HasType(CardType.Artifact).Should().BeTrue();
            g.HasType(CardType.Creature).Should().BeTrue();
            // CR 111.4 — colourless.
            CardColors.GetColors(g).Should().BeEmpty();
        }
    }

    [Fact]
    public void SacrificeForLife_GainsThreeLifeAndSacrificesSelf()
    {
        var (zones, stack, triggers, bus) = BuildEngine();

        var tote = TinkersToteFactory.Create(_alice, zones, triggers);
        tote.SetOwner(_alice);
        tote.SetController(_alice);
        // Place directly on the battlefield WITHOUT routing through ZoneService
        // so the ETB trigger does not pre-fire (isolates the sac ability).
        tote.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tote);

        var ability = tote.Abilities.OfType<ActivatedAbility>().Single();

        // No {T} cost on this ability (unlike Carrot Cake) — costs are a single
        // {W} mana pip + the self-sacrifice.
        ability.Costs.OfType<SacrificeSelfCost>().Should().HaveCount(1);

        var sacCost = ability.Costs.OfType<SacrificeSelfCost>().Single();
        var lifeBefore = _alice.LifeTotal;

        // Pay the Sacrifice cost via the bus-aware path (CR 701.16).
        sacCost.Pay(_alice, bus);

        // Resolve the gain-3-life effect body (self-contained).
        foreach (var fx in ability.Effects)
        {
            fx.Execute();
        }

        _alice.LifeTotal.Should().Be(lifeBefore + 3, "{W}, Sacrifice: You gain 3 life.");
        _alice.Zones.Graveyard.GetCards().Should().Contain(tote);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(tote);
    }

    private static void ResolveAll(Majik.Core.Stack.Stack stack)
    {
        while (stack.Count > 0)
        {
            stack.Pop()!.Resolve();
        }
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, IEventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, bus);
    }
}
