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
/// ## v1 posture (documented narrowings — no new mechanic)
/// - <b>"attacking" candidate restriction</b> — same gap as
///   <see cref="RestlessRidgelineFactory"/>: the engine has no per-<see cref="Creature"/>
///   "is attacking" flag reachable from this factory closure (attacking state
///   lives on the combat object). The candidate gatherer therefore offers all
///   battlefield creatures; the "attacking" qualifier is recorded in the
///   request description. Resolution honours whatever target the
///   controller/agent supplied.
/// - <b>"Activate only during the end of combat step" (CR 602.5b)</b> — a
///   timing activation restriction. The current step is not reachable from this
///   factory closure (it lives in the game's phase machine, not on
///   <see cref="Player"/>), so — exactly as <see cref="BarbarianRingFactory"/>
///   exposes its Threshold gate — the step gate is surfaced as the public
///   <see cref="IsEndOfCombatStep"/> predicate for bot-policy /
///   action-validator use until <c>IActivatedAbility</c> ships a step-aware
///   <c>CanActivate</c> hook. The dispatcher path does not wire live phase
///   state, so the ability is constructed instant-speed (the restriction is the
///   timing rider, not sorcery speed).
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
            targetRequests: new[] { targetRequest });

        land.AddAbility(pinger);

        return land;
    }

    /// <summary>
    /// CR 602.5b — "Activate only during the end of combat step." True iff the
    /// supplied step is the <see cref="PhaseStateType.EndOfCombat"/> step.
    /// Public so bot policies / the action validator can gate the pinger's
    /// activation timing (mirrors <see cref="BarbarianRingFactory.IsThresholdActive"/>
    /// exposing its Threshold gate).
    /// </summary>
    public static bool IsEndOfCombatStep(PhaseStateType step) =>
        step == PhaseStateType.EndOfCombat;

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
