using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Declarative SPELL-effect path (the instant/sorcery analogue of
/// <see cref="JsonTargetingEffectsTests"/>). Proves that an instant/sorcery
/// can carry the SAME ability <see cref="EffectDefinition"/> verbs
/// (<c>return_to_hand</c>, <c>deal_damage</c>, <c>destroy_target</c>) and
/// resolve them against the spell's CHOSEN target, threaded through the
/// production <see cref="SpellCastFlow"/> → <see cref="Majik.Core.Services.StackResolver"/>
/// path — no bespoke C# resolve closure required.
///
/// Each test drives the real cast flow: build a <see cref="SpellDefinition"/>
/// from an <see cref="EffectDefinition"/> list via
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>, cast with a
/// scripted target, resolve, and assert the effect hit ONLY the chosen target.
/// Illegal-target cases (CR 608.2b) fizzle cleanly.
/// </summary>
public class JsonSpellEffectsTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Majik.Core.Services.ZoneService _zones;
    private readonly Majik.Core.Services.StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public JsonSpellEffectsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new Majik.Core.Services.ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new Majik.Core.Services.StackResolver(_bus, _zones);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    private Instant CastInstant(string name, string manaCost)
    {
        var card = new Instant(name, manaCost);
        card.SetOwner(_alice);
        card.SetController(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        return card;
    }

    private async Task CastAndResolve(Instant card, SpellDefinition def, object? chosen)
    {
        var agent = new ScriptedAgent();
        if (chosen != null) agent.QueueTargets(new[] { chosen });
        else agent.QueueTargets(System.Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(_alice, card, def, agent, NewContext(), alternativeCost: null);
        _resolver.ResolveTop(_stack);
    }

    // ── return_to_hand (bounce) ──────────────────────────────────────────────

    [Fact]
    public async Task ReturnToHand_BouncesChosenCreature_ToOwnersHand()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Unsummon",
            new EffectDefinition[]
            {
                new ReturnToHandEffectDef { TargetFilter = "creature" },
            });

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        var bystander = OnBattlefield(new Creature("Other Bear", "{1}{G}", 2, 2), _bob);

        var card = CastInstant("Unsummon", "{U}");
        await CastAndResolve(card, def, bear);

        bear.Zone.Should().Be(ZoneType.Hand, "the chosen creature returns to its owner's hand (CR 701.20)");
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        bystander.Zone.Should().Be(ZoneType.Battlefield, "only the chosen target is bounced");
    }

    [Fact]
    public async Task ReturnToHand_IllegalTarget_FizzlesCleanly()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Unsummon",
            new EffectDefinition[] { new ReturnToHandEffectDef { TargetFilter = "creature" } });

        // Bear already in the graveyard — illegal at resolution (CR 608.2b).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var card = CastInstant("Unsummon", "{U}");
        await CastAndResolve(card, def, bear);

        bear.Zone.Should().Be(ZoneType.Graveyard, "illegal target at resolution → no-op (CR 608.2b)");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void ReturnToHand_DeclaresSingleTargetRequest()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Unsummon",
            new EffectDefinition[] { new ReturnToHandEffectDef { TargetFilter = "creature" } });

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── deal_damage (burn) ───────────────────────────────────────────────────

    [Fact]
    public async Task DealDamage_DealsToChosenCreature_NotABystander()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Shock",
            new EffectDefinition[] { new DealDamageEffectDef { Amount = 2, Target = "any" } });

        var victim = OnBattlefield(new Creature("Victim", "{G}", 3, 3), _bob);
        var bystander = OnBattlefield(new Creature("Bystander", "{G}", 3, 3), _bob);

        var card = CastInstant("Shock", "{R}");
        await CastAndResolve(card, def, victim);

        victim.Damage.Should().Be(2, "the chosen creature takes 2 damage");
        bystander.Damage.Should().Be(0, "damage hits only the chosen target");
    }

    [Fact]
    public async Task DealDamage_DealsToChosenPlayer()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Shock",
            new EffectDefinition[] { new DealDamageEffectDef { Amount = 2, Target = "any" } });

        var card = CastInstant("Shock", "{R}");
        await CastAndResolve(card, def, _bob);

        _bob.LifeTotal.Should().Be(18, "2 damage to a player is 2 life lost (CR 119.3)");
    }

    // ── destroy_target ───────────────────────────────────────────────────────

    [Fact]
    public async Task DestroyTarget_DestroysChosenArtifact_NotABystander()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Shatter",
            new EffectDefinition[] { new DestroyTargetEffectDef { TargetFilter = "artifact" } });

        var victim = OnBattlefield(new Artifact("Doomed Relic", "{2}"), _bob);
        var bystander = OnBattlefield(new Artifact("Safe Relic", "{2}"), _bob);

        var card = CastInstant("Shatter", "{1}{R}");
        await CastAndResolve(card, def, victim);

        victim.Zone.Should().Be(ZoneType.Graveyard, "the chosen artifact is destroyed");
        bystander.Zone.Should().Be(ZoneType.Battlefield, "only the chosen target is destroyed");
    }

    // ── exile_target (spell path) ────────────────────────────────────────────

    [Fact]
    public async Task ExileTarget_ExilesChosenCreature_NotABystander()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Excommunicate",
            new EffectDefinition[] { new ExileTargetEffectDef { TargetFilter = "creature" } });

        var victim = OnBattlefield(new Creature("Doomed Bear", "{1}{G}", 2, 2), _bob);
        var bystander = OnBattlefield(new Creature("Safe Bear", "{1}{G}", 2, 2), _bob);

        var card = CastInstant("Excommunicate", "{2}{W}");
        await CastAndResolve(card, def, victim);

        victim.Zone.Should().Be(ZoneType.Exile, "the chosen creature is exiled (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(victim);
        bystander.Zone.Should().Be(ZoneType.Battlefield, "only the chosen target is exiled");
    }

    [Fact]
    public async Task ExileTarget_IllegalTarget_FizzlesCleanly()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Excommunicate",
            new EffectDefinition[] { new ExileTargetEffectDef { TargetFilter = "creature" } });

        // Already in the graveyard — illegal at resolution (CR 608.2b).
        var bear = new Creature("Doomed Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var card = CastInstant("Excommunicate", "{2}{W}");
        await CastAndResolve(card, def, bear);

        bear.Zone.Should().Be(ZoneType.Graveyard, "illegal target at resolution → no-op (CR 608.2b)");
        _bob.Zones.Exile.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void ExileTarget_DeclaresSingleTargetRequest()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Excommunicate",
            new EffectDefinition[] { new ExileTargetEffectDef { TargetFilter = "creature" } });

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── lose_life_target (rider that shares the preceding target slot) ─────────

    [Fact]
    public async Task LoseLifeTarget_RiderSharesPreviousSlot_VaporSnagBouncesAndDrainsController()
    {
        // Vapor Snag: "Return target creature to its owner's hand. Its
        // controller loses 1 life." The lose-life rider does NOT declare its
        // own target — it shares the bounce's slot and drains the chosen
        // creature's controller (CR 119.3).
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Vapor Snag",
            new EffectDefinition[]
            {
                new ReturnToHandEffectDef { TargetFilter = "creature" },
                new LoseLifeTargetEffectDef { Amount = 1, Subject = "controller" },
            });

        // Exactly ONE target request — the rider shares the bounce's slot.
        def.TargetRequests.Should().HaveCount(1);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);

        var card = CastInstant("Vapor Snag", "{U}");
        await CastAndResolve(card, def, bear);

        bear.Zone.Should().Be(ZoneType.Hand, "the chosen creature returns to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.LifeTotal.Should().Be(19, "its controller loses 1 life (CR 119.3)");
        _alice.LifeTotal.Should().Be(20, "the caster is unaffected");
    }

    [Fact]
    public async Task LoseLifeTarget_IllegalTarget_NeitherBounceNorLifeLoss()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Vapor Snag",
            new EffectDefinition[]
            {
                new ReturnToHandEffectDef { TargetFilter = "creature" },
                new LoseLifeTargetEffectDef { Amount = 1, Subject = "controller" },
            });

        // Already in the graveyard — illegal at resolution (CR 608.2b). The
        // rider keys off "its controller", undefined off the battlefield, so
        // neither clause happens.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var card = CastInstant("Vapor Snag", "{U}");
        await CastAndResolve(card, def, bear);

        bear.Zone.Should().Be(ZoneType.Graveyard, "illegal target → no bounce (CR 608.2b)");
        _bob.LifeTotal.Should().Be(20, "no life loss when the target fizzles");
    }

    [Fact]
    public async Task LoseLifeTarget_SubjectTarget_DrainsTargetedPlayerDirectly()
    {
        // "Target player loses 2 life" — the lose-life verb standalone with
        // subject "target": it declares its OWN player target slot.
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Mind Rot Lite",
            new EffectDefinition[]
            {
                new LoseLifeTargetEffectDef { Amount = 2, Subject = "target", TargetFilter = "player" },
            });

        def.TargetRequests.Should().HaveCount(1);

        var card = CastInstant("Mind Rot Lite", "{B}");
        await CastAndResolve(card, def, _bob);

        _bob.LifeTotal.Should().Be(18, "the targeted player loses 2 life (CR 119.3)");
        _alice.LifeTotal.Should().Be(20);
    }

    // ── gain_control (Threaten / Act of Treason, until end of turn) ────────────

    [Fact]
    public async Task GainControl_StealsChosenCreature_UntapsAndGrantsHaste()
    {
        var continuous = new Majik.Core.Effects.ContinuousEffectsService(_bus);
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Act of Treason",
            new EffectDefinition[]
            {
                new GainControlEffectDef { TargetFilter = "creature", Duration = "end_of_turn" },
            },
            replacements: null,
            continuous: continuous);

        // Bob's tapped, summoning-sick creature.
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        var card = CastInstant("Act of Treason", "{2}{R}");
        await CastAndResolve(card, def, bear);

        bear.Controller.Should().BeSameAs(_alice, "the caster gains control of the chosen creature (CR 613.2)");
        bear.IsTapped.Should().BeFalse("Threaten untaps the gained creature (CR 701.21)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should()
            .BeTrue("the gained creature gains haste until end of turn (CR 302.6)");
    }

    [Fact]
    public async Task GainControl_RevertsToOwner_AtEndOfTurnCleanup()
    {
        var continuous = new Majik.Core.Effects.ContinuousEffectsService(_bus);
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Act of Treason",
            new EffectDefinition[] { new GainControlEffectDef { TargetFilter = "creature" } },
            replacements: null,
            continuous: continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;

        var card = CastInstant("Act of Treason", "{2}{R}");
        await CastAndResolve(card, def, bear);
        bear.Controller.Should().BeSameAs(_alice);

        // CR 514.2 — cleanup step ends the until-end-of-turn control change.
        continuous.ExpireEndOfTurn();

        bear.Controller.Should().BeSameAs(_bob, "control reverts to the owner at cleanup (CR 514.2)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should()
            .BeFalse("the until-EOT haste grant ends at cleanup too");
    }

    [Fact]
    public async Task GainControl_IllegalTarget_FizzlesCleanly()
    {
        var continuous = new Majik.Core.Effects.ContinuousEffectsService(_bus);
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Act of Treason",
            new EffectDefinition[] { new GainControlEffectDef { TargetFilter = "creature" } },
            replacements: null,
            continuous: continuous);

        // Already in the graveyard — illegal at resolution (CR 608.2b).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.ActiveEffects = continuous;
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var card = CastInstant("Act of Treason", "{2}{R}");
        await CastAndResolve(card, def, bear);

        bear.Controller.Should().BeSameAs(_bob, "illegal target at resolution → no control change (CR 608.2b)");
    }

    [Fact]
    public void GainControl_DeclaresSingleTargetRequest()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Act of Treason",
            new EffectDefinition[] { new GainControlEffectDef { TargetFilter = "creature" } });

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }
}
