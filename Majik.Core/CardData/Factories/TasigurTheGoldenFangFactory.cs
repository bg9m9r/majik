using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tasigur, the Golden Fang (Khans of Tarkir, {4}{B/G}).
///
/// Legendary Creature — Human Shaman 4/5. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    {B}{G}{U}: Target opponent chooses a card in your graveyard. Return
///    that card to your hand. Activate only as a sorcery."
///
/// ## Implemented (v1)
/// - Legendary 4/5 Creature with Human + Shaman subtypes, mana cost
///   {4}{B/G} (hybrid pip parsed via <see cref="ValueObjects.ManaCost"/>'s
///   HybridPip path — same idiom as Boros Reckoner's {R/W} cost).
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
/// - <b>Activate only as a sorcery</b>: CR 117.1a. The engine has no
///   per-activated-ability sorcery-speed gate yet (only spell-casting
///   consults <see cref="Rules.CastingRestrictions"/>). Same deferral
///   pattern as Wishclaw Talisman / Priest of Fell Rites / Walking
///   Ballista — noted in the activated ability comment.
/// - <b>Mana cost {B}{G}{U}</b> on the activated ability is modelled as
///   <see cref="ManaCostCost"/> with the printed cost string; the
///   payment surface itself is handled by the activation flow and the
///   controller's mana pool.
/// - <b>Bot-side <c>IAlternativeCostProbe</c>-style discovery for
///   Delve</b> is not yet wired; the heuristic bot won't proactively
///   delve Tasigur — same gap as Treasure Cruise.
/// </summary>
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
            manaCost: "{4}{B/G}",
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
        // production). CR 117.1a sorcery-speed restriction deferred (see
        // class xmldoc). CR 113.3 — the opponent makes the choice via
        // their agent; controller routes through ChooseLibraryPickAsync
        // (closest existing "agent picks one card from a list" prompt).
        // ----------------------------------------------------------------

        var returnEffect = new Effect(
            "Tasigur: target opponent chooses a card in your graveyard → hand",
            () =>
            {
                var candidates = owner.Zones.Graveyard.GetCards().ToList();
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
                    opponent = all?.FirstOrDefault(p => !ReferenceEquals(p, owner));
                }
                if (opponent == null) return;
                if (ReferenceEquals(opponent, owner)) return;

                // CR 113.3 — the opponent picks. Defer to their agent;
                // fall back to the first card deterministically if no
                // agent is registered (mirrors Wishclaw Talisman's
                // tutor fallback).
                var agent = AgentRegistry.Get(opponent);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates: candidates,
                        kindLabel: "card in opponent's graveyard")
                        .GetAwaiter().GetResult()
                    : candidates[0];

                // CR 113.3 — a player who's required to choose must do
                // so if possible. Defensive null fallback: if the agent
                // returns null but candidates exist, pick the first
                // (matches the heuristic-bot posture used elsewhere).
                pick ??= candidates[0];

                // Move the chosen card from controller's graveyard to
                // controller's hand. Direct-zone mutation mirrors
                // Wrenn-and-Six's +1 lands-from-graveyard return idiom
                // — no ZoneService wiring at this dispatcher path.
                owner.Zones.Graveyard.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{B}{G}{U}"),
            },
            effects: new IEffect[] { returnEffect });

        card.AddAbility(activated);
        return card;
    }
}
