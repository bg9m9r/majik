using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Death Wish (Judgment, {1}{B}{B}).
///
/// Sorcery. Oracle text (Scryfall, 2026-05):
///   "You may put a card you own from outside the game into your hand.
///    You lose half your life, rounded up. Exile Death Wish."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}{B}.
/// - Resolve produces two ordered effects on the spell's resolution:
///     1. <see cref="WishTutorEffect"/> with
///        <see cref="WishTutorEffect.Predicates.AnyCard"/> against
///        <see cref="Player.Wishboard"/> (CR 408 — same pool semantics
///        as Mastermind's Acquisition mode 2; no type filter).
///     2. Caster loses half their current life, rounded up
///        (<c>(int)Math.Ceiling(caster.LifeTotal / 2.0)</c>) via
///        <see cref="Player.LoseLife"/>. CR 119.3 / CR 119.4 — "lose
///        half your life, rounded up" is evaluated at resolution time
///        against the current life total (post-tutor, but the tutor
///        doesn't touch life so order is observationally identical).
///
/// ## Deferred (v1 gaps)
/// - <b>"Exile Death Wish"</b>. The printed self-exile rider is the same
///   shape sideboard semantics already provide; same posture as every
///   other wish-cycle factory.
/// - <b>"You may"</b> opt-in: v1 collapses to mandatory-with-decline
///   (<see cref="WishTutorEffect"/> with a null agent pick is a clean
///   no-op for the tutor portion; the life-loss is unconditional in v1).
///   The strict-MTG reading is "you may [tutor]" + the life-loss is
///   independent — v1 matches that posture: even if the agent declines
///   the tutor, the life-loss still fires.
/// - <b>Reveal event</b>: tutor-factory gap.
/// </summary>
[CardName("Death Wish")]
public static class DeathWishFactory
{
    public const string CardName = "Death Wish";
    public const string PrintedManaCost = "{1}{B}{B}";

    /// <summary>Construct Death Wish as a Sorcery owned by
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
    /// Build the <see cref="SpellDefinition"/> for Death Wish. No target
    /// requests — the wish-tutor resolves via the caster's wishboard
    /// pile (CR 408) and the life-loss targets the caster unconditionally.
    /// </summary>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new WishTutorEffect(
                    predicate: WishTutorEffect.Predicates.AnyCard,
                    pileLabel: "a card you own from outside the game",
                    intent: BotIntent.Tutor)
                    .AsEffect(caster),
                new Effect("Death Wish — lose half life (rounded up)", () =>
                {
                    // CR 119.3 — "lose half your life, rounded up" is
                    // evaluated at resolution against the current life
                    // total. Negative-life edge: caster already at 0 or
                    // below → ceil(0/2) = 0 (no-op); engine SBA handling
                    // (CR 704.5a) is responsible for the actual loss.
                    var loss = (int)Math.Ceiling(caster.LifeTotal / 2.0);
                    if (loss <= 0) return;
                    caster.LoseLife(loss);
                }),
            });
    }
}
