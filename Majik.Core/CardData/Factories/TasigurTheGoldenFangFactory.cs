using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tasigur, the Golden Fang (Khans of Tarkir, {5}{B}).
///
/// Legendary Creature — Human Shaman 4/5. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    {B}{G}{U}: Target opponent chooses a card in your graveyard. Return
///    that card to your hand. Activate only as a sorcery."
///
/// ## Implemented (v1)
/// - Legendary 4/5 Creature with Human + Shaman subtypes, printed mana
///   cost {5}{B}. (Delve reduces what you PAY at cast time per CR 702.66 —
///   it does not change the printed cost / mana value, which stays MV 6.)
/// - "Delve" marker keyword via <see cref="KeywordAbility"/> so downstream
///   code (UI, bot probes, action validator) can introspect the keyword.
///   The actual Delve mechanic (CR 702.66) lives in
///   <see cref="Costs.DelveCost"/> + <see cref="Game.SpellCastFlow"/>;
///   callers cast Tasigur via the cast-flow's <c>delveCost</c> parameter
///   when they want to substitute graveyard exiles for generic mana —
///   same wire-up as Treasure Cruise / Dig Through Time / Murktide Regent.
/// - Activated ability {B}{G}{U}: an opponent (chosen via
///   <paramref name="opponentChooser"/>; defaults to the first non-
///   controller in <paramref name="allPlayersResolver"/>) selects a card
///   from the controller's graveyard, which is then returned to the
///   controller's hand. The opponent's <see cref="IPlayerAgent"/> is
///   consulted via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///   (CR 113.3 — choices made by a player), labelled "card in opponent's
///   graveyard". Empty graveyard → clean no-op.
///
/// ## Deferred (v1 gaps)
/// (The activate-as-sorcery timing window for the {B}{G}{U} ability is
/// now enforced via the ActionValidator gate; see "Implemented" above.)
/// - <b>Mana cost {B}{G}{U}</b> on the activated ability is modelled as
///   <see cref="ManaCostCost"/> with the printed cost string; the
///   payment surface itself is handled by the activation flow and the
///   controller's mana pool.
/// - <b>Bot-side delve discovery</b>: now wired via
///   <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/>, which
///   surfaces Tasigur's Delve <see cref="KeywordAbility"/> marker to the
///   heuristic bot's
///   <see cref="Majik.Core.Players.Agents.IAlternativeCostProbe"/> stream
///   as a <see cref="Majik.Core.Costs.DelveAlternativeCost"/>.
/// </summary>
[CardName("Tasigur, the Golden Fang")]
public static class TasigurTheGoldenFangFactory
{
    public const string CardName = "Tasigur, the Golden Fang";

    /// <summary>
    /// Construct Tasigur with no all-players resolver (test / vanilla
    /// path). The activated ability's "target opponent chooses" effect
    /// is a no-op in this mode (no opponent is reachable from a single-
    /// player view) — the card shape + activated ability + Delve marker
    /// are all present.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, allPlayersResolver: null, opponentChooser: null);

    /// <summary>
    /// Construct Tasigur fully wired against a runtime all-players
    /// resolver. When <paramref name="opponentChooser"/> is supplied,
    /// that opponent is asked to pick a card from the controller's
    /// graveyard; otherwise the first non-controller in
    /// <paramref name="allPlayersResolver"/> is used (deterministic
    /// auto-pick — same posture as Yawgmoth's opponent iteration).
    /// </summary>
    /// <param name="owner">Owner and initial controller of the card.</param>
    /// <param name="allPlayersResolver">
    /// Called at ability resolution time to obtain the list of all players
    /// (including the controller). May be null — the opponent-choose
    /// effect silently no-ops.
    /// </param>
    /// <param name="opponentChooser">
    /// Optional chooser for which opponent is the "target opponent".
    /// Null = first non-controller in the resolver's list.
    /// </param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        Func<Player>? opponentChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{5}{B}",
            power: 4,
            toughness: 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.66 — Delve marker. The behavior itself is in DelveCost +
        // SpellCastFlow; the marker is here so introspection (UI, bots)
        // can see the keyword on the card. Same pattern as Treasure
        // Cruise / Dig Through Time / Murktide Regent.
        card.AddAbility(new KeywordAbility("Delve", card, owner));

