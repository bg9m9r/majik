using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Desert (Arabian Nights and many reprints). Land —
/// Desert.
///
/// Oracle text (verified against the embedded Modern seed / Scryfall
/// 2026-06-02):
///   "{T}: Add {C}.
///    {T}: This land deals 1 damage to target attacking creature. Activate
///    only during the end of combat step."
///
/// Shares the {C} Desert mana shape of <see cref="HostileDesertFactory"/>
/// (Land + Desert subtype + a single {T}: Add {C} mana ability sourced from
/// the embedded JSON definition) and adds a {T} pinger that mirrors
/// <see cref="BarbarianRingFactory"/>'s damage ability — an
/// <see cref="ActivatedAbility"/> with a single 1..1 target request resolving
/// through <see cref="Fx.DealDamageAny"/> — narrowed here to "target attacking
/// creature" and dealing 1 damage instead of any-target / 2.
///
/// ## Implemented (v1)
/// - <b>Land — Desert</b> (nonbasic, no supertype) materialised from
///   <c>desert.json</c> via <see cref="CardDefinitionLoader.FromEmbeddedResource"/>
///   + <see cref="CardDefinitionFactory.Build"/>.
/// - <b>{T}: Add {C}</b> — vanilla colourless <see cref="ManaAbility"/> from the
///   JSON definition (CR 605.1 — mana abilities don't use the stack).
/// - <b>{T}: This land deals 1 damage to target attacking creature.</b> —
///   an <see cref="ActivatedAbility"/> with a single <see cref="AdditionalCost.Tap"/>
///   cost (the {T} is the whole cost; no mana) and a mandatory 1..1
///   <see cref="TargetRequest"/> (CR 601.2c). On resolution the chosen creature
///   takes 1 damage via <see cref="Fx.DealDamageAny"/> (CR 119 — creature
///   damage; the dispatcher also covers planeswalker / player defensively even
///   though only a creature is a legal target here). CR 608.2b — no chosen
///   target → clean no-op.
///
///   * <b>"Activate only during the end of combat step" (CR 602.5b)</b> — a
///     timing activation restriction, modelled as the ability's CONTEXT-AWARE
///     <c>canActivateCheckCtx</c> gate (the same seam Hired Claw's "an opponent
///     lost life this turn" rider uses). The gate reads the live step off the
///     <see cref="Majik.Core.Game.GameContext.CurrentPhase"/> the engine threads
///     into every activation-legality consult (the live TurnDriver / GameFacade
///     dispatch path and the bot's <c>LegalActionEnumerator</c>), so the timing
///     restriction now WORKS on the production routed build — the public
///     <see cref="IsEndOfCombatStep"/> predicate (kept for direct bot-policy /
///     action-validator use) is the same gate, not a stand-in. The context-less
///     consult falls back to "true" (CR 602.5c posture) so shape-only tests
///     aren't wedged. The ability stays instant-speed (the restriction is the
///     timing rider, not sorcery speed).
///
/// ## v1 posture (documented narrowing — no new mechanic)
/// - <b>"attacking" candidate restriction</b> — same gap as
///   <see cref="RestlessRidgelineFactory"/>: the engine has no per-<see cref="Creature"/>
///   "is attacking" flag reachable from this factory closure (attacking state
///   lives on the combat object). The candidate gatherer therefore offers all
///   battlefield creatures; the "attacking" qualifier is recorded in the
///   request description. Resolution honours whatever target the
///   controller/agent supplied.
/// </summary>
[CardName("Desert")]
public static class DesertFactory
{
    public const string CardName = "Desert";
    public const string Slug = "desert";
    public const int DamageAmount = 1;

    /// <summary>
    /// Construct Desert owned and controlled by <paramref name="owner"/>. The
    /// {C} mana ability (from JSON) + the {T} pinger are attached so the card
    /// surface is complete. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type, Desert
        // subtype, {T}: Add {C} mana ability). The pinger is layered on below —
        // its target request + damage resolution are not expressible in the
        // current JSON AbilityDefinition schema (same posture as Hostile
        // Desert's animate ability).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: This land deals 1 damage to target attacking creature.
        //      Activate only during the end of combat step.
        //
        // CR 602 — ordinary activated ability (uses the stack). The only cost
        // is {T} (AdditionalCost.Tap). A single mandatory 1..1 target request
        // (CR 601.2c) offers battlefield creatures (the "attacking" qualifier
        // is a documented v1 narrowing — see class summary). On resolution the
        // chosen creature takes 1 damage via Fx.DealDamageAny (CR 119).
        // CR 608.2b — no chosen target → clean no-op.
        // ----------------------------------------------------------------
        ActivatedAbility? pinger = null;

        var targetRequest = new TargetRequest(
            Description: "target attacking creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal,
            CandidateGatherer: _ => GatherCreatures(land));

        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to target attacking creature",
            () =>
            {
                if (pinger == null
                    || pinger.ChosenTargets.Count == 0
                    || pinger.ChosenTargets[0].Count == 0)
                {
                    return; // CR 608.2b — no chosen target → no-op.
                }

                var target = pinger.ChosenTargets[0][0];
                Fx.DealDamageAny(target, DamageAmount);
            });

        pinger = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[] { targetRequest },
            // CR 602.5b — "Activate only during the end of combat step." Modelled
            // as the ability's CONTEXT-AWARE canActivateCheckCtx gate (the same
            // seam Hired Claw's "an opponent lost life this turn" rider uses):
            // true iff the live step threaded onto the GameContext is the
            // end-of-combat step. The engine supplies a GameContext at every
            // activation-legality consult (AbilityActivator.CanActivate on the
            // live TurnDriver/GameFacade dispatch path, and the bot's
            // LegalActionEnumerator), so the timing restriction now WORKS on the
            // production routed build — it is no longer merely the public
            // IsEndOfCombatStep predicate. GameContext.CurrentPhase carries the
            // live StepStateType (its name predates the CR phase/step rename).
            // The context-less consult (no GameContext) falls back to "true" so
            // shape-only tests / harnesses without a live step aren't wedged.
            canActivateCheckCtx: ctx =>
                ctx.CurrentPhase is { } step && IsEndOfCombatStep(step));

        land.AddAbility(pinger);

        return land;
    }

    /// <summary>
    /// CR 602.5b — "Activate only during the end of combat step." True iff the
    /// supplied step is the <see cref="StepStateType.EndOfCombat"/> step.
    /// Public so bot policies / the action validator can gate the pinger's
    /// activation timing (mirrors <see cref="BarbarianRingFactory.IsThresholdActive"/>
    /// exposing its Threshold gate).
    /// </summary>
    public static bool IsEndOfCombatStep(StepStateType step) =>
        step == StepStateType.EndOfCombat;

    /// <summary>
    /// CR 601.2c — candidate pool for "target attacking creature": every
    /// battlefield <see cref="Creature"/> controlled by either player. The
    /// "attacking" qualifier is a documented v1 narrowing — the engine has no
    /// per-creature attacking flag reachable here, so all creatures are offered
    /// (same posture as <see cref="RestlessRidgelineFactory"/>). Scans both
    /// players' battlefields, deduped by reference.
    /// </summary>
    private static IReadOnlyList<object> GatherCreatures(Land self)
    {
        var result = new List<object>();
        foreach (var p in new[] { self.Owner, self.Controller })
        {
            if (p == null) continue;
            foreach (var c in p.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                if (!result.Any(r => ReferenceEquals(r, c))) result.Add(c);
            }
        }
        return result;
    }
}
