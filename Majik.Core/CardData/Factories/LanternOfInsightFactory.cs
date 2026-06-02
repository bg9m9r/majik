using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lantern of Insight (Fifth Dawn, {1}).
///
/// Artifact. Oracle text (verified against Scryfall + the embedded pool):
///   "Players play with the top card of their libraries revealed.
///    {T}, Sacrifice this artifact: Target player shuffles."
///
/// The card's base shape (name, single Artifact card type, {1}) is materialised
/// from the embedded JSON definition (<c>lantern-of-insight.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/> / <see cref="ExpeditionMapFactory"/>. The
/// top-card-revealed static rider and the {T}, Sacrifice shuffle ability are
/// layered on here because the JSON effect schema doesn't express either shape.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>"Players play with the top card of their libraries revealed."</b> —
///   a description-only <see cref="StaticAbility"/> (CR 604.1 — functions while
///   on the battlefield). Same posture as <see cref="ConspicuousSnoopFactory"/>'s
///   "Play with the top card of your library revealed" rider: the engine has no
///   live public top-of-library reveal broadcast through the event/agent layer,
///   so the static is wired for audit / bot-surface visibility and the
///   <see cref="LookAtTopOfLibrary"/> helper exposes the per-player peek. Lantern
///   reveals the top card of <i>every</i> player's library (not just the
///   controller's), which the description records.
/// - <b>{T}, Sacrifice this artifact: Target player shuffles</b> — single
///   <see cref="ActivatedAbility"/> (CR 605 — not a mana ability; goes on the
///   stack) with two costs: <see cref="AdditionalCost.Tap"/> +
///   <see cref="AdditionalCost.Sacrifice"/> on the artifact itself, and no mana
///   pip in the printed cost. A 1..1 "target player" <see cref="TargetRequest"/>
///   is declared. On resolution the artifact is sacrificed
///   (Battlefield → owner's graveyard, CR 701.16) and the chosen player shuffles
///   their library once via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20). The target player is read off
///   <see cref="ActivatedAbility.ChosenTargets"/>; v1 falls back to the
///   controller when no target was set (same deterministic posture as
///   <see cref="NihilSpellbombFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Top-of-library revealed (public window)</b>: the static is
///   description-only — opponents' agents don't see each player's revealed top
///   card as a live public reveal, only the controller-side
///   <see cref="LookAtTopOfLibrary"/> peek works. Same gap as Conspicuous Snoop /
///   Future Sight / Dark Confidant — no continuous public top-card broadcast
///   exists yet.
/// - <b>Target player prompt</b>: v1 reads <c>ChosenTargets[0][0]</c> and falls
///   back to the controller when unset (same posture as Nihil Spellbomb).
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub, so the
///   effect closure performs the Battlefield → Graveyard move directly — same
///   posture as Renegade Map / Nihil Spellbomb / Pyrite Spellbomb.
/// </summary>
[CardName("Lantern of Insight")]
public static class LanternOfInsightFactory
{
    public const string CardName = "Lantern of Insight";
    public const string Slug = "lantern-of-insight";

    public const string TopRevealedDescription =
        "Players play with the top card of their libraries revealed.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Lantern of Insight owned and controlled by
    /// <paramref name="owner"/>. The top-card-revealed static rider and the
    /// "{T}, Sacrifice: target player shuffles" activated ability are attached
    /// structurally.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {1}) from the embedded JSON definition.
        var lantern = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        lantern.SetOwner(owner);
        lantern.SetController(owner);

        // ----------------------------------------------------------------
        // "Players play with the top card of their libraries revealed."
        // CR 604.1 — static ability, functions while on the battlefield.
        // Description-only (no live public-reveal broadcast — see class doc).
        // ----------------------------------------------------------------
        lantern.AddAbility(new StaticAbility(
            source: lantern,
            controller: owner,
            description: TopRevealedDescription));

        // ----------------------------------------------------------------
        // {T}, Sacrifice this artifact: Target player shuffles.
        // CR 605 — not a mana ability (goes on the stack).
        // Cost: tap + self-sacrifice (Battlefield → owner's graveyard).
        // Target: 1..1 TargetRequest "target player".
        // On resolve: sacrifice self (AdditionalCost.Pay is a stub — the
        // closure performs the move), then the chosen player shuffles once
        // (CR 701.20). Falls back to the controller when no target was set.
        // ----------------------------------------------------------------
        ActivatedAbility? shuffleAbility = null;
        var shuffleEffect = new Effect(
            $"{CardName}: target player shuffles + sac self",
            () =>
            {
                var controller = lantern.Controller ?? owner;
                SacrificeSelf(lantern, owner, controller);

                Player targetPlayer = controller;
                if (shuffleAbility != null
                    && shuffleAbility.ChosenTargets.Count > 0
                    && shuffleAbility.ChosenTargets[0].Count > 0
                    && shuffleAbility.ChosenTargets[0][0] is Player chosenPlayer)
                {
                    targetPlayer = chosenPlayer;
                }

                // CR 701.20 — the target player shuffles their library.
                LibraryShuffle.ShuffleLibrary(targetPlayer, Slug);
            });

        shuffleAbility = new ActivatedAbility(
            source: lantern,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(lantern),
                AdditionalCost.Sacrifice(lantern),
            },
            effects: new IEffect[] { shuffleEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        lantern.AddAbility(shuffleAbility);

        return lantern;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="lantern"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors Renegade Map / Nihil Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact lantern, Player owner, Player controller)
    {
        if (lantern.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(lantern);
        owner.Zones.Graveyard.AddCard(lantern);
        lantern.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Helper exposing Lantern's "play with the top card revealed" rider as a
    /// per-player peek. Returns the top card of <paramref name="player"/>'s
    /// library, or null when the library is empty. Pure read — no zone
    /// mutation, no event publish. Mirrors
    /// <see cref="ConspicuousSnoopFactory.LookAtTopOfLibrary"/>.
    /// </summary>
    public static ICard? LookAtTopOfLibrary(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Zones.Library.GetCards().FirstOrDefault();
    }
}
