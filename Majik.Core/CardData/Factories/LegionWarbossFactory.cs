using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Legion Warboss (Guilds of Ravnica, {1}{R}).
/// Creature — Goblin Soldier, 2/1.
///
/// Oracle text (verified against Gatherer / Scryfall 2026-05-29):
///   "Mentor (Whenever this creature attacks, put a +1/+1 counter on target
///    attacking creature with lesser power.)
///    At the beginning of combat on your turn, create a 1/1 red Goblin
///    creature token. That token gains haste until end of turn and attacks
///    this combat if able."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Goblin Soldier, mana cost {1}{R}, owner/controller wired.
///   Base shape materialised from the embedded JSON definition
///   (<c>legion-warboss.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (same posture as
///   <see cref="FirebrandArcherFactory"/> / <see cref="RestlessBivouacFactory"/>).
/// - <b>Mentor (CR 702.134)</b> — "Whenever this creature attacks, put a
///   +1/+1 counter on target attacking creature with lesser power." Wired as
///   a <see cref="TriggeredAbility"/> over
///   <see cref="CreatureAttacksEvent"/> matching Warboss itself
///   (<see cref="Triggers.OnAttackSelf"/>), with a 1..1
///   <see cref="TargetRequest"/> "target attacking creature with lesser
///   power". Candidate enumeration + the lesser-power comparison go through
///   the supplied <c>attackingCreaturesSource</c> closure (same closure shape
///   as <see cref="GoblinRabblemasterFactory"/> — the engine does not yet
///   expose a live "currently attacking creatures" view from inside the
///   effect closure). On resolution the chosen target's legality is rechecked
///   (CR 608.2b — must still be an attacking creature on the battlefield whose
///   power is strictly less than Warboss's current power) before one
///   <see cref="CounterType.PlusOnePlusOne"/> counter is placed (mirrors
///   <see cref="RestlessBivouacFactory"/>'s targeted-counter attack trigger).
///   The token Warboss just made at beginning of combat IS an attacking
///   creature with lesser power (1 &lt; 2), so it is a legal Mentor target —
///   the canonical line ("the token enters before attackers are declared, so
///   it can attack and then be mentored"). Note: per CR 702.134a a creature
///   can be the target of its own Mentor only if it has lesser power than the
///   source; Warboss (power 2) can never target itself (2 is not &lt; 2),
///   which the strict <c>&lt;</c> recheck enforces.
/// - <b>Begin-combat token (CR 508.1 — "At the beginning of combat on your
///   turn")</b> — wired as a <see cref="TriggeredAbility"/> over
///   <see cref="StepStartedEvent"/> for
///   <see cref="PhaseStateType.BeginningOfCombat"/> restricted to the
///   controller's own turns (<see cref="Triggers.OnStepBegin"/>). On
///   resolution it creates one 1/1 red Goblin creature token under Warboss's
///   controller (CR 111 / CR 111.4) via
///   <see cref="TokenFactory.CreateOnBattlefield"/> and grants it Haste until
///   end of turn (CR 702.10 / 613.1c) via
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>, clearing summoning
///   sickness so it can be declared as an attacker the same turn.
///
/// ## Deferred (v1 gaps)
/// - <b>"attacks this combat if able"</b> (CR 508.1g — must-attack combat
///   requirement). Shipped as an <c>"AttacksThisCombat"</c>
///   <see cref="KeywordAbility"/> marker on the token only; the must-attack
///   primitive is not wired into combat declaration yet (same posture as
///   <see cref="UlamogsCrusherFactory"/>'s "attacks each combat if able"
///   marker). The token is still created with Haste, so it CAN attack; it
///   simply isn't yet forced to.
/// - <b>Live combat-attackers provider</b>: production callers must wire the
///   <c>attackingCreaturesSource</c> closure for the Mentor target pool /
///   lesser-power recheck. When null, the Mentor trigger's pump body is a
///   no-op (token-creation and Haste are unaffected). Same caveat as
///   <see cref="GoblinRabblemasterFactory"/> — drops once
///   <c>ICurrentCombatProvider</c> ships.
/// - <b>Red colour identity of the token</b> stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/> (the token is correctly red);
///   no further Layer-5 work needed.
/// - <b>Trigger-on-stack timing</b>: the Mentor counter and the begin-combat
///   token are registered immediately when each trigger effect runs. Real MTG
///   puts each trigger on the stack and resolves it in APNAP order; v1
///   collapses this to trigger-resolves-now (observationally equivalent for
///   the counter placement / token creation here).
/// </summary>
[CardName("Legion Warboss")]
public static class LegionWarbossFactory
{
    public const string CardName = "Legion Warboss";
    public const string Slug = "legion-warboss";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Legion Warboss with no live runtime services. Suitable for
    /// card-shape / dispatcher tests — the Mentor attack trigger and the
    /// begin-combat token trigger are attached to the card shape (so
    /// <see cref="ICard.Abilities"/> includes them) but are not registered
    /// with any <see cref="TriggerManager"/>, and the Mentor pump body is a
    /// no-op (no attackers source). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(
            owner,
            triggers: null,
            attackingCreaturesSource: null,
            zoneService: null);

    /// <summary>
    /// Construct a fully-wired Legion Warboss.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the Mentor attack
    /// trigger and the begin-combat token trigger against. May be null — both
    /// triggers are still attached to the card shape.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list. Called at Mentor resolution to enumerate legal
    /// "attacking creature with lesser power" candidates and to recheck the
    /// chosen target. May be null — the Mentor pump body is a no-op (the
    /// begin-combat token is still created).</param>
    /// <param name="zoneService">Optional zone service so the token's ETB
    /// CardMovedEvent fires (Soul Warden etc.). Pass <c>null</c> for raw zone
    /// moves.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        System.Func<System.Collections.Generic.IReadOnlyList<Creature>>? attackingCreaturesSource,
        ZoneService? zoneService = null)
    {
        System.ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Goblin + Soldier subtypes, {1}{R}, 2/1). The Mentor trigger and the
        // begin-combat token trigger are layered on below — neither is
        // expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Mentor (CR 702.134) — "Whenever this creature attacks, put a
        // +1/+1 counter on target attacking creature with lesser power."
        //
        // A 1..1 TargetRequest. Candidate enumeration + the strict
        // lesser-power comparison flow through attackingCreaturesSource
        // (same closure posture as Goblin Rabblemaster). The chosen target
        // is set via TriggeredAbility.SetChosenTargets by the prompt
        // pipeline / tests; resolution rechecks legality (CR 608.2b).
        // ----------------------------------------------------------------
        TriggeredAbility? mentorTrigger = null;
        var mentorEffect = new Effect(
            $"{CardName} (Mentor): put a +1/+1 counter on target attacking creature with lesser power",
            () =>
            {
                if (mentorTrigger == null) return;
                if (attackingCreaturesSource == null) return; // shape-only path
                if (mentorTrigger.ChosenTargets.Count == 0) return;
                if (mentorTrigger.ChosenTargets[0].Count == 0) return;

                if (mentorTrigger.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — resolve-time legality recheck. The chosen
                // target must STILL be an attacking creature on the
                // battlefield whose power is strictly less than Warboss's
                // current power (CR 702.134a — "lesser power").
                var attackers = attackingCreaturesSource()
                    ?? System.Array.Empty<Creature>();
                bool stillAttacking = attackers.Any(a => ReferenceEquals(a, target));
                if (!stillAttacking) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;
                if (target.Power >= card.Power) return; // strict "lesser power"

                target.Counters.Add(CounterType.PlusOnePlusOne, 1);
            });

        mentorTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { mentorEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target attacking creature with lesser power",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(mentorTrigger);
        triggers?.RegisterTriggeredAbility(mentorTrigger);

        // Mentor is a keyword ability (CR 702.134); expose a marker so the
        // keyword scan surface is uniform (Trample / Haste shape).
        card.AddAbility(new KeywordAbility("Mentor", card, owner));

        // ----------------------------------------------------------------
        // "At the beginning of combat on your turn, create a 1/1 red Goblin
        // creature token. That token gains haste until end of turn and
        // attacks this combat if able." (CR 508.1 begin-combat trigger.)
        //
        // Restricted to the controller's own turns via
        // Triggers.OnStepBegin(owner, BeginningOfCombat).
        // ----------------------------------------------------------------
        var beginCombatEffect = new Effect(
            $"{CardName}: at beginning of combat, create a 1/1 red Goblin token with haste (attacks if able)",
            () => CreateGoblinToken(card.Controller ?? owner, zoneService));

        var beginCombatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.BeginningOfCombat),
            effects: new IEffect[] { beginCombatEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(beginCombatTrigger);
        triggers?.RegisterTriggeredAbility(beginCombatTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 red Goblin creature token under
    /// <paramref name="controller"/>'s control, grant it Haste until end of
    /// turn (CR 702.10 / 613.1c) and clear summoning sickness so it can be
    /// declared as an attacker the same turn. The "attacks this combat if
    /// able" must-attack requirement (CR 508.1g) is recorded as an
    /// "AttacksThisCombat" <see cref="KeywordAbility"/> marker only — the
    /// must-attack primitive isn't wired into combat declaration yet (same
    /// posture as <see cref="UlamogsCrusherFactory"/>).
    /// </summary>
    public static Creature CreateGoblinToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        System.ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Goblin creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Red });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // "That token gains haste until end of turn." CR 613.1c (Layer 6)
        // keyword grant + CR 702.10b summoning-sickness lift so it can be
        // declared as an attacker the same turn. A freshly-minted token has
        // no ContinuousEffectsService wired, so attach one and register the
        // EOT grant against it (the grant expires via the service's cleanup
        // pass, CR 514.2). HasHaste reads the computed keyword set off this
        // service.
        token.ActiveEffects ??= new ContinuousEffectsService();
        token.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(token, "Haste"));
        token.HasSummoningSickness = false;

        // "and attacks this combat if able." CR 508.1g must-attack
        // requirement — marker only (primitive not wired yet; same posture
        // as Ulamog's Crusher).
        token.AddAbility(new KeywordAbility("AttacksThisCombat", token, controller));

        return token;
    }
}
