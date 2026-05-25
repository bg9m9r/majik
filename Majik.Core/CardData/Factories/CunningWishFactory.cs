using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cunning Wish (Judgment, {2}{U}).
///
/// Instant. Oracle text:
///   "You may choose an instant card you own from outside the game, reveal
///    that card, and put it into your hand. Exile Cunning Wish."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{U}.
/// - Resolve: delegates to <see cref="WishTutorEffect"/> with
///   <see cref="WishTutorEffect.Predicates.InstantCard"/>. The
///   "outside the game" pool is <see cref="Player.Wishboard"/>
///   (CR 408 — semantic alias over <see cref="Player.Sideboard"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"Exile Cunning Wish"</b>. Sideboard semantics already keep the
///   cast card out of the library; same posture as every other wish-cycle
///   factory.
/// - <b>"You may"</b> opt-in: v1 collapses to mandatory-with-decline.
/// - <b>Reveal event</b>: tutor-factory gap.
/// </summary>
[CardName("Cunning Wish")]
public static class CunningWishFactory
{
    public const string CardName = "Cunning Wish";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>Construct Cunning Wish as an Instant owned by
    /// <paramref name="owner"/>. Card shape only — the resolve body is
    /// produced by <see cref="BuildDefinition"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Cunning Wish. No target
    /// requests — the wish-tutor resolves via the caster's wishboard
    /// pile (CR 408).
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
                    predicate: WishTutorEffect.Predicates.InstantCard,
                    pileLabel: "an instant card you own from outside the game",
                    intent: BotIntent.Tutor)
                    .AsEffect(caster),
            });
    }
}
