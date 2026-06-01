using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Stone Brain (March of the Machine: The
/// Aftermath, {2}).
///
/// Legendary Artifact. Oracle text (verified against Scryfall):
///   "{2}, {T}, Exile The Stone Brain: Choose a card name. Search target
///    opponent's graveyard, hand, and library for up to four cards with
///    that name and exile them. That player shuffles, then draws a card
///    for each card exiled from their hand this way. Activate only as a
///    sorcery."
///
/// The card's base shape (name, Legendary Artifact, {2}) is materialised
/// from the embedded JSON definition (<c>the-stone-brain.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/>. The {2}, {T}, Exile-self
/// name-search-and-exile ability is layered on here because the JSON schema
/// doesn't express multi-zone name searches.
///
/// ## Implemented (v1)
/// - Card identity (Legendary Artifact, mana cost {2}, owner / controller
///   wiring) — Legendary supertype flows from the JSON definition.
/// - <b>{2}, {T}, Exile The Stone Brain: choose a name; search target
///   player's graveyard, hand, and library; exile up to four matches; that
///   player shuffles; draws one per card exiled from their hand</b> — single
///   <see cref="ActivatedAbility"/> with two costs: <see cref="ManaCostCost"/>
///   ("{2}") + <see cref="AdditionalCost.Tap"/> on the Brain. The "Exile The
///   Stone Brain" portion of the cost is performed by the effect closure
///   (battlefield → exile, CR 701.18a) — same posture as
///   <see cref="NihilSpellbombFactory"/> / <see cref="ExpeditionMapFactory"/>
///   self-sacrifice, since the generic <see cref="AdditionalCost"/> payment is
///   a no-op stub. A 1..1 TargetRequest for "target player" is declared
///   (CR 115.1). On resolution the target player is read from
///   <see cref="ActivatedAbility.ChosenTargets"/> (falls back to the first
///   non-controller seat passed at construction, then to the controller).
/// - <b>Sorcery-speed restriction (CR 601.3e / 117.1a)</b> — "Activate only
///   as a sorcery" is wired via <c>sorcerySpeed: true</c> on the activated
///   ability, mirroring <see cref="WishclawTalismanFactory"/>.
/// - <b>Multi-zone name search + exile (CR 701.19)</b> — the chosen name is
///   matched (ordinal, case-sensitive) across the target player's graveyard,
///   hand, and library, capped at four total matches (CR's "up to four"). Each
///   match is moved to that player's exile zone. The count of matches taken
///   from the HAND is tracked separately.
/// - <b>That player shuffles, then draws (CR 701.20a)</b> — after the exile
///   the target player shuffles their library once (whether or not anything
///   was found), then draws one card for each card exiled from their hand this
///   way, via <see cref="Fx.DrawCards"/> (CR 614 replacement-aware).
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-a-card-name prompt</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't surface a card-name picker yet — same queue as Pithing Needle /
///   Cabal Therapy / Cavern of Souls. The caller supplies a
///   <c>Func&lt;Player,string?&gt;</c> selector; a null / empty name matches
///   nothing (defensive — no exiles rather than sweeping nameless cards).
/// - <b>"target opponent" gate</b>: the printed text restricts the target to
///   an opponent. v1 declares a generic "target player" TargetRequest and
///   does not enforce the opponent-only constraint at the ability level (same
///   deferral posture as Nihil Spellbomb's "target player"). The target is
///   read from <see cref="ActivatedAbility.ChosenTargets"/>.
/// - <b>Exile-as-cost side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> has no Exile member, so the effect closure
///   performs the battlefield → exile move directly (same observable posture
///   as Nihil Spellbomb / Expedition Map self-sacrifice).
/// - <b>Search reveal</b>: the exiled cards move zones without publishing a
///   per-card reveal event. Same gap as every tutor/search factory.
/// </summary>
[CardName("The Stone Brain")]
public static class TheStoneBrainFactory
{
    public const string CardName = "The Stone Brain";
    public const string Slug = "the-stone-brain";

    /// <summary>Oracle "up to four cards" cap (CR 701.19).</summary>
    private const int MaxMatches = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct The Stone Brain owned and controlled by
    /// <paramref name="owner"/> with no name selector / target wired —
    /// suitable for card-shape and dispatcher tests. Choosing a name will
    /// resolve to null (matches nothing) and the target falls back to the
    /// controller.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, nameSelector: null, targetPlayer: null);

    /// <summary>
    /// Construct The Stone Brain with its activated ability fully wired.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="nameSelector">Resolves the chosen card name at resolution
    /// time (CR 701.19). Called with the Brain's controller. Returning null /
    /// empty matches nothing.</param>
    /// <param name="targetPlayer">Fallback target player used when the ability
    /// resolves without a chosen target on <see cref="ActivatedAbility.ChosenTargets"/>
    /// (e.g. direct test invocation). May be null — falls back to the
    /// controller.</param>
    public static Artifact Create(
        Player owner,
        Func<Player, string?>? nameSelector,
        Player? targetPlayer)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Legendary Artifact, {2}) from the embedded JSON.
        var brain = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        brain.SetOwner(owner);
        brain.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Exile The Stone Brain:
        //   Choose a card name. Search target opponent's graveyard, hand,
        //   and library for up to four cards with that name and exile them.
        //   That player shuffles, then draws a card for each card exiled
        //   from their hand this way. Activate only as a sorcery.
        //
        // CR 602 — activated ability. Cost: {2} (mana) + tap + exile-self.
        // CR 601.3e / 117.1a — sorcery-speed (sorcerySpeed: true below).
        // CR 115.1 — 1..1 TargetRequest "target player".
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;
        var effect = new Effect(
            "The Stone Brain: choose a name; exile up to four matches from " +
            "target player's graveyard/hand/library; shuffle; draw per hand exile",
            () =>
            {
                // "Exile The Stone Brain" — CR 701.18a. The generic
                // AdditionalCost has no Exile member, so perform the move
                // here (same posture as Nihil Spellbomb self-sacrifice).
                ExileSelf(brain, owner);

                // Resolve the target player from ChosenTargets; fall back to
                // the construction-time target, then to the controller.
                var victim = ResolveTargetPlayer(ability, targetPlayer, owner);

                // CR 701.19 — "Choose a card name." A null / empty name
                // matches nothing (defensive guard so a missing selector
                // doesn't sweep nameless cards), but the player still
                // shuffles below (the search still happened).
                var chosenName = nameSelector?.Invoke(brain.Controller ?? owner);

                int handExiles = 0;
                if (!string.IsNullOrEmpty(chosenName))
                {
                    handExiles = ExileUpToFourMatches(victim, chosenName);
                }

                // CR 701.20a — "That player shuffles" once, regardless of
                // how many cards were found.
                LibraryShuffle.ShuffleLibrary(victim, Slug);

                // "then draws a card for each card exiled from their hand
                // this way." CR 614-aware draw via Fx.DrawCards.
                if (handExiles > 0)
                {
                    Fx.DrawCards(victim, handExiles);
                }
            });

        ability = new ActivatedAbility(
            source: brain,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(brain),
            },
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            // CR 601.3e / 117.1a — "Activate only as a sorcery."
            sorcerySpeed: true);

        brain.AddAbility(ability);

        return brain;
    }

    /// <summary>
    /// Resolve the target player. Prefer the chosen target on
    /// <paramref name="ability"/>; fall back to the construction-time
    /// <paramref name="fallback"/>; finally to <paramref name="owner"/>.
    /// </summary>
    private static Player ResolveTargetPlayer(
        ActivatedAbility? ability, Player? fallback, Player owner)
    {
        if (ability != null
            && ability.ChosenTargets.Count > 0
            && ability.ChosenTargets[0].Count > 0
            && ability.ChosenTargets[0][0] is Player chosen)
        {
            return chosen;
        }

        return fallback ?? owner;
    }

    /// <summary>
    /// CR 701.19 — search the target player's graveyard, hand, and library
    /// for cards whose name matches <paramref name="chosenName"/> (ordinal),
    /// capped at four total matches ("up to four"), and exile each. Returns
    /// the number of matches that came from the player's HAND (used to size
    /// the subsequent draw). Hand is searched before library so the
    /// hand-exile count is maximised within the four-card cap, matching the
    /// printed intent (the draw replaces cards stripped from the hand).
    /// </summary>
    private static int ExileUpToFourMatches(Player victim, string chosenName)
    {
        bool Matches(ICard c) =>
            string.Equals(c.Name, chosenName, StringComparison.Ordinal);

        // Order zones graveyard → hand → library. The four-card cap spans
        // all three zones combined (CR 701.19 "up to four cards ... and
        // exile them"). Snapshot each zone before mutating it.
        var graveyard = victim.Zones.Graveyard.GetCards().Where(Matches).ToList();
        var hand = victim.Zones.Hand.GetCards().Where(Matches).ToList();
        var library = victim.Zones.Library.GetCards().Where(Matches).ToList();

        int remaining = MaxMatches;
        int handExiles = 0;

        remaining -= ExileFrom(victim, victim.Zones.Graveyard, graveyard, remaining);

        int handTaken = ExileFrom(victim, victim.Zones.Hand, hand, remaining);
        handExiles += handTaken;
        remaining -= handTaken;

        ExileFrom(victim, victim.Zones.Library, library, remaining);

        return handExiles;
    }

    /// <summary>
    /// Move up to <paramref name="limit"/> cards from <paramref name="source"/>
    /// to <paramref name="victim"/>'s exile zone. Returns the number moved.
    /// </summary>
    private static int ExileFrom(
        Player victim, IZone source, IReadOnlyList<ICard> cards, int limit)
    {
        if (limit <= 0) return 0;

        int taken = 0;
        foreach (var card in cards)
        {
            if (taken >= limit) break;
            source.RemoveCard(card);
            victim.Zones.Exile.AddCard(card);
            card.SetZone(ZoneType.Exile);
            taken++;
        }

        return taken;
    }

    /// <summary>
    /// CR 701.18a — move <paramref name="brain"/> from the battlefield to its
    /// owner's exile zone (the "Exile The Stone Brain" cost). Idempotent —
    /// no-op if the card is already off the battlefield.
    /// </summary>
    private static void ExileSelf(Artifact brain, Player owner)
    {
        if (brain.Zone != ZoneType.Battlefield) return;
        var controller = brain.Controller ?? owner;
        controller.Zones.Battlefield.RemoveCard(brain);
        owner.Zones.Exile.AddCard(brain);
        brain.SetZone(ZoneType.Exile);
    }
}
