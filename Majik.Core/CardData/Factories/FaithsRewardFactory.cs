using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faith's Reward (Mirrodin Besieged, {3}{W}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Return to the battlefield all permanent cards in your graveyard
///    that were put there from the battlefield this turn."
///
/// White board-wrath-recovery / "Second Sunrise" cousin — reanimates
/// every permanent that the controller LOST from the battlefield this
/// turn. Pairs naturally with white sweeper recoveries (Wrath of God
/// → Faith's Reward = wipe the board, then bring all your stuff back)
/// and the "Sunrise" combo lines that abuse zone-bounce ETBs.
///
/// ## Implemented (v1)
///
/// - Instant card shape ({3}{W}), built via the fluent <see cref="CardDef"/>
///   DSL.
/// - <b>Resolve effect (CR 121.1 / CR 701.20)</b>: scans the live
///   <see cref="TurnState.PermanentsMovedToGraveyardThisTurn"/> ledger
///   for the controller, filters to (a) cards still in the controller's
///   graveyard at resolution (CR 608.2b — state-changed objects), (b)
///   permanent CARDS (Creature / Artifact / Enchantment / Land /
///   Planeswalker / Battle — see CR 110.4a — not Instant / Sorcery, even
///   though instants / sorceries can't be on the battlefield to begin
///   with this filter future-proofs against type-changing effects), and
///   moves each back to the battlefield under the caster's control
///   (CR 110.2 — controller of the returned permanent is whoever brought
///   it back, default to the caster). Each return is routed through
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> — when a
///   <see cref="ZoneService"/> is supplied each returned permanent's ETB
///   publishes <see cref="Majik.Core.Events.CardMovedEvent"/> so
///   downstream ETB triggers (Soul Warden / Mentor of the Meek /
///   Hangarback Walker's PendingCastX is null → 0 counters as expected
///   for a non-cast entry) fire.
/// - <b>TurnState plumbing</b>: this PR adds
///   <see cref="TurnState.RecordPermanentMovedToGraveyard"/> +
///   <see cref="TurnState.PermanentsMovedToGraveyardThisTurn"/>. The
///   <see cref="TurnDriver"/>'s <see cref="Majik.Core.Events.CardMovedEvent"/>
///   subscriber records every Battlefield → Graveyard transition (CR 121
///   — "put there from the battlefield this turn") keyed by the card's
///   OWNER (CR 404.1 — graveyards are owner-scoped; a stolen creature
///   that dies returns to its owner's graveyard and Faith's Reward
///   retrieves it for the owner — not for the temporary controller).
///   Reset at turn start alongside the other per-turn ledgers
///   (<see cref="TurnState.Reset"/>).
///
/// ## Notes
///
/// - <b>"Permanent cards" filter</b>: CR 110.4a — a permanent card is any
///   card whose printed type is Artifact, Battle, Creature, Enchantment,
///   Land, or Planeswalker. The resolution body uses
///   <see cref="ICard.HasType"/> over the five common permanent types
///   (Battle subtype is not yet enumerated in the engine's
///   <see cref="CardType"/> enum — same gap as Force of Despair's
///   battle-type scan). Cards that have lost their permanent typing in
///   the graveyard via continuous effects (extremely rare) are skipped
///   correctly because the printed-type check has already moved with the
///   card.
/// - <b>Controller on return</b>: CR 110.2 — the new controller is the
///   caster of Faith's Reward (NOT the original controller). When the
///   spell brings back a creature that was stolen by an opponent and
///   then died, Faith's Reward's caster (the owner) becomes the new
///   controller — matching the "your graveyard" wording.
///
/// ## Deferred (v1 gaps)
///
/// - <b>No agent prompt</b>: "you may" / target selection are not part
///   of Faith's Reward's printed wording — every qualifying card is
///   returned (no opt-out per-card). No prompt is needed at v1.
/// - <b>Battle subtype scan</b>: same gap as Force of Despair; the
///   engine's <see cref="CardType"/> enum doesn't include Battle yet, so
///   Battle cards aren't scanned. Modern-legal Battles (March of the
///   Machine) aren't typically board-wrathed-and-rezzed, so the gap is
///   strictly future-print scope.
/// - <b>Tokens that ceased to exist</b>: SBA 704.5d says a token in any
///   zone other than the battlefield ceases to exist. By the time
///   Faith's Reward resolves, token "cards" are gone — the live-zone
///   re-check at resolution skips them automatically (a token's Zone
///   isn't Graveyard anymore once SBA runs).
/// </summary>
[CardName("Faith's Reward")]
public static class FaithsRewardFactory
{
    public const string CardName = "Faith's Reward";
    public const string PrintedManaCost = "{3}{W}";

