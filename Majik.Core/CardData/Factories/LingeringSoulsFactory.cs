using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lingering Souls (Innistrad / Modern Horizons,
/// {2}{B}).
///
/// Sorcery. Oracle text (Scryfall, verbatim):
///   "Create two 1/1 white Spirit creature tokens with flying.
///    Flashback {1}{W}"
///
/// ## Implemented (v1)
///
/// - Sorcery shape, mana cost <c>{2}{B}</c>.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>): create two 1/1
///   white Spirit creature tokens with Flying (CR 603.6a / CR 111.4).
///   Delegates to <see cref="TriplicateSpiritsFactory.CreateSpiritTokens"/>
///   so the printed token shape stays in sync with Triplicate Spirits —
///   both cards emit the same Spirit token (same printed wording, same
///   colour, same subtype, same keyword).
/// - Flashback alt-cost (<c>{1}{W}</c>) is exposed via
///   <see cref="BuildFlashbackCost"/> — parsed from <see cref="OracleText"/>
///   by <see cref="FlashbackOracleParser"/> so the data-driven binder path
///   and this named-factory path agree on cost shape (same pattern as
///   <see cref="FaithlessLootingFactory"/>). Callers wire the returned
///   <see cref="FlashbackAlternativeCost"/> into
///   <see cref="Majik.Core.Game.SpellCastFlow"/> when casting from
///   graveyard; the cost's <c>OnResolved</c> hook exiles the card after
///   resolution (CR 702.34b).
///
/// ## Deferred (v1 gaps)
///
/// - The Spirit token spawn intentionally fires before the optional
///   flashback exile (same shape as Faithless Looting); the exile is owned
///   by <see cref="FlashbackAlternativeCost.OnResolved"/>, not this
///   factory.
/// </summary>
[CardName("Lingering Souls")]
public static class LingeringSoulsFactory
{
    public const string CardName = "Lingering Souls";
    public const string PrintedManaCost = "{2}{B}";
    public const int TokenCount = 2;

    /// <summary>
    /// Oracle text used by <see cref="BuildFlashbackCost"/> to derive the
    /// flashback cost via <see cref="FlashbackOracleParser"/>. Kept on the
    /// factory so the production load path (Scryfall row → oracle text →
    /// parser) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "Create two 1/1 white Spirit creature tokens with flying.\nFlashback {1}{W}";

    /// <summary>
    /// Build a Lingering Souls sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so the caster reference matches
    /// the player resolving the spell, and a live <see cref="ZoneService"/>
    /// can be threaded in.
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
    /// Build Lingering Souls' resolve effect — create two 1/1 white Spirit
    /// creature tokens with Flying (CR 603.6a / CR 111.4). Single
    /// <see cref="IEffect"/> entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list. The same effect
    /// is reused for both the printed-cost cast and the flashback cast —
    /// flashback's post-resolve exile is performed by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>, not here.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create two 1/1 white Spirit creature tokens with flying",
                () => TriplicateSpiritsFactory.CreateSpiritTokens(caster, TokenCount, zones)),
        };
    }

    /// <summary>
    /// Build the flashback alternative cost ({1}{W}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here)
    /// keeps the named-factory path and the data-driven oracle binder path
    /// agreeing on shape — any change to the parser's interpretation of
    /// "Flashback {1}{W}" flows through to this factory automatically.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Lingering Souls's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
