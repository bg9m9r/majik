using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spymaster's Vault (Modern Horizons 3).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped unless you control a Swamp.
///    {T}: Add {B}.
///    {B}, {T}: Target creature you control connives X, where X is the
///    number of creatures that died this turn. (Draw X cards, then discard
///    X cards. Put a +1/+1 counter on that creature for each nonland card
///    discarded this way.)"
///
/// Scryfall-confirmed type line: Land (no basic supertype, no subtypes).
/// Spymaster's Vault is NOT itself a Swamp, so it cannot satisfy its own
/// ETB-tapped predicate.
///
/// The card's base shape (name, Land, the <c>{T}: Add {B}</c> mana ability)
/// is materialised from the embedded JSON definition
/// (<c>spymasters-vault.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The connive activated ability
/// is layered on here — the JSON <c>AbilityDefinition</c> schema's
/// <c>connive_self</c> verb applies to the SOURCE card (a Land, which the
/// connive routine no-ops on) and carries only a fixed <c>amount</c>, so it
/// cannot express "target creature you control connives X, X = creatures
/// died this turn" (same posture as <see cref="EldraziDisplacerFactory"/> /
/// <see cref="StormscaleScionFactory"/> whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Land identity + {T}: Add {B}</b> — from the JSON definition (the
///   mana ability is a vanilla <see cref="ManaAbility"/>, CR 605.1).
/// - <b>"This land enters tapped unless you control a Swamp" (CR 614.1c)</b>
///   — handled at battlefield entry by the engine's
///   <see cref="Majik.Core.CardData.SubtypeEntersTappedBinder"/> reading the
///   printed oracle (the same path every "enters tapped unless you control a
///   [subtype]" land takes — Drowned Catacomb, Sulfur Falls, etc.). The
///   named factory deliberately does NOT re-register the ETB replacement
///   (the single-arg dispatch path has no <see cref="Majik.Core.Effects.ReplacementBus"/>;
///   shape-only posture matching every other ETB-replacement factory's
///   dispatch path, e.g. <see cref="CastleLocthwainFactory"/>).
/// - <b>{B}, {T}: Target creature you control connives X (CR 602 / 701.50)</b>
///   — an <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{B}"), AdditionalCost.Tap(self)]</c> and a 1..1
///   "creature you control" <see cref="TargetRequest"/>. At resolution
///   (CR 608.2b legality re-check) the chosen creature connives X times via
///   <see cref="Fx.Connive"/>, where X is read from
///   <see cref="TurnState.CreaturesDiedThisTurn"/> (CR 702.104b death-count
///   tracking). Connive itself (CR 701.50): the connived creature's
///   controller draws X then discards X, with a +1/+1 counter per nonland
///   discarded — all handled by <see cref="Majik.Core.Keywords.ConniveAction"/>.
///   X = 0 (no creatures died this turn) is a clean no-op (Fx.Connive
///   guards amount &lt;= 0).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + the connive activated ability,
///   built against a no-death <see cref="TurnState"/> snapshot (X resolves
///   to 0 unless a live <see cref="TurnState"/> is supplied). The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TurnState?)"/> — when a live
///   <see cref="TurnState"/> is supplied, the connive amount tracks the
///   real creatures-died-this-turn count at resolution time (captured by
///   reference so deaths AFTER construction but BEFORE activation count —
///   same captured-TurnState pattern as <see cref="StormscaleScionFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven connive discard pick</b>: <see cref="ConniveAction"/>'s
///   v1 deterministic discard policy (last card in hand) is used; the
///   agent-prompt discard pick is deferred engine-wide (same queue as
///   Faithless Looting / Cathartic Reunion's discard pick).
/// </summary>
[CardName("Spymaster's Vault")]
public static class SpymastersVaultFactory
{
    public const string CardName = "Spymaster's Vault";
    public const string Slug = "spymasters-vault";
    public const string ActivationManaCost = "{B}";

    /// <summary>
    /// Construct Spymaster's Vault with no live <see cref="TurnState"/>.
    /// The connive activated ability is attached; with no death-count source
    /// X resolves to 0 (connive is a no-op). The overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, turnState: null);

    /// <summary>
    /// Construct Spymaster's Vault. When <paramref name="turnState"/> is
    /// supplied, the connive activated ability reads X (creatures died this
    /// turn) from it at resolution time (captured by reference).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="turnState">Live turn state for the connive X count
    /// (CR 702.104b). When null, X resolves to 0.</param>
    public static Land Create(Player owner, TurnState? turnState)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: name, Land, and the
        // {T}: Add {B} mana ability (CR 605.1). The connive activated ability
        // is layered on below (it outgrows the JSON schema).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {B}, {T}: Target creature you control connives X, where X is the
        // number of creatures that died this turn (CR 602 / 701.50 /
        // 702.104b).
        //
        // Cost = {B} mana + tap self. Target = a creature the activating
        // player controls (1..1). At resolution the chosen creature connives
        // X times; X is read live from TurnState.CreaturesDiedThisTurn so a
        // death AFTER construction but BEFORE activation counts (captured by
        // reference — same shape as StormscaleScionFactory's storm count).
        //
        // The effect lambda captures `land` (not `owner`) so live controller
        // tracking via land.Controller picks up control-change effects at
        // resolution time ("creature you control" is read from the current
        // controller).
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;
        var conniveEffect = new Effect(
            $"{CardName}: target creature you control connives X (X = creatures died this turn)",
            () =>
            {
                if (ability == null) return;
                var chosen = ability.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality re-check: the target
                // must still be a creature on the battlefield controlled by
                // the activating player.
                if (target.Zone != ZoneType.Battlefield) return;
                var controller = land.Controller ?? owner;
                if (!ReferenceEquals(target.Controller, controller)) return;

                // X = creatures that died this turn (CR 702.104b). 0 when no
                // TurnState is wired or none died — Fx.Connive no-ops on
                // amount <= 0.
                var x = turnState?.CreaturesDiedThisTurn ?? 0;
                Fx.Connive(target, x);
            });

        ability = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { conniveEffect },
            sorcerySpeed: false,
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // Connive grows the creature (+1/+1 counters) — a buff.
                    Intent: BotIntent.Buff,
                    // "creature you control" — every creature on the
                    // activating player's battlefield (CR 109.5 — "you"
                    // refers to the ability's controller).
                    CandidateGatherer: ctx => (land.Controller ?? owner)
                        .Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        land.AddAbility(ability);

        return land;
    }
}
