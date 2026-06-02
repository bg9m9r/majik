using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nephalia Drownyard (Innistrad and reprints).
///
/// Land. Oracle text (verified against Scryfall 2026-06-01):
///   "{T}: Add {C}.
///    {1}{U}{B}, {T}: Target player mills three cards."
///
/// ## Build path
///
/// Identity + the {T}: Add {C} mana ability are authored in the embedded JSON
/// definition (<c>Majik.Core/CardData/Cards/nephalia-drownyard.json</c>) and
/// materialized through <see cref="CardDefinitionFactory"/> — the same vanilla
/// colorless-land mana shape used by Rogue's Passage / Bonders' Enclave. The
/// targeted "{1}{U}{B}, {T}: Target player mills three cards" activated ability
/// is hand-attached on top because the data-driven
/// <see cref="CardDefinitionFactory"/> does not yet express a targeted-mill
/// effect (no <c>mill_target</c> EffectDefinition; same posture as Rogue's
/// Passage's hand-rolled targeted ability and Restless Reef's targeted-mill
/// attack trigger).
///
/// ## Implemented (v1)
///
/// - <b>{T}: Add {C}</b> — JSON <c>"mana"</c> ability producing {C}.
///   CR 605.1 — mana abilities don't use the stack.
/// - <b>{1}{U}{B}, {T}: Target player mills three cards</b> —
///   <see cref="ActivatedAbility"/> (CR 602.1) with a
///   <see cref="ManaCostCost"/> for the {1}{U}{B} mana component + an
///   <see cref="AdditionalCost"/>.Tap, and a 1..1 "target player"
///   <see cref="TargetRequest"/>. On resolution the factory reads
///   <see cref="ActivatedAbility.ChosenTargets"/>[0][0] and, when the choice
///   is a <see cref="Player"/>, mills <see cref="MillCount"/> cards from them
///   via <see cref="MillAction.Apply"/> (CR 701.13). A short library mills all
///   remaining cards without losing the game (CR 701.13a). When the chosen
///   target token doesn't resolve to a <see cref="Player"/> the ability no-ops
///   per CR 608.2b (illegal target at resolution) — same posture as Rogue's
///   Passage / Restless Reef.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Target legality in ActionValidator</b>: the target list is an
///   unconstrained "target player"; the activator's pick is honoured verbatim
///   and the resolution-time guard handles illegal targets (CR 608.2b). Same
///   posture as Rogue's Passage / Glimpse the Unthinkable.
/// </summary>
[CardName("Nephalia Drownyard")]
public static class NephaliaDrownyardFactory
{
    public const string CardName = "Nephalia Drownyard";
    public const string Slug = "nephalia-drownyard";

    /// <summary>The {1}{U}{B} mana component of the mill ability.</summary>
    public const string ActivationCost = "{1}{U}{B}";

    /// <summary>Number of cards milled from the chosen target player.</summary>
    public const int MillCount = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Nephalia Drownyard. Identity + the {T}: Add {C} mana ability
    /// come from JSON; the {1}{U}{B},{T} targeted-mill activated ability is
    /// hand-attached on top. There is no ETB-tapped clause, so no
    /// <see cref="ReplacementBus"/> overload is needed.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + {T}: Add {C} from the embedded JSON definition.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {1}{U}{B}, {T}: Target player mills three cards.
        // CR 602.1 — ordinary activated ability (uses the stack).
        // CR 701.13 — "mill N" puts the top N cards of the chosen player's
        //   library into their graveyard.
        // CR 608.2b — an illegal (non-Player) target at resolution → no-op.
        // ----------------------------------------------------------------
        ActivatedAbility? millAbility = null;
        var millEffect = new Effect(
            $"{CardName}: target player mills {MillCount} cards",
            () =>
            {
                if (millAbility == null) return;
                if (millAbility.ChosenTargets.Count == 0) return;
                if (millAbility.ChosenTargets[0].Count == 0) return;
                if (millAbility.ChosenTargets[0][0] is not Player target) return;

                // CR 701.13 / 701.13a — mill three; a short library mills all
                // remaining cards without losing the game.
                MillAction.Apply(target, MillCount);
            });

        millAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { millEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(millAbility);

        return land;
    }
}
