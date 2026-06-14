using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prismatic Ending (Modern Horizons 2, {W}).
///
/// Sorcery. Oracle text:
///   "Exile target nonland permanent with mana value less than or equal
///    to the number of colors of mana spent to cast Prismatic Ending."
///
/// CR 202.2 / CR 106.4 — "colors of mana spent to cast" counts the
/// distinct colors of mana paid into the spell's cost (white, blue,
/// black, red, green); colourless mana contributes nothing, hybrid mana
/// counts as whichever colour the controller chose to pay.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {W}.
/// - Single 1..1 "target nonland permanent" target request via
///   <see cref="BuildSpellDefinition"/>.
/// - On resolve: looks up the colours-spent cap from a caller-supplied
///   <see cref="Func{Int32}"/>. The chosen target is exiled (CR 701.21)
///   iff it is still on the battlefield, is not a land, and its mana
///   value is &lt;= the cap. CR 608.2b — illegal target → no effect.
///
/// ## Mana-provenance ledger (live)
/// The colours-of-mana-spent count is read off the LIVE mana-provenance
/// ledger (<see cref="Majik.Core.Cards.Card.PendingCastColors"/>, stamped
/// by <see cref="Majik.Core.Game.TurnDriver"/> at payment time), surfaced
/// to the resolution effect via
/// <see cref="Majik.Core.Abilities.ResolutionContext.SourceCard"/>. Hybrid
/// pips count the colour actually paid, Phyrexian pips paid with life count
/// zero colours, and colourless / generic mana never contributes — all of
/// which fall out of the ledger's per-colour pool delta for free
/// (CR 106.4 / CR 202.2). The production card binds through
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.ConvergeExileTemplate"/>,
/// which uses the same ledger read
/// (<see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.ConvergeColorsSpent"/>).
///
/// <para>The <c>colorsSpentProvider</c> closure remains as an explicit
/// override for shape / dispatcher tests that resolve outside a live spell
/// frame; when <c>null</c> the effect reads the live ledger off the
/// resolution context.</para>
/// </summary>
[CardName("Prismatic Ending")]
public static class PrismaticEndingFactory
{
    public const string CardName = "Prismatic Ending";

    // CR 107.3 — printed cost is {X}{W}; the generic {X} is poured into more
    // colours of mana so Converge can reach a higher colours-spent count.
    public const string PrintedManaCost = "{X}{W}";

    /// <summary>Current Scryfall oracle text. Kept here so the data-driven
    /// import path can cross-check the named factory against Scryfall.</summary>
    public const string OracleText =
        "Converge — Exile target nonland permanent if its mana value is less " +
        "than or equal to the number of colors of mana spent to cast this spell.";

    /// <summary>
    /// Build a Prismatic Ending sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve behaviour is built on demand via
    /// <see cref="BuildSpellDefinition(Func{object, object}, Func{int}?)"/>.
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
    /// Single-arg path — reads the colours-spent cap off the LIVE
    /// mana-provenance ledger
    /// (<see cref="Majik.Core.Abilities.ResolutionContext.SourceCard"/> →
    /// <see cref="Majik.Core.Cards.Card.PendingCastColors"/>) at resolution.
    /// This is the production shape. Tests that resolve outside a live spell
    /// frame supply an explicit provider via the two-arg overload.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver) =>
        BuildSpellDefinition(resolver, colorsSpentProvider: null);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Prismatic Ending
    /// is cast. Single 1..1 "target nonland permanent" request; on
    /// resolution the targeted permanent is exiled iff it's still on the
    /// battlefield, is not a land, and its mana value is &lt;= the
    /// colours-spent cap (CR 608.2b — illegal target → no effect).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="colorsSpentProvider">Optional explicit override of the
    /// colours-spent count, for shape / dispatcher tests that resolve outside
    /// a live spell frame. <c>null</c> reads the LIVE mana-provenance ledger
    /// (<see cref="Majik.Core.Cards.Card.PendingCastColors"/>) off the
    /// resolution context's
    /// <see cref="Majik.Core.Abilities.ResolutionContext.SourceCard"/>.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<int>? colorsSpentProvider)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target nonland permanent",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target nonland permanent with mv ≤ colors spent",
                        rc =>
                        {
                            if (raw is not Permanent target)
                                return ValueTask.CompletedTask;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield)
                                return ValueTask.CompletedTask;
                            if (target.HasType(CardType.Land))
                                return ValueTask.CompletedTask;

                            // CR 202.2 — colours of mana spent. Explicit
                            // provider wins (test seam); otherwise read the
                            // live mana-provenance ledger off SourceCard.
                            var cap = colorsSpentProvider?.Invoke()
                                      ?? SpellTemplates.Templates.Bespoke.ConvergeColorsSpent.From(rc);
                            if (target.ManaCostValue.TotalValue > cap)
                                return ValueTask.CompletedTask;

                            // Exile (CR 701.21). Routed through the owning
                            // player's zones so the permanent's owner-of-
                            // zone bookkeeping stays consistent across
                            // multi-player games (mirrors PathToExile).
                            var fromOwner = target.Owner;
                            if (fromOwner != null)
                            {
                                fromOwner.Zones.Battlefield.RemoveCard(target);
                                fromOwner.Zones.Exile.AddCard(target);
                            }
                            target.SetZone(ZoneType.Exile);
                            return ValueTask.CompletedTask;
                        }),
                };
            });
    }

    /// <summary>
    /// Helper: count the distinct colours represented in a mana-symbol
    /// string (e.g. <c>"WUG"</c> → 3). Useful for integration callers that
    /// already snapshot the spent-mana letters and want a quick adapter
    /// for <see cref="BuildSpellDefinition(Func{object, object}, Func{int}?)"/>.
    /// Recognises W, U, B, R, G (case-insensitive); other characters are
    /// ignored (C is colourless per CR 106.4; numerals are generic).
    /// </summary>
    public static int ColorCount(string? spentSymbols)
    {
        if (string.IsNullOrEmpty(spentSymbols)) return 0;

        var seen = 0;
        var w = false; var u = false; var b = false; var r = false; var g = false;
        foreach (var ch in spentSymbols)
        {
            switch (char.ToUpperInvariant(ch))
            {
                case 'W': if (!w) { w = true; seen++; } break;
                case 'U': if (!u) { u = true; seen++; } break;
                case 'B': if (!b) { b = true; seen++; } break;
                case 'R': if (!r) { r = true; seen++; } break;
                case 'G': if (!g) { g = true; seen++; } break;
            }
        }
        return seen;
    }
}
