using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eye of Ugin (Worldwake).
///
/// Legendary Land. Oracle text:
///   "Colorless Eldrazi spells you cast cost {2} less to cast.
///    {7}, {T}: Search your library for a colorless creature card,
///    reveal it, put it into your hand, then shuffle."
///
/// ## Implemented (v1)
/// - Legendary Land identity (no printed subtypes).
/// - <b>Static cost reducer</b>: a <see cref="SpellCostReductionAbility"/>
///   on the land. The predicate matches spells with
///   <see cref="CardSubtype.Eldrazi"/> AND no coloured pips
///   (<see cref="CardColors.GetColors"/> returns an empty set). The
///   reduction is a flat 2 generic.
///   <see cref="CostReduction.GetEffectiveCost"/> scans the caster's
///   battlefield for this ability shape, so the "you cast" scope is
///   enforced by the cost-calc helper. Coloured pips on the spell are
///   untouched (CR 117.7c) and the floor-at-zero clamp is shared with
///   the other reducers.
/// - <b>{7}, {T}: tutor a colorless creature → hand, then shuffle</b> —
///   wired as an <see cref="ActivatedAbility"/> with
///   <c>ManaCostCost("{7}") + AdditionalCost.Tap(land)</c>. Resolution
///   asks the controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for a colorless
///   creature card from the controller's library; the pick is placed
///   into the controller's hand, then the library is shuffled
///   (CR 701.20a). Mirrors Stoneforge Mystic / Ranger-Captain of Eos's
///   tutor posture (deterministic first-eligible-card fallback when no
///   agent is registered). Empty candidate list = clean no-op shuffle
///   (CR 701.19a permits declining to find; the shuffle still happens
///   since the printed oracle's "then shuffle" runs regardless).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the tutor moves the picked card from Library
///   → Hand without publishing a reveal event; same gap as every other
///   tutor factory (Mystical Tutor, Stoneforge Mystic, …).
/// - <b>Eldrazi-cost-of-abilities rider</b>: the printed text reads
///   "Colorless Eldrazi spells you cast cost {2} less to cast." — only
///   SPELLS, not activated abilities of Eldrazi. This is the engine's
///   correct surface to model (no cost-of-abilities reducer surface
///   today); the SpellCostReductionAbility already scopes correctly.
/// </summary>
[CardName("Eye of Ugin")]
public static class EyeOfUginFactory
{
    public const string CardName = "Eye of Ugin";
    public const string TutorActivationCost = "{7}";

    /// <summary>
    /// Construct an Eye of Ugin owned and controlled by
    /// <paramref name="owner"/>. Wires both the static cost-reducer and
    /// the activated tutor ability.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Static: "Colorless Eldrazi spells you cast cost {2} less to cast."
        // CR 117.7 — generic-mana reduction; coloured pips untouched
        // (CR 117.7c). The "you cast" scope is enforced by
        // CostReduction.GetEffectiveCost scanning the caster's
        // battlefield for the reducer.
        // ----------------------------------------------------------------
        land.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasSubtype(CardSubtype.Eldrazi)
                            && CardColors.GetColors(c).Count == 0,
            reduction: (_, _) => 2,
            description: "Colorless Eldrazi spells you cast cost {2} less to cast."));

        // ----------------------------------------------------------------
        // {7}, {T}: Search your library for a colorless creature card,
        //   reveal it, put it into your hand, then shuffle.
        // CR 602 — activated ability with mana + tap costs.
        // CR 701.19a — declining / no candidate is legal; the printed
        // "then shuffle" runs regardless (CR 701.20a).
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: tutor a colorless creature card → hand; shuffle",
            () =>
            {
                static bool Pred(ICard c) =>
                    c.HasType(CardType.Creature) && CardColors.GetColors(c).Count == 0;

                var candidates = owner.Zones.Library.GetCards()
                    .Where(Pred)
                    .ToList();

                if (candidates.Count == 0)
                {
                    // No candidate → declined / nothing to find. Shuffle
                    // still fires per the printed oracle.
                    LibraryShuffle.ShuffleLibrary(owner, "eye-of-ugin");
                    return;
                }

                // Mirror MysticalTutorFactory: agent-driven pick with a
                // deterministic first-match fallback. The kindLabel is
                // the prompt string surfaced to the agent so policies
                // can score / filter by oracle wording.
                var agent = AgentRegistry.Get(owner);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates,
                        "colorless creature card")
                        .GetAwaiter().GetResult()
                    : candidates[0];

                if (pick == null)
                {
                    // Agent declined (CR 701.19a) — shuffle still happens.
                    LibraryShuffle.ShuffleLibrary(owner, "eye-of-ugin");
                    return;
                }

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                LibraryShuffle.ShuffleLibrary(owner, "eye-of-ugin");
            });

        var tutorAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(TutorActivationCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { tutorEffect });

        land.AddAbility(tutorAbility);

        return land;
    }
}
