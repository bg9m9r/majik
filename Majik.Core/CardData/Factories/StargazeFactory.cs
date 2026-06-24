using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stargaze (Magic: The Gathering — March of the
/// Machine, {X}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-24):
///   "Look at twice X cards from the top of your library. Put X cards from
///    among them into your hand and the rest into your graveyard. You lose X
///    life."
///
/// ## Implementation
///
/// Card shape (name, Sorcery, {X}{B}{B}) is materialised from the embedded
/// JSON definition (<c>stargaze.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The {X}{B}{B} cost makes the
/// card's <see cref="SpellDefinition.HasVariableX"/> true so the cast flow
/// prompts for X (CR 601.2b) and stamps it onto
/// <see cref="ChosenSpellParams.X"/>.
///
/// The on-resolve dig-and-drain is built via <see cref="BuildSpellDefinition"/>,
/// the single source of truth shared by:
///   * the production cast path — <see cref="OracleSpellBinder"/> binds the
///     seed oracle text to this definition through
///     <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.StargazeTemplate"/>
///     (the named-factory <see cref="BuildSpellDefinition"/> is NOT itself in
///     the prod path — cards are resolved AT CAST TIME BY NAME via the binder
///     registry, so the template delegates here to keep one implementation), and
///   * the unit test, which exercises the resolve body directly.
///
/// ## Resolve semantics (CR notes)
/// - <b>X</b> is read from <see cref="ChosenSpellParams.X"/> (CR 601.2b /
///   CR 107.3 — the announced value chosen at cast time).
/// - <b>"Look at twice X cards from the top of your library"</b> — CR 701
///   look; the controller examines (does NOT reveal) the top 2X cards. If the
///   library has fewer than 2X cards, you look at as many as there are
///   (CR 120.6-style "as many as you can").
/// - <b>"Put X cards from among them into your hand and the rest into your
///   graveyard"</b> — the controller chooses X of the looked-at cards for hand;
///   the remainder go to the graveyard. The same dig-selection posture as the
///   shared
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.LibrarySpellFactory"/>
///   look-K helper / <see cref="DigThroughTimeFactory"/>: the deterministic
///   first-K-to-hand pick (bots auto-pick; UI clients build the selector). When
///   fewer than 2X cards were looked at, you put as many as you can into your
///   hand (up to X) and the rest into the graveyard.
/// - <b>"You lose X life"</b> — CR 119.3: a separate life-change event AFTER
///   the cards move. CR 119.4 — losing 0 life (X = 0) is not losing life, so
///   the whole resolve body is a clean no-op at X = 0.
///
/// ## Rules citations
/// - CR 117.5 — printed mana cost.
/// - CR 601.2b — choosing the value of X.
/// - CR 119.3 / 119.4 — losing X life as a separate event; losing 0 is a no-op.
/// </summary>
[CardName("Stargaze")]
public static class StargazeFactory
{
    public const string CardName = "Stargaze";
    public const string Slug = "stargaze";
    public const string PrintedManaCost = "{X}{B}{B}";

    /// <summary>Build the card shape from the embedded JSON definition. This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Stargaze — a variable-X
    /// spell with no modes and no targets. The resolve body reads the chosen
    /// X off the <see cref="ChosenSpellParams"/> stamped by the cast flow.
    /// </summary>
    /// <param name="caster">Spell controller — digs, keeps, and loses the
    /// life.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: chosen => BuildResolveEffect(caster, chosen.X ?? 0));
    }

    /// <summary>
    /// Build Stargaze's resolve effect — look at the top 2X cards, put X of
    /// them into hand, the rest into the graveyard, then lose X life. Exposed
    /// for direct unit testing of the unique dig-and-drain behaviour.
    /// </summary>
    /// <param name="caster">Spell controller performing the dig + life loss.</param>
    /// <param name="x">Chosen X.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster, int x)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: look at top {2 * x}, hand {x}, rest to graveyard; lose {x} life.",
                () =>
                {
                    // CR 119.4 — losing 0 life is not losing life; with X = 0
                    // there is nothing to look at, keep, or lose. Clean no-op.
                    if (x <= 0) return;

                    var library = caster.Zones.Library;

                    // "Look at twice X cards from the top of your library." Take
                    // as many as exist when the library is short (look at as
                    // many as you can).
                    var looked = library.GetCards().Take(2 * x).ToList();

                    // "Put X cards from among them into your hand and the rest
                    // into your graveyard." Deterministic first-X-to-hand pick
                    // (bots auto-pick; UI clients build the selector) — same
                    // posture as the shared look-K dig helper /
                    // DigThroughTimeFactory. When fewer than 2X were looked at,
                    // keep up to X and bin the remainder.
                    var keep = Math.Min(x, looked.Count);
                    for (var i = 0; i < keep; i++)
                    {
                        var c = looked[i];
                        library.RemoveCard(c);
                        caster.Zones.Hand.AddCard(c);
                        c.SetZone(ZoneType.Hand);
                    }
                    for (var i = keep; i < looked.Count; i++)
                    {
                        var c = looked[i];
                        library.RemoveCard(c);
                        caster.Zones.Graveyard.AddCard(c);
                        c.SetZone(ZoneType.Graveyard);
                    }

                    // CR 119.3 — "You lose X life" is a separate life-change
                    // event, applied after the cards move.
                    caster.LoseLife(x);
                }),
        };
    }
}