    /// <summary>CardDef DSL — card shape only. The return-all-permanent-
    /// cards-moved-to-graveyard-this-turn body lives in
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Reads the live
    /// <see cref="TurnState"/> via <paramref name="turnStateResolver"/> at
    /// resolution and returns every permanent card from the caster's
    /// graveyard that this turn's BF→Graveyard ledger recorded as
    /// belonging to the caster (CR 121.1 / CR 701.20). No
    /// <see cref="TargetRequest"/>s — printed text scans the caster's
    /// graveyard ledger, no per-card target picks (CR 700.6 — "all
    /// permanent cards" is enumeration, not targeting).
    /// </summary>
    /// <param name="caster">Spell controller — both the ledger key
    /// (owner-scoped, CR 404.1) and the new controller of returned
    /// permanents.</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. When the callback
    /// returns null (no driver wired — typical for shape / dispatcher
    /// tests) the resolve body is a clean no-op rather than degrading
    /// to "return every permanent card" (which would be a different
    /// card — Living Death shape). Production callers pass
    /// <c>() =&gt; turnDriver.TurnState</c>, mirroring
    /// <see cref="ForceOfDespairFactory.BuildSpellDefinition"/>.</param>
    /// <param name="zones">Optional <see cref="ZoneService"/> so the
    /// returned permanents' ETB publishes
    /// <see cref="Majik.Core.Events.CardMovedEvent"/>. Pass null for
    /// raw-zone moves (shape-test path).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<TurnState?> turnStateResolver,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(turnStateResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName} — return all permanent cards in your graveyard that were put there from the battlefield this turn",
                    () => Resolve(caster, turnStateResolver, zones)),
            });
    }

    /// <summary>
    /// Shared resolve helper. Snapshots the per-turn BF→Graveyard ledger
    /// for the caster, filters to permanent cards still in the caster's
    /// graveyard at resolution, and returns each to the battlefield
    /// under the caster's control (CR 110.2 — new controller is the
    /// caster). Snapshot avoids "concurrent modification" mid-iteration
    /// when ETB triggers on a returned permanent mutate the ledger
    /// (e.g. one of the returned creatures' ETB ability sacrifices
    /// another).
    /// </summary>
    private static void Resolve(
        Player caster,
        Func<TurnState?> turnStateResolver,
        ZoneService? zones)
    {
        var turnState = turnStateResolver.Invoke();
        if (turnState == null) return;

        // Snapshot the ledger so ETB-on-return triggers can't reshape
        // the iteration target (CR 608.2c — simultaneous, but we walk
        // in a stable order for determinism).
        var candidates = turnState.PermanentsMovedToGraveyardThisTurn(caster)
            .Where(c => c.Zone == ZoneType.Graveyard)
            .Where(c => IsPermanentCard(c))
            // Filter: must be in CASTER's graveyard (CR 404.1 —
            // "your graveyard"). The ledger is already owner-keyed
            // (RecordPermanentMovedToGraveyard uses Owner.Id), so this
            // is defence-in-depth for shape-test shenanigans.
            .Where(c => ReferenceEquals(c.Owner, caster))
            .ToList();

        foreach (var card in candidates)
        {
            // CR 110.2 / CR 701.20 — return to battlefield under the
            // caster's control. ZoneService-routed when supplied so
            // ETB triggers on the returned permanent fire.
            Fx.ReturnFromGraveyardToBattlefield(card, caster, zones);
        }
    }

    /// <summary>
    /// CR 110.4a — a permanent card has one of the permanent card types
    /// (Artifact / Battle / Creature / Enchantment / Land / Planeswalker).
    /// v1 scans the five enum-represented types; Battle is deferred
    /// alongside the engine-wide Battle support (same gap as
    /// <see cref="ForceOfDespairFactory"/>).
    /// </summary>
    public static bool IsPermanentCard(ICard card) =>
        card.HasType(CardType.Creature)
        || card.HasType(CardType.Artifact)
        || card.HasType(CardType.Enchantment)
        || card.HasType(CardType.Land)
        || card.HasType(CardType.Planeswalker);
}
