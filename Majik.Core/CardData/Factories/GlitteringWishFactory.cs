using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glittering Wish (Future Sight, {G}{W}).
///
/// Sorcery (multicolored — Selesnya gold). Oracle text:
///   "You may choose a multicolored card you own from outside the game,
///    reveal that card, and put it into your hand. Exile Glittering Wish."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {G}{W}.
/// - Resolve: delegates to <see cref="WishTutorEffect"/> with
///   <see cref="WishTutorEffect.Predicates.MulticoloredCard"/> (CR 105.1c —
///   multicolored = ≥2 distinct colours, evaluated via
///   <see cref="Majik.Core.Cards.CardColors.GetColors"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"Exile Glittering Wish"</b>. Sideboard semantics; same posture
///   as the rest of the cycle.
/// - <b>"You may"</b> opt-in: v1 collapses to mandatory-with-decline.
/// - <b>Reveal event</b>: tutor-factory gap.
/// </summary>
[CardName("Glittering Wish")]
public static class GlitteringWishFactory
{
    public const string CardName = "Glittering Wish";
    public const string PrintedManaCost = "{G}{W}";

    /// <summary>Construct Glittering Wish as a Sorcery owned by
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
    /// Build the <see cref="SpellDefinition"/> for Glittering Wish. No
    /// target requests — the wish-tutor resolves via the caster's
    /// wishboard pile (CR 408).
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
                    predicate: WishTutorEffect.Predicates.MulticoloredCard,
                    pileLabel: "a multicolored card you own from outside the game",
                    intent: BotIntent.Tutor)
                    .AsEffect(caster),
            });
    }
}
