using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Burning Wish (Judgment, {1}{R}).
///
/// Sorcery. Oracle text:
///   "You may choose a sorcery card you own from outside the game, reveal
///    that card, and put it into your hand. Exile Burning Wish."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{R}.
/// - Resolve: delegates to <see cref="WishTutorEffect"/> with
///   <see cref="WishTutorEffect.Predicates.SorceryCard"/>. The
///   "outside the game" pool is <see cref="Player.Wishboard"/>
///   (CR 408 — semantic alias over <see cref="Player.Sideboard"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"Exile Burning Wish"</b>. The printed self-exile rider is the same
///   shape sideboard semantics already provide (the cast card never
///   re-enters the library), and the engine has no dedicated
///   "cast-from-stack exiles itself on resolve" hook yet. Matches the
///   posture every other wish-cycle factory uses.
/// - <b>"You may"</b>. The printed wording is opt-in; v1 treats the effect
///   as mandatory-with-decline (the same <c>WishTutorEffect</c> agent
///   prompt returns null → no-op, which is semantically equivalent for
///   the heuristic agent).
/// - <b>Reveal event</b>. Picked card moves wishboard → hand without
///   publishing a <c>CardRevealedEvent</c>; same gap as every tutor
///   factory.
/// </summary>
[CardName("Burning Wish")]
public static class BurningWishFactory
{
    public const string CardName = "Burning Wish";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>Construct Burning Wish as a Sorcery owned by
    /// <paramref name="owner"/>. Card shape only — the resolve body is
    /// produced by <see cref="BuildDefinition"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Burning Wish. No target
    /// requests — the wish-tutor resolves via the caster's wishboard
    /// pile (CR 408) rather than a cast-time target.
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
                    predicate: WishTutorEffect.Predicates.SorceryCard,
                    pileLabel: "a sorcery card you own from outside the game",
                    intent: BotIntent.Tutor)
                    .AsEffect(caster),
            });
    }
}
