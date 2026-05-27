using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Divination (M10 and many reprints, {2}{U}).
///
/// Sorcery. Oracle text:
///   "Draw two cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U}.
/// - No target requests — the effect resolves entirely on the caster.
/// - Resolve effect via <see cref="BuildResolveEffect"/>:
///     <see cref="Fx.DrawCards"/> — caster draws 2 cards (CR 121.1).
///     Each draw routes through the replacement bus so Dredge and similar
///     effects get a shot per draw. An empty library stamps the standard
///     SBA loss flag (CR 704.5b) without throwing.
/// </summary>
[CardName("Divination")]
public static class DivinationFactory
{
    public const string CardName = "Divination";
    public const string PrintedManaCost = "{2}{U}";
    public const int DrawAmount = 2;

    /// <summary>
    /// Construct Divination as a Sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve closure is produced by
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Divination. No modes,
    /// no X, no target requests — the body resolves entirely on the caster.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build the resolve effect: caster draws 2 cards.
    /// Each draw routes through <see cref="Fx.DrawCards"/> so the
    /// replacement bus (Dredge etc.) gets a shot per draw, and an empty
    /// library stamps the SBA loss flag (CR 704.5b) without throwing.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw two cards.",
                () =>
                {
                    // CR 121.1 — draw 2. Replacement bus per-draw; empty
                    // library stamps the SBA loss flag (CR 704.5b).
                    Fx.DrawCards(caster, DrawAmount);
                }),
        };
    }
}
