using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Strike It Rich (Streets of New Capenna, {R}).
///
/// Sorcery. Oracle text:
///   "Create a Treasure token.
///    Flashback {2}{R}."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Set: Streets of New Capenna (snc), rare</item>
///   <item>Mana cost: {R}</item>
///   <item>Mana value: 1</item>
///   <item>Type line: Sorcery</item>
///   <item>Colors: R; color identity: R</item>
/// </list>
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}, mana value 1, red.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) creates one
///   Treasure token under the caster's control via
///   <see cref="TokenFactory.CreateTreasure"/>. The token is a colourless
///   artifact (CR 111.10) with "{T}, Sacrifice this artifact: Add one mana
///   of any color." (five <see cref="ManaAbility"/> options — W/U/B/R/G).
///   The same effect is reused for both the printed-cost cast and the
///   flashback cast.
/// - Flashback alt-cost ({2}{R}) is exposed via <see cref="BuildFlashbackCost"/>
///   (parsed by <see cref="FlashbackOracleParser"/> from the printed oracle
///   text so the data-driven binder path and this named-factory path agree
///   on cost shape). Post-resolve exile (CR 702.33b) is handled by the
///   cost's <c>OnResolved</c> hook.
///
/// ## Rules citations
/// - CR 702.33 — Flashback keyword (cast from graveyard, then exile).
/// - CR 702.33b — "After it resolves, it's exiled."
/// - CR 111.10 — Treasure token rules text.
///
/// ## Engine role
/// Required by RubyStorm and Belcher bot decks. Strike It Rich provides
/// single-{R} mana acceleration: cast → 1 Treasure → tap for any pip. The
/// Flashback lets a second cast from the graveyard for {2}{R}, netting
/// another Treasure at the cost of three mana.
///
/// ## Deferred (v1 gaps)
/// - Treasure tap-to-sac prompt: uses the five-option ManaAbility model
///   shared by all Treasure tokens; agent selects the colour at mana-pick
///   time.
/// </summary>
[CardName("Strike It Rich")]
public static class StrikeItRichFactory
{
    public const string CardName = "Strike It Rich";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Oracle text used by <see cref="BuildFlashbackCost"/> to derive the
    /// flashback cost via <see cref="FlashbackOracleParser"/>. Kept on the
    /// factory so the production load path (Scryfall row → oracle text →
    /// parser) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "Create a Treasure token.\nFlashback {2}{R}";

    /// <summary>
    /// Build a Strike It Rich sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time Treasure creation is built on
    /// demand via <see cref="BuildResolveEffect"/>; flashback cost in
    /// <see cref="BuildFlashbackCost"/>.
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
    /// Build Strike It Rich's resolve effect — create one Treasure token
    /// under the caster's control. Single <see cref="IEffect"/> entry so
    /// callers can splice it into a <c>SpellDefinition.EffectFactory</c>
    /// result or a <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// The same effect is reused for both the printed-cost cast and the
    /// flashback cast — flashback's post-resolve exile is performed by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>, not here.
    /// </summary>
    /// <param name="caster">Spell controller; the Treasure enters under
    /// their control (CR 111.10).</param>
    /// <param name="zoneService">Optional zone service — routes the token's
    /// ETB through <see cref="ZoneService"/> so <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// publishes (enabling downstream triggers). Null → direct zone move,
    /// suitable for unit-test / shape-only paths.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Strike It Rich: create a Treasure token.", () =>
            {
                // CR 111.10 — Treasure token: colourless artifact with
                // "{T}, Sacrifice this artifact: Add one mana of any color."
                // TokenFactory.CreateTreasure handles the full spec including
                // the five ManaAbility options and the battlefield ETB move.
                TokenFactory.CreateTreasure(caster, zoneService);
            }),
        };
    }

    /// <summary>
    /// Build the flashback alternative cost ({2}{R}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here)
    /// keeps the named-factory path and the data-driven oracle binder path
    /// agreeing on shape — any change to the parser's interpretation of
    /// "Flashback {2}{R}" flows through to this factory automatically.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Strike It Rich's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
