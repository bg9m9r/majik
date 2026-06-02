using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Stone Brain (Kaldheim, {2}).
///
/// Legendary Artifact. Oracle text (verified against Scryfall 2026-06-02):
///   "{2}, {T}, Exile The Stone Brain: Choose a card name. Search target
///    opponent's graveyard, hand, and library for up to four cards with that
///    name and exile them. That player shuffles, then draws a card for each
///    card exiled from their hand this way. Activate only as a sorcery."
///
/// The card's base shape (name, Legendary supertype, Artifact card type, {2})
/// is materialised from the embedded JSON definition (<c>the-stone-brain.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/> / <see cref="GuardianIdolFactory"/>. The
/// {2}, {T}, Exile-self name-choose-and-search ability is layered on here
/// because the JSON schema doesn't express search / exile / draw effects.
///
/// ## Implemented (v1)
/// - Card identity (Legendary Artifact, mana cost {2}, owner / controller).
/// - <b>{2}, {T}, Exile ~: name-choose graveyard/hand/library exile + draw</b>
///   — single sorcery-speed <see cref="ActivatedAbility"/>
///   (<c>sorcerySpeed: true</c> ⇒ CR 117.1a / 307.5 "Activate only as a
///   sorcery") with two structural costs: a <see cref="ManaCostCost"/>("{2}")
///   and <see cref="AdditionalCost.Tap"/> on the artifact. The
///   <b>Exile The Stone Brain</b> portion of the cost is performed inside the
///   resolve effect (battlefield → owner's exile) rather than as a cost
///   primitive — the engine has no generic "exile this permanent as a cost"
///   <see cref="AdditionalCost"/> yet, identical posture to Renegade Map /
///   Expedition Map performing their sacrifice in the effect closure.
/// - <b>Resolution</b> (CR 701.19 search / 701.20a shuffle / 120 draw):
///   sweeps the target opponent's graveyard, hand, and library for up to four
///   cards whose name matches the chosen name (case-insensitive), exiles them,
///   shuffles that player's library, then the target opponent draws one card
///   for each card exiled <i>from their hand</i> this way. Self-exile of The
///   Stone Brain happens first so it is gone before the search resolves.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent "choose a card name" prompt</b>:
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> doesn't yet declare a
///   ChooseCardName prompt (same gap noted on Meddling Mage / Pithing Needle).
///   Until that lands, the chosen name + target opponent are supplied to the
///   <see cref="Create(Player, Player, string)"/> overload; the shape-only
///   <see cref="Create(Player)"/> attaches the ability structurally with no
///   live target/name (its resolution is a no-op beyond the self-exile guard).
/// - <b>Opponent-targeting via TargetRequest</b>: the target opponent is
///   supplied directly rather than chosen through a player
///   <see cref="Majik.Core.Targeting.TargetRequest"/> (no player-target
///   request surface on activated abilities yet — same posture as other
///   opponent-targeting factories that capture the target in the closure).
/// - <b>Exile-as-cost primitive</b>: see the cost note above; remove the
///   in-effect self-exile once a generic exile-self <see cref="AdditionalCost"/>
///   lands.
/// </summary>
[CardName("The Stone Brain")]
public static class TheStoneBrainFactory
{
    public const string CardName = "The Stone Brain";
    public const string Slug = "the-stone-brain";

    /// <summary>The {2} mana pip of the activated ability's printed cost.</summary>
    public const string ActivationManaCost = "{2}";

    /// <summary>"up to four cards with that name" — the per-name exile cap.</summary>
    public const int ExileCap = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct The Stone Brain owned and controlled by
    /// <paramref name="owner"/> with the activated ability attached
    /// structurally but no live target opponent / chosen name. Suitable for
    /// card-shape / dispatcher tests; resolving the ability only performs the
    /// self-exile cost portion (no search runs without a target + name).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, targetOpponent: null, chosenName: null);

