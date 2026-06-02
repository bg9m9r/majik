using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stoke the Flames (Magic 2015, {2}{R}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Convoke (Your creatures can help cast this spell. Each creature you tap
///    while casting this spell pays for {1} or one mana of that creature's
///    color.)
///    Stoke the Flames deals 4 damage to any target."
///
/// ## Implementation
///
/// Combines two already-supported shapes:
/// - <b>Resolve body</b>: the archetypal "deal 4 damage to any target" burn,
///   identical to <see cref="FlameJavelinFactory"/> — routed through
///   <see cref="Fx.DealDamageAny"/> so all four "any target" classes resolve
///   correctly (CR 115.3 — creature, player, planeswalker, or battle).
///   CR 306.7 — damage to a planeswalker becomes loyalty removal; CR 309.5 —
///   damage to a battle becomes defense removal; both handled inside
///   <see cref="Fx.DealDamageAny"/>.
/// - <b>Convoke keyword marker</b> (CR 702.51) — same inline
///   <see cref="KeywordAbility"/> shape as <see cref="ChordOfCallingFactory"/>
///   / <see cref="ConclaveTribunalFactory"/>. The marker is purely
///   descriptive; per-cast cost reduction is surfaced via
///   <see cref="ConvokeAdditionalCost"/> (built on demand by
///   <see cref="BuildAdditionalCost"/>) — caller threads it through the cast
///   flow's <c>additionalCosts</c> parameter.
///
/// Card shape comes from the embedded JSON (<c>stoke-the-flames.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The Convoke marker is attached on top
/// of the loaded shape (the data-only JSON schema does not express keywords),
/// and the resolve-time body lives in <see cref="BuildSpellDefinition"/>
/// because a <see cref="SpellDefinition"/> needs a target resolver supplied by
/// the caller's <see cref="GameContext"/>.
///
/// ## Deferred (v1 gaps)
/// - Same Convoke-flow gap documented on <see cref="ChordOfCallingFactory"/>:
///   the v1 cost-reduction path is the per-tap reducer
///   <see cref="ConvokeAdditionalCost"/>; agent-driven creature-tap prompts on
///   the cast flow are deferred.
/// </summary>
[CardName("Stoke the Flames")]
public static class StokeTheFlamesFactory
{
    public const string CardName = "Stoke the Flames";
    public const string Slug     = "stoke-the-flames";

    /// <summary>Printed mana cost — {2}{R}{R}, mana value 4.</summary>
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>CR 119 — fixed 4 damage to any target.</summary>
    public const int Damage = 4;

    /// <summary>
    /// Build the card shape from the embedded JSON definition, then attach the
    /// Convoke keyword marker (CR 702.51).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def  = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 702.51 — Convoke keyword marker. Descriptive only; the cost
        // machinery lives on the ConvokeAdditionalCost returned by
        // BuildAdditionalCost. Same inline attach as Chord of Calling.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        return card;
    }

    /// <summary>
    /// CR 702.51 — build the Convoke additional cost for this Stoke the Flames
    /// spell with the caller-selected untapped creatures. Same shape as
    /// <see cref="ChordOfCallingFactory.BuildAdditionalCost"/>: the caller
    /// threads the returned cost through the cast flow's
    /// <c>additionalCosts</c> parameter so the chosen creatures are tapped and
    /// each folds a per-tap reduction (generic OR a coloured pip matching the
    /// creature's colour, per CR 702.51b) into the mana payment.
    /// </summary>
    public static ConvokeAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Creature> tappedCreatures) =>
        new(card, tappedCreatures);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Stoke the Flames is
    /// cast. Single 1..1 "any target" request, no X; on resolution deals
    /// <see cref="Damage"/> (4) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/> (CR 120.3).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Stoke the Flames: 4 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