        // ----------------------------------------------------------------
        // Activated ability:
        //   {B}{G}{U}: Target opponent chooses a card in your graveyard.
        //   Return that card to your hand. Activate only as a sorcery.
        //
        // CR 605 — not a mana ability (effect is zone movement, not mana
        // production). CR 117.1a / 307.5 — sorcery-speed restriction
        // enforced via ActionValidator (sorcerySpeed: true on the
        // activated ability below). CR 113.3 — the opponent makes the
        // choice via their agent; controller routes through
        // ChooseLibraryPickAsync (closest existing "agent picks one card
        // from a list" prompt).
        // ----------------------------------------------------------------

        // RE-SOURCE-SAFE (agatha-bespoke-source-migration-creature-tail-batch):
        // "your graveyard" / "your hand" resolve to the live
        // ResolutionContext.Source's CONTROLLER (this ability's own source at
        // resolution) rather than the captured `owner`, falling back to `owner`
        // only on the context-less legacy sync path (ResolutionContext.Legacy,
        // Source = null). Marked RebindSafe so Agatha's Soul Cauldron re-homes this
        // REAL "{B}{G}{U}: target opponent picks a card from your graveyard → your
        // hand" ability to a counter-bearing bearer via ActivatedAbility.RebindTo
        // (CR 707.2 / 613.1f): the return reads the BEARER'S controller's
        // graveyard / hand, never re-reading the exiled Tasigur. The activated
        // ability's mana cost carries no captured source; the "opponent chooses
        // from your graveyard → your hand" shape is OUTSIDE the
        // OracleActivatedAbilityBinder reconstructable set, so RebindTo of the real
        // ability is the only sound re-home.
        var returnEffect = new Effect(
            "Tasigur: target opponent chooses a card in your graveyard → hand",
            async ctx =>
            {
                var you = ctx.Source?.Controller ?? card.Controller ?? owner;
                var candidates = you.Zones.Graveyard.GetCards().ToList();
                if (candidates.Count == 0) return;

                // Resolve "target opponent". If no resolver is supplied
                // we can't reach an opponent — clean no-op (single-player
                // test/vanilla path).
                Player? opponent = null;
                if (opponentChooser != null)
                {
                    opponent = opponentChooser();
                }
                else if (allPlayersResolver != null)
                {
                    var all = allPlayersResolver();
                    opponent = all?.FirstOrDefault(p => !ReferenceEquals(p, you));
                }
                if (opponent == null) return;
                if (ReferenceEquals(opponent, you)) return;

                // CR 113.3 — the opponent picks. Defer to their agent;
                // fall back to the first card deterministically if no
                // agent is registered (mirrors Wishclaw Talisman's
                // tutor fallback).
                var agent = ctx.Agent ?? AgentRegistry.Get(opponent);
                ICard? pick = agent != null
                    ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                        candidates: candidates,
                        kindLabel: "card in opponent's graveyard").ConfigureAwait(false))
                    : candidates[0];

                // CR 113.3 — a player who's required to choose must do
                // so if possible. Defensive null fallback: if the agent
                // returns null but candidates exist, pick the first
                // (matches the heuristic-bot posture used elsewhere).
                pick ??= candidates[0];

                // Move the chosen card from "your" graveyard to "your"
                // hand (you = the live source's controller). Direct-zone
                // mutation mirrors Wrenn-and-Six's +1 lands-from-graveyard
                // return idiom — no ZoneService wiring at this dispatcher path.
                you.Zones.Graveyard.RemoveCard(pick);
                you.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{B}{G}{U}"),
            },
            effects: new IEffect[] { returnEffect },
            sorcerySpeed: true,
            rebindSafe: true);

        card.AddAbility(activated);
        return card;
    }
}
