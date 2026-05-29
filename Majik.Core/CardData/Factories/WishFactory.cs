using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wish ({2}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "You may play a card you own from outside the game this turn."
///
/// Sibling of the Judgment / Future Sight wish cycle (Burning / Cunning /
/// Glittering / Living / Death Wish). The base card shape (name / Sorcery
/// type / {2}{R} cost) is materialised from the embedded JSON definition
/// (<c>wish.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the resolve body is produced
/// by <see cref="BuildDefinition"/> because the JSON
/// <c>AbilityDefinition</c> schema does not express the wish-tutor primitive
/// (same posture as <see cref="ArdentPleaFactory"/>).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{R} (mana value 3).
/// - Resolve: delegates to <see cref="WishTutorEffect"/> with
///   <see cref="WishTutorEffect.Predicates.AnyCard"/> — Wish places no
///   type / colour restriction on the eligible card. The "outside the
///   game" pool is <see cref="Player.Wishboard"/> (CR 408 — semantic
///   alias over <see cref="Player.Sideboard"/>), same as Death Wish /
///   Mastermind's Acquisition mode 2.
///
/// ## Deferred (v1 gaps)
/// - <b>"play ... this turn"</b>. The printed effect grants *permission to
///   play* a card from outside the game for the remainder of the turn
///   (CR 118.1 / CR 608 — a duration-scoped play permission), rather than
///   moving the card to hand. The engine has no "grant play-from-outside
///   permission until end of turn" hook yet, so v1 uses the
///   observationally-equivalent supported primitive: fetch the chosen
///   card to hand (where it can then be played normally). This matches the
///   wishboard → hand posture every other wish-cycle factory uses.
/// - <b>"You may"</b>. The printed wording is opt-in; v1 treats the effect
///   as mandatory-with-decline (the <c>WishTutorEffect</c> agent prompt
///   returning null is a clean no-op — semantically equivalent for the
///   heuristic agent).
/// - <b>Reveal event</b>. Picked card moves wishboard → hand without
///   publishing a <c>CardRevealedEvent</c>; same gap as every tutor
///   factory.
/// </summary>
[CardName("Wish")]
public static class WishFactory
{
    public const string CardName = "Wish";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "wish";

    /// <summary>Construct Wish as a Sorcery owned by <paramref name="owner"/>.
    /// Base shape is materialised from the embedded JSON definition; the
    /// resolve body is produced by <see cref="BuildDefinition"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        // CardDefinitionFactory.Build already stamps owner + controller.
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Wish. No target requests —
    /// the wish-tutor resolves via the caster's wishboard pile (CR 408)
    /// rather than a cast-time target.
    /// </summary>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new[]
            {
                new WishTutorEffect(
                    predicate: WishTutorEffect.Predicates.AnyCard,
                    pileLabel: "a card you own from outside the game",
                    intent: BotIntent.Tutor)
                    .AsEffect(caster),
            });
    }
}
