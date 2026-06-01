using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sandstorm Verge (Edge of Eternities,
/// Land — Desert).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "{T}: Add {C}.
///    {3}, {T}: Target creature can't block this turn. Activate only as a
///    sorcery."
///
/// Sibling of <see cref="TectonicEdgeFactory"/> (a {C}-producing utility land
/// whose printed mana ability + an activated ability share one body): the
/// base shape — Land — Desert plus the <b>{T}: Add {C}</b> mana ability
/// (CR 605.1, no stack) — is materialised from the embedded JSON definition
/// (<c>sandstorm-verge.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="GhituEncampmentFactory"/>). The {3}, {T} "can't block" ability
/// is layered on here because the JSON <c>AbilityDefinition</c> schema does
/// not express targeted activated abilities yet.
///
/// ## Implemented (v1)
/// - <b>Land — Desert</b> identity (from JSON; no supertypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (from JSON,
///   CR 605.1).
/// - <b>{3}, {T}: Target creature can't block this turn. Activate only as a
///   sorcery.</b> — an <see cref="ActivatedAbility"/> with:
///     - <see cref="ManaCostCost"/> {3}
///     - <see cref="AdditionalCost.Tap"/>
///     - <c>sorcerySpeed: true</c> — CR 117.1a / 307.5 "Activate only as a
///       sorcery"; <see cref="Rules.ActionValidator"/> rejects activation
///       outside the controller's main phase / with a non-empty stack.
///   A 1..1 <see cref="TargetRequest"/> declares "target creature". On
///   resolution the effect reads the chosen target out of
///   <see cref="ActivatedAbility.ChosenTargets"/>, rechecks the target is a
///   Creature still on the battlefield (CR 608.2b illegal-target check), and
///   registers an EOT-scoped <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/> against the target's
///   <see cref="Creature.ActiveEffects"/> — the exact CannotBlock posture
///   used by <see cref="EarthshakerKhenraFactory"/>. The "this turn" rider
///   maps to the effect's default <c>expiresAtEndOfTurn: true</c> (CR 514.2).
///
/// ## v1 posture (shared with the CannotBlock family)
/// - <b>Target legality at choose-time</b>: a live
///   <see cref="TargetRequest.CandidateGatherer"/> surfaces every creature on
///   the board (any creature is a legal target — there is no power gate, unlike
///   Earthshaker Khenra). The resolve-time recheck (CR 608.2b) re-validates the
///   pick fizzles cleanly if the target left the battlefield between choose and
///   resolve.
/// - <b>Null <see cref="Creature.ActiveEffects"/></b> (shape-only tests): the
///   CannotBlock grant silently no-ops, same as Earthshaker Khenra.
/// </summary>
[CardName("Sandstorm Verge")]
public static class SandstormVergeFactory
{
    public const string CardName = "Sandstorm Verge";
    public const string Slug = "sandstorm-verge";

    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land — Desert
        // type, {T}: Add {C} mana ability). The {3}, {T} can't-block ability
        // is layered on below — the JSON AbilityDefinition schema does not
        // express targeted activated abilities yet (same posture as Ghitu
        // Encampment's animate ability).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {3}, {T}: Target creature can't block this turn.
        // Activate only as a sorcery.
        //
        // CR 602 — ordinary activated ability (uses the stack). CR 117.1a /
        // 307.5 — the sorcery-speed rider is carried by sorcerySpeed: true.
        // ----------------------------------------------------------------
        ActivatedAbility? cantBlockAbility = null;
        var cantBlockEffect = new Effect(
            $"{CardName}: target creature can't block this turn",
            () =>
            {
                if (cantBlockAbility == null) return;
                var chosen = cantBlockAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b

                // CR 509.1c — register a CannotBlock restriction scoped to the
                // chosen creature. The default ExpiresAtEndOfTurn matches the
                // printed "this turn" rider (CR 514.2). The restriction lives
                // on the target's ContinuousEffectsService (Creature.ActiveEffects);
                // the combat validator queries there. When ActiveEffects is null
                // (shape-only tests) the grant silently no-ops — Earthshaker
                // Khenra posture.
                if (target.ActiveEffects == null) return;
                target.ActiveEffects.Register(
                    new CombatRestrictionEffect(CombatRestriction.CannotBlock, target));
            });

        cantBlockAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { cantBlockEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live candidate gatherer (agent-prompt MVP). Any creature
                    // is a legal target (no power gate). The resolve-time recheck
                    // (CR 608.2b) re-validates still-on-battlefield at resolution.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            // CR 117.1a / 307.5 — "Activate only as a sorcery."
            sorcerySpeed: true);

        land.AddAbility(cantBlockAbility);

        return land;
    }
}
