using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// End-to-end coverage for the declarative
/// <c>deal_damage_to_triggering_player</c> effect verb paired with the
/// <c>whenever_a_player_casts_spell</c> trigger (CR 119 / CR 603.3) — the
/// declarative lift of the hand-rolled Ash Zealot / Eidolon of the Great Revel
/// boxed-closure idiom. The trigger fires on a
/// <see cref="SpellCastEvent"/> for ANY player, stamps the spell's caster as the
/// "that player" (CR 603.3), and the untargeted resolve effect deals N damage to
/// THAT player off <see cref="ResolutionContext.TriggeringPlayer"/> — no chosen
/// target slot. Each test parses a throwaway JSON
/// <see cref="CardDefinition"/>, builds it through the PRODUCTION loader, raises
/// the real <see cref="SpellCastEvent"/>, then resolves the pending trigger and
/// asserts the caster lost life (and that the loss feeds
/// <see cref="Player.LifeLostThisTurn"/>).
/// </summary>
public class JsonDealDamageToTriggeringPlayerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // Ash Zealot shape — "Whenever a player casts a spell from a graveyard,
    // this creature deals 3 damage to that player."
    private const string GraveyardPunisherJson = """
    {
      "name": "Test Graveyard Punisher",
      "types": ["Creature"],
      "subtypes": ["Human", "Warrior"],
      "manaCost": "{R}{R}",
      "power": 2,
      "toughness": 2,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_a_player_casts_spell", "fromGraveyardOnly": true },
          "effects": [ { "type": "deal_damage_to_triggering_player", "amount": 3 } ]
        }
      ]
    }
    """;

    // Eidolon shape — "Whenever a player casts a spell with mana value 3 or
    // less, this creature deals 2 damage to that player."
    private const string CheapSpellPunisherJson = """
    {
      "name": "Test Cheap-Spell Punisher",
      "types": ["Creature"],
      "subtypes": ["Spirit"],
      "manaCost": "{R}{R}",
      "power": 2,
      "toughness": 2,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_a_player_casts_spell", "maxManaValue": 3 },
          "effects": [ { "type": "deal_damage_to_triggering_player", "amount": 2 } ]
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

    private static SpellCastEvent GraveyardCast(Player caster, string manaCost = "{R}")
    {
        var c = new Instant("Flashbacked Bolt", manaCost) { Owner = caster };
        var spell = new Majik.Core.Spells.Spell(c, caster) { WasCastFromGraveyard = true };
        return new SpellCastEvent(spell);
    }

    private static SpellCastEvent HandCast(Player caster, string manaCost)
    {
        var c = new Instant("Hand Spell", manaCost) { Owner = caster };
        return new SpellCastEvent(new Majik.Core.Spells.Spell(c, caster));
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
    // Ash Zealot shape — graveyard-cast punisher.
    // -------------------------------------------------------------------

    [Fact]
    public void OpponentCastsFromGraveyard_Deals3ToThatPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(GraveyardPunisherJson, triggers);

        bus.Publish(GraveyardCast(_bob));

        triggers.PendingCount.Should().Be(1, "a graveyard cast fires the punisher (CR 603.1)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _bob.LifeTotal.Should().Be(17, "the untargeted verb deals 3 to the caster (CR 603.3 'that player')");
        _bob.LifeLostThisTurn.Should().Be(3, "the loss feeds Spectacle / Revolt observers");
    }

    [Fact]
    public void ControllerCastsFromGraveyard_DamagesController()
    {
        // CR 700.6 — "a player" is unrestricted; the trigger's own controller
        // casting from THEIR graveyard still bounces the damage onto themselves.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(GraveyardPunisherJson, triggers);

        bus.Publish(GraveyardCast(_alice));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _alice.LifeTotal.Should().Be(17, "the controller's own graveyard cast still triggers");
    }

    [Fact]
    public void SpellCastFromHand_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(GraveyardPunisherJson, triggers);

        bus.Publish(HandCast(_bob, "{R}"));

        triggers.PendingCount.Should().Be(0, "only graveyard casts trigger the from-graveyard punisher");
        _bob.LifeTotal.Should().Be(20);
    }

    // -------------------------------------------------------------------
    // Eidolon shape — cheap-spell (mana-value cap) punisher.
    // -------------------------------------------------------------------

    [Fact]
    public void CheapSpellCast_Deals2ToThatPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(CheapSpellPunisherJson, triggers);

        // MV 1 <= 3 — fires.
        bus.Publish(HandCast(_bob, "{R}"));

        triggers.PendingCount.Should().Be(1, "an MV-3-or-less spell fires the cheap-spell punisher");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _bob.LifeTotal.Should().Be(18, "the untargeted verb deals 2 to the caster");
    }

    [Fact]
    public void ExpensiveSpellCast_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(CheapSpellPunisherJson, triggers);

        // MV 4 > 3 — the cap excludes it (CR 202.3).
        bus.Publish(HandCast(_bob, "{3}{R}"));

        triggers.PendingCount.Should().Be(0, "a spell above the mana-value cap does not trigger");
        _bob.LifeTotal.Should().Be(20);
    }

    // -------------------------------------------------------------------
    // Verb shape: untargeted (declares NO TargetRequest).
    // -------------------------------------------------------------------

    [Fact]
    public void Verb_IsUntargeted_NoTargetRequest()
    {
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(new EventBus()), new EventBus());
        var (ability, _) = BuildAndRegister(GraveyardPunisherJson, triggers);

        ability.TargetRequests.Should().BeEmpty(
            "deal_damage_to_triggering_player punishes the trigger-identified player, not a chosen target");
    }

    [Fact]
    public void TriggeringPlayer_StampedOnAbility_AtConditionMatch()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (ability, _) = BuildAndRegister(GraveyardPunisherJson, triggers);

        bus.Publish(GraveyardCast(_bob));

        ability.TriggeringPlayer.Should().BeSameAs(_bob,
            "the trigger condition stamps the caster as 'that player' before going on the stack (CR 603.3)");
    }
}