    /// <summary>
    /// Construct The Stone Brain with a live <paramref name="targetOpponent"/>
    /// and <paramref name="chosenName"/>. Resolving the single activated
    /// ability exiles up to four cards named <paramref name="chosenName"/> from
    /// the target opponent's graveyard, hand, and library; shuffles that
    /// player's library; then the opponent draws one card per card exiled from
    /// their hand this way. The Stone Brain exiles itself first (cost portion).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetOpponent">The opponent whose zones are searched. Null
    /// on the shape-only path — resolution then performs only the self-exile.</param>
    /// <param name="chosenName">The chosen card name. Null/empty on the
    /// shape-only path — resolution then exiles nothing by name.</param>
    public static Artifact Create(
        Player owner,
        Player? targetOpponent,
        string? chosenName)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Legendary, Artifact, {2}) from the embedded JSON.
        var brain = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        brain.SetOwner(owner);
        brain.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Exile The Stone Brain: Choose a card name. Search target
        // opponent's graveyard, hand, and library for up to four cards with
        // that name and exile them. That player shuffles, then draws a card
        // for each card exiled from their hand this way.
        // Activate only as a sorcery.
        //
        // CR 117.1a / 307.5 — sorcery-speed rider (sorcerySpeed: true).
        // CR 602 — ordinary activated ability; mana ({2}) + tap costs.
        // Exile-self is performed in the effect (no exile-as-cost primitive).
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: exile up to {ExileCap} '{chosenName}' from target opponent's GY/hand/library + that player draws per hand-exile",
            () =>
            {
                // Self-exile (cost portion): The Stone Brain → owner's exile.
                // Performed first so it is gone before the search resolves.
                ExileSelf(brain, owner);

                if (targetOpponent == null || string.IsNullOrEmpty(chosenName))
                {
                    return; // shape-only path — no search target / name.
                }

                // CR 701.19 — sweep the target opponent's graveyard, hand, and
                // library for up to four cards with the chosen name. Order is
                // deterministic (graveyard, hand, library) and capped at four
                // across all three zones — "up to four cards with that name".
                var sweep = new List<ICard>();
                AddMatches(sweep, targetOpponent.Zones.Graveyard.GetCards(), chosenName);
                AddMatches(sweep, targetOpponent.Zones.Hand.GetCards(), chosenName);
                AddMatches(sweep, targetOpponent.Zones.Library.GetCards(), chosenName);

                var toExile = sweep.Take(ExileCap).ToList();

                var handExiled = 0;
                foreach (var card in toExile)
                {
                    if (card.Zone == ZoneType.Hand)
                    {
                        handExiled++;
                    }
                    MoveToExile(targetOpponent, card);
                }

                // CR 701.20a — that player shuffles (the search happened).
                LibraryShuffle.ShuffleLibrary(targetOpponent, Slug);

                // CR 120 — "then draws a card for each card exiled from their
                // hand this way." The TARGET OPPONENT draws.
                if (handExiled > 0)
                {
                    Fx.DrawCards(targetOpponent, handExiled);
                }
            });

        var ability = new ActivatedAbility(
            source: brain,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(brain),
            },
            effects: new IEffect[] { effect },
            sorcerySpeed: true);

        brain.AddAbility(ability);

        return brain;
    }

    private static void AddMatches(List<ICard> sweep, IEnumerable<ICard> source, string name)
    {
        sweep.AddRange(source.Where(
            c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Move <paramref name="card"/> from its current zone (owned by
    /// <paramref name="owner"/>) to that owner's exile. Mirrors Extirpate's
    /// per-card exile move.
    /// </summary>
    private static void MoveToExile(Player owner, ICard card)
    {
        switch (card.Zone)
        {
            case ZoneType.Graveyard:
                owner.Zones.Graveyard.RemoveCard(card);
                break;
            case ZoneType.Hand:
                owner.Zones.Hand.RemoveCard(card);
                break;
            case ZoneType.Library:
                owner.Zones.Library.RemoveCard(card);
                break;
        }
        owner.Zones.Exile.AddCard(card);
        card.SetZone(ZoneType.Exile);
    }

    /// <summary>
    /// Exile The Stone Brain itself (cost portion). Battlefield → owner's
    /// exile. Idempotent — same posture as Renegade Map's SacrificeSelf.
    /// </summary>
    private static void ExileSelf(Artifact brain, Player owner)
    {
        if (brain.Zone != ZoneType.Battlefield) return;
        var holder = brain.Controller ?? owner;
        holder.Zones.Battlefield.RemoveCard(brain);
        owner.Zones.Exile.AddCard(brain);
        brain.SetZone(ZoneType.Exile);
    }
}
