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
/// ## Deferred (v1 gaps)
/// - <b>Mana provenance ledger</b>: the engine has no per-spell record
///   of which colours of mana were actually spent on the cost. Until
///   that exists, callers must pass <paramref name="colorsSpentProvider"/>
///   explicitly. The single-arg <see cref="BuildSpellDefinition(Func{object, object})"/>
///   path defaults to a cap of <see cref="DefaultColorsSpent"/> (1),
///   which models the printed minimum (the {W} pip alone) — enough to
///   exile mv-0 / mv-1 nonland permanents. See <see cref="ColorCount"/>
///   for the helper test/integration callers can compose against.
/// - <b>Hybrid / Phyrexian colour selection</b>: when a real provenance
///   ledger exists, hybrid pips paid as colour C count toward C only;
///   Phyrexian pips paid with life count zero colours. The
///   <see cref="Func{Int32}"/> closure is the integration point.
/// </summary>
[CardName("Prismatic Ending")]
public static class PrismaticEndingFactory
{
    public const string CardName = "Prismatic Ending";
    public const string PrintedManaCost = "{W}";

    /// <summary>
    /// Default colours-spent cap when no provider is supplied — models
    /// the printed minimum (single {W} pip → 1 colour). This is the
    /// floor; any real cast pays at least the printed pip.
    /// </summary>
    public const int DefaultColorsSpent = 1;

    /// <summary>Printed oracle text. Kept here so the data-driven import
    /// path can cross-check the named factory against Scryfall.</summary>
    public const string OracleText =
        "Exile target nonland permanent with mana value less than or equal " +
        "to the number of colors of mana spent to cast Prismatic Ending.";

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
    /// Single-arg path — defaults to <see cref="DefaultColorsSpent"/> for
    /// the colours-spent cap. Suitable for shape / dispatcher tests where
    /// no live mana-provenance ledger is wired.
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
    /// <param name="colorsSpentProvider">Optional. Called at resolve time
    /// to fetch the count of distinct colours of mana spent to cast this
    /// instance of the spell. <c>null</c> falls back to
    /// <see cref="DefaultColorsSpent"/>.</param>
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
                        () =>
                        {
                            if (raw is not Permanent target) return;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (target.HasType(CardType.Land)) return;

                            var cap = colorsSpentProvider?.Invoke()
                                      ?? DefaultColorsSpent;
                            if (target.ManaCostValue.TotalValue > cap) return;

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
