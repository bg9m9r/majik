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
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

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

    // ── fight (CR 701.12) ────────────────────────────────────────────────────

    private static SpellDefinition PreyUponDef() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Prey Upon",
            new EffectDefinition[]
            {
                new FightEffectDef
                {
                    Source = "target",
                    ControllerTargetFilter = "creature_you_control",
                    TargetFilter = "creature_you_dont_control",
                },
            });

    private Creature WithKeyword(Creature c, string keyword)
    {
        c.AddAbility(new Majik.Core.Abilities.KeywordAbility(keyword, c, c.Controller));
        return c;
    }

    private async Task CastFightAndResolve(
        Instant card, SpellDefinition def, object fighter, object other)
    {
        var agent = new ScriptedAgent();
        // Two ordered target slots: fighter ("you control") then other.
        agent.QueueTargets(new[] { fighter });
        agent.QueueTargets(new[] { other });
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(_alice, card, def, agent, NewContext(), alternativeCost: null);
        _resolver.ResolveTop(_stack);
    }

    [Fact]
    public void PreyUpon_DeclaresTwoTargetRequests()
    {
        // source: "target" declares the fighter + the other creature.
        PreyUponDef().TargetRequests.Should().HaveCount(2);
    }

    [Fact]
    public void TwoFights_DeclareFourContiguousTargetRequests()
    {
        // Two source:"target" fights in one spell exercise the N-slot list
        // threading across MULTIPLE multi-slot effects: each contributes a
        // primary + one extra, so the spell announces four ordered slots
        // (fighterA, otherA, fighterB, otherB) — proving ToExtraTargetRequests
        // is appended as an ordered run, not a single fixed 2nd slot.
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Double Prey",
            new EffectDefinition[]
            {
                new FightEffectDef
                {
                    Source = "target",
                    ControllerTargetFilter = "creature_you_control",
                    TargetFilter = "creature_you_dont_control",
                },
                new FightEffectDef
                {
                    Source = "target",
                    ControllerTargetFilter = "creature_you_control",
                    TargetFilter = "creature_you_dont_control",
                },
            });

        def.TargetRequests.Should().HaveCount(4);
    }

    // ── N-slot extra-target hook (generalization of the fight 2-slot path) ────

    /// <summary>Records the <see cref="ResolutionContext.ChosenTargets"/> it
    /// is handed so the test can assert the spell bridge delivered every
    /// contiguous slot in order.</summary>
    private sealed class RecordingEffect : Majik.Core.Abilities.IEffect
    {
        public System.Collections.Generic.IReadOnlyList<
            System.Collections.Generic.IReadOnlyList<object>>? Seen { get; private set; }

        public string Description => "recording";

        public System.Threading.Tasks.ValueTask ExecuteAsync(
            Majik.Core.Abilities.ResolutionContext ctx)
        {
            Seen = ctx.ChosenTargets;
            return System.Threading.Tasks.ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task SpellTargetedEffect_DeliversThreeOrderedPicks_ForNSlotVerb()
    {
        // The 2nd-target hook generalized to N slots: an effect that declares a
        // primary + TWO extra contiguous slots must read its three picks at
        // ChosenTargets[0..2] in declaration order at resolution. This is the
        // one-up of the fight (CR 701.12) two-slot path — proves the spell
        // adapter threads an ordered LIST of extra picks, not a single 2nd slot.
        var primary = new object();
        var extra1 = new object();
        var extra2 = new object();

        var inner = new RecordingEffect();
        var wrapped = new CardDefRuntime.SpellTargetedEffect(
            inner,
            picks: new[] { primary },
            extraPicks: new System.Collections.Generic.IReadOnlyList<object>[]
            {
                new[] { extra1 },
                new[] { extra2 },
            });

        await wrapped.ExecuteAsync(Majik.Core.Abilities.ResolutionContext.Legacy);

        inner.Seen.Should().NotBeNull();
        var seen = inner.Seen!;
        seen.Should().HaveCount(3, "primary + two extra contiguous slots");
        seen[0].Should().ContainSingle().Which.Should().BeSameAs(primary);
        seen[1].Should().ContainSingle().Which.Should().BeSameAs(extra1);
        seen[2].Should().ContainSingle().Which.Should().BeSameAs(extra2);
    }

    [Fact]
    public async Task PreyUpon_BothCreatures_TakeEachOthersPower()
    {
        var mine = OnBattlefield(new Creature("Mine", "{G}", 3, 4), _alice);
        var theirs = OnBattlefield(new Creature("Theirs", "{G}", 2, 5), _bob);

        var card = CastInstant("Prey Upon", "{G}");
        await CastFightAndResolve(card, PreyUponDef(), mine, theirs);

        // CR 701.12a — simultaneous bilateral power damage.
        mine.Damage.Should().Be(2, "Theirs has power 2");
        theirs.Damage.Should().Be(3, "Mine has power 3");
    }

    [Fact]
    public async Task PreyUpon_Deathtouch_MarksTheOtherForDestruction()
    {
        // CR 702.2b — deathtouch applies to fight damage (it is damage, not
        // combat damage).
        var snake = WithKeyword(OnBattlefield(new Creature("Snake", "{G}", 1, 1), _alice), "Deathtouch");
        var giant = OnBattlefield(new Creature("Giant", "{G}", 0, 8), _bob);

        var card = CastInstant("Prey Upon", "{G}");
        await CastFightAndResolve(card, PreyUponDef(), snake, giant);

        giant.MarkedForDestructionByDeathtouch.Should().BeTrue();
        snake.Damage.Should().Be(0, "the giant has 0 power and deals no damage back");
    }

    [Fact]
    public async Task PreyUpon_Lifelink_GainsControllerLife()
    {
        // CR 702.15a — lifelink applies to fight damage.
        var vamp = WithKeyword(OnBattlefield(new Creature("Vampire", "{G}", 3, 3), _alice), "Lifelink");
        var bear = OnBattlefield(new Creature("Bear", "{G}", 2, 2), _bob);

        var card = CastInstant("Prey Upon", "{G}");
        await CastFightAndResolve(card, PreyUponDef(), vamp, bear);

        _alice.LifeTotal.Should().Be(23, "lifelink gains 3 from the 3 fight damage");
    }

    [Fact]
    public async Task PreyUpon_IllegalOtherTarget_WholeFightFizzles()
    {
        // CR 608.2b / 701.12c — if the OTHER creature is gone at resolution,
        // the fight fizzles entirely: the fighter takes no damage either.
        var mine = OnBattlefield(new Creature("Mine", "{G}", 3, 4), _alice);

        var theirs = new Creature("Theirs", "{G}", 5, 5);
        theirs.SetOwner(_bob);
        theirs.SetController(_bob);
        theirs.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(theirs);

        var card = CastInstant("Prey Upon", "{G}");
        await CastFightAndResolve(card, PreyUponDef(), mine, theirs);

        mine.Damage.Should().Be(0, "a fight needs both creatures — the other is gone");
        theirs.Damage.Should().Be(0);
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

    // ── counter_target_spell (CR 701.5) ──────────────────────────────────────

    // Counter resolution reaches the live stack off ctx.Game.Stack, so the
    // resolver must thread the live GameContext (the prod PriorityLoop does;
    // the no-arg ResolveTop overload passes null and the counter no-ops).
    private async Task CastAndResolveWithGame(Instant card, SpellDefinition def, object? chosen)
    {
        var agent = new ScriptedAgent();
        if (chosen != null) agent.QueueTargets(new[] { chosen });
        else agent.QueueTargets(System.Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);

        var ctx = NewContext();
        await _flow.CastAsync(_alice, card, def, agent, ctx, alternativeCost: null);
        await _resolver.ResolveTopAsync(_stack, game: ctx);
    }

    private Majik.Core.Spells.Spell BobCasts(Card spellCard)
    {
        spellCard.SetOwner(_bob);
        spellCard.SetController(_bob);
        var spell = new Majik.Core.Spells.Spell(spellCard, _bob);
        _stack.Push(spell);
        return spell;
    }

    [Fact]
    public async Task CounterTargetSpell_CountersChosenSpell_ToGraveyard()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Cancel",
            new EffectDefinition[] { new CounterTargetSpellEffectDef() });

        var bolt = new Instant("Lightning Bolt", "{R}");
        var bobSpell = BobCasts(bolt);

        var card = CastInstant("Cancel", "{1}{U}{U}");
        await CastAndResolveWithGame(card, def, bobSpell);

        bolt.Zone.Should().Be(ZoneType.Graveyard, "the countered spell goes to its owner's graveyard (CR 701.5)");
        _stack.GetAll().Should().NotContain(bobSpell);
    }

    [Fact]
    public async Task CounterTargetSpell_AndGainLife_ResolvesBothClauses()
    {
        // Absorb shape — counter target spell, THEN gain 3 life (CR 608.2c).
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Absorb",
            new EffectDefinition[]
            {
                new CounterTargetSpellEffectDef(),
                new GainLifeSelfEffectDef { Amount = 3 },
            });

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var bobSpell = BobCasts(bear);

        var card = CastInstant("Absorb", "{W}{U}{U}");
        await CastAndResolveWithGame(card, def, bobSpell);

        bear.Zone.Should().Be(ZoneType.Graveyard, "Absorb counters ANY target spell (no type rider)");
        _alice.LifeTotal.Should().Be(23, "the lifegain rider resolves alongside the counter (CR 119.3)");
    }

    [Fact]
    public async Task CounterTargetSpell_Noncreature_DoesNotCounterCreatureSpell()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Negate",
            new EffectDefinition[] { new CounterTargetSpellEffectDef { Noncreature = true } });

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var bobSpell = BobCasts(bear);

        var card = CastInstant("Negate", "{1}{U}");
        await CastAndResolveWithGame(card, def, bobSpell);

        // CR 608.2b — a creature spell is an illegal target for the noncreature
        // rider; the counter does nothing and the creature spell stays.
        bear.Zone.Should().NotBe(ZoneType.Graveyard, "the noncreature rider gates creature spells out");
        _stack.GetAll().Should().Contain(bobSpell);
    }

    [Fact]
    public async Task CounterTargetSpell_Creature_CountersOnlyCreatureSpell()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Essence Scatter",
            new EffectDefinition[] { new CounterTargetSpellEffectDef { Creature = true } });

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var bobSpell = BobCasts(bear);

        var card = CastInstant("Essence Scatter", "{1}{U}");
        await CastAndResolveWithGame(card, def, bobSpell);

        bear.Zone.Should().Be(ZoneType.Graveyard, "the creature rider counters a creature spell (CR 701.5)");
    }

    [Fact]
    public void CounterTargetSpell_DeclaresSingleSpellTargetRequest()
    {
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Cancel",
            new EffectDefinition[] { new CounterTargetSpellEffectDef() });

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("spell");
        def.TargetRequests[0].Intent.Should().Be(Majik.Core.Cards.BotIntent.Counter);
    }
}
