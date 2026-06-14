using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// End-to-end coverage for the declarative
/// <c>whenever_an_opponent_sacrifices_permanent</c> trigger (CR 701.16 /
/// CR 109.5 / CR 603.3) — the opponent-scoped <b>aristocrat payoff-consumer</b>
/// over the dedicated <see cref="PermanentSacrificedEvent"/> surface. The trigger
/// fires on a sacrifice by a player OTHER than the controller (CR 102.2),
/// optionally gated on the sacrificed permanent's card type (CR 205.2) and on
/// excluding tokens (CR 111.7), stamps the sacrificing player as "that player"
/// (CR 603.3), and an untargeted <c>deal_damage_to_triggering_player</c> reads it
/// back at resolution — the declarative lift of the hand-rolled It That Betrays
/// steal trigger over the SAME event. Each test parses a throwaway JSON
/// <see cref="CardDefinition"/>, builds it through the PRODUCTION loader, raises a
/// real <see cref="PermanentSacrificedEvent"/>, then resolves the pending trigger
/// and asserts the sacrificing player lost the expected life.
/// </summary>
public class JsonWheneverAnOpponentSacrificesPermanentTriggerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // Vengeful Tracker shape — "Whenever an opponent sacrifices an artifact,
    // this creature deals 2 damage to them."
    private const string ArtifactPunisherJson = """
    {
      "name": "Test Artifact-Sac Punisher",
      "types": ["Creature"],
      "subtypes": ["Human", "Detective"],
      "manaCost": "{1}{R}",
      "power": 2,
      "toughness": 2,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_an_opponent_sacrifices_permanent", "permanentType": "Artifact" },
          "effects": [ { "type": "deal_damage_to_triggering_player", "amount": 2 } ]
        }
      ]
    }
    """;

    // No-type-gate variant — "Whenever an opponent sacrifices a permanent, …".
    private const string AnyPermanentPunisherJson = """
    {
      "name": "Test Any-Sac Punisher",
      "types": ["Creature"],
      "subtypes": ["Spirit"],
      "manaCost": "{B}",
      "power": 1,
      "toughness": 1,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_an_opponent_sacrifices_permanent" },
          "effects": [ { "type": "deal_damage_to_triggering_player", "amount": 1 } ]
        }
      ]
    }
    """;

    // Nontoken-only variant.
    private const string NontokenPunisherJson = """
    {
      "name": "Test Nontoken-Sac Punisher",
      "types": ["Creature"],
      "subtypes": ["Spirit"],
      "manaCost": "{B}",
      "power": 1,
      "toughness": 1,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_an_opponent_sacrifices_permanent", "nontokenOnly": true },
          "effects": [ { "type": "deal_damage_to_triggering_player", "amount": 1 } ]
        }
      ]
    }
    """;

    private (TriggeredAbility ability, Permanent card) BuildAndRegister(
        string json, TriggerManager triggers)
    {
        var def = CardDefinitionLoader.FromJson(json);
        var card = (Permanent)CardDefinitionFactory.Build(def, _alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        var ability = card.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(ability);
        return (ability, card);
    }

    private static Artifact SacrificedArtifact(Player owner)
    {
        var a = new Artifact("Doomed Trinket", "{1}");
        a.SetOwner(owner);
        a.SetController(owner);
        return a;
    }

    private static Creature SacrificedCreature(Player owner)
    {
        var c = new Creature("Doomed Beast", "{1}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void ResolveAll(Majik.Core.Stack.Stack stack)
    {
        while (true)
        {
            var top = stack.Pop();
            if (top == null) break;
            top.Resolve();
        }
    }

    // -------------------------------------------------------------------
    // Vengeful Tracker shape — opponent sacrifices an artifact.
    // -------------------------------------------------------------------

    [Fact]
    public void OpponentSacrificesArtifact_Deals2ToThatPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(ArtifactPunisherJson, triggers);

        bus.Publish(new PermanentSacrificedEvent(SacrificedArtifact(_bob), _bob, wasToken: false));

        triggers.PendingCount.Should().Be(1, "an opponent's artifact sacrifice fires the punisher (CR 701.16)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _bob.LifeTotal.Should().Be(18, "the untargeted verb deals 2 to the sacrificing opponent (CR 603.3 'them')");
        _bob.LifeLostThisTurn.Should().Be(2, "the loss feeds Spectacle / Revolt observers");
    }

    [Fact]
    public void ControllerSacrificesArtifact_DoesNotTrigger()
    {
        // CR 102.2 — the controller is never their own opponent.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(ArtifactPunisherJson, triggers);

        bus.Publish(new PermanentSacrificedEvent(SacrificedArtifact(_alice), _alice, wasToken: false));

        triggers.PendingCount.Should().Be(0, "the controller sacrificing is not 'an opponent sacrifices'");
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void OpponentSacrificesNonArtifact_DoesNotTrigger()
    {
        // CR 205.2 — the type gate excludes a non-artifact permanent.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(ArtifactPunisherJson, triggers);

        bus.Publish(new PermanentSacrificedEvent(SacrificedCreature(_bob), _bob, wasToken: false));

        triggers.PendingCount.Should().Be(0, "only an artifact sacrifice fires the artifact punisher");
        _bob.LifeTotal.Should().Be(20);
    }

    // -------------------------------------------------------------------
    // No-type-gate variant — any permanent type fires it.
    // -------------------------------------------------------------------

    [Fact]
    public void NoTypeGate_AnyPermanentTypeFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AnyPermanentPunisherJson, triggers);

        // A creature sacrifice fires the no-gate variant.
        bus.Publish(new PermanentSacrificedEvent(SacrificedCreature(_bob), _bob, wasToken: false));

        triggers.PendingCount.Should().Be(1, "with no type gate, any permanent sacrifice fires it");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _bob.LifeTotal.Should().Be(19);
    }

    // -------------------------------------------------------------------
    // Nontoken-only variant — CR 111.7.
    // -------------------------------------------------------------------

    [Fact]
    public void NontokenOnly_TokenSacrificeIsSkipped()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(NontokenPunisherJson, triggers);

        bus.Publish(new PermanentSacrificedEvent(SacrificedCreature(_bob), _bob, wasToken: true));

        triggers.PendingCount.Should().Be(0, "a token sacrifice is skipped by the nontoken gate (CR 111.7)");
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void NontokenOnly_NontokenSacrificeFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(NontokenPunisherJson, triggers);

        bus.Publish(new PermanentSacrificedEvent(SacrificedCreature(_bob), _bob, wasToken: false));

        triggers.PendingCount.Should().Be(1, "a nontoken sacrifice fires the nontoken punisher");
    }

    // -------------------------------------------------------------------
    // Trigger stamps the sacrificing player as "that player" (CR 603.3).
    // -------------------------------------------------------------------

    [Fact]
    public void TriggeringPlayer_StampedOnAbility_AtConditionMatch()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (ability, _) = BuildAndRegister(ArtifactPunisherJson, triggers);

        bus.Publish(new PermanentSacrificedEvent(SacrificedArtifact(_bob), _bob, wasToken: false));

        ability.TriggeringPlayer.Should().BeSameAs(_bob,
            "the trigger stamps the sacrificing opponent as 'them' before going on the stack (CR 603.3)");
    }

    [Fact]
    public void Verb_IsUntargeted_NoTargetRequest()
    {
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(new EventBus()), new EventBus());
        var (ability, _) = BuildAndRegister(ArtifactPunisherJson, triggers);

        ability.TargetRequests.Should().BeEmpty(
            "the opponent-sac payoff punishes the trigger-identified player, not a chosen target");
    }
}
