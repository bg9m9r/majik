using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anointed Procession (Amonkhet, {3}{W}).
///
/// Enchantment. Oracle text:
///   "If an effect would create one or more tokens under your control,
///    it creates twice that many of those tokens instead."
///
/// ## Implementation
///
/// CR 614 replacement effect on <see cref="TokenCreationIntent"/>. The
/// factory registers a <see cref="TokenDoublerReplacement"/> gated on
/// <c>intent.Controller == owner</c> so the doubler fires for every
/// token-creation routed through
/// <see cref="Majik.Core.Tokens.TokenFactory.CreateOnBattlefield(Majik.Core.Tokens.TokenFactory.TokenSpec, Player, int, Majik.Core.Zones.ZoneService?, ReplacementBus?)"/>.
///
/// ## Stacking
///
/// CR 616.1c — each doubler instance fires at most once per intent, so:
///   - Two copies of Anointed Procession shipped-1 = 4 (1 → 2 → 4).
///   - Anointed Procession + Parallel Lives shipped-1 = 4 (1 → 2 → 4).
///   - Anointed Procession + Doubling Season shipped-1 = 4 (Doubling
///     Season's token half doubles independently of its counter half).
///
/// ## Caller integration
///
/// Token-creating effects must route through the bus-aware
/// <c>TokenFactory.CreateOnBattlefield(spec, controller, count, zones, replacements)</c>
/// overload to honour Anointed Procession. The single-token overload
/// (<c>CreateOnBattlefield(spec, controller, zones)</c>) bypasses the
/// bus and is reserved for callers that explicitly opt out
/// (e.g. token-copy effects spawned by Splinter Twin, where the
/// printed text "create a token that's a copy" already encodes "a"
/// — the doubler retrofit for copy-tokens is tracked separately and
/// requires the copy-token analogue intent).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Source-zone gate</b>: the replacement fires while the source
///   enchantment sits on the battlefield. The zone check is
///   <i>implicit</i> in the current MVP — every doubler factory
///   registers once on <see cref="Create"/> and relies on the source
///   being on the battlefield when the intent fires. Blink / bounce
///   does not yet deregister; tracked as a follow-up shared with
///   every other replacement today.
/// - <b>Replacement ordering prompt</b> (CR 616.1): when multiple
///   doublers overlap the affected player chooses the order. The bus
///   applies in registration order today.
/// </summary>
[CardName("Anointed Procession")]
public static class AnointedProcessionFactory
{
    public const string CardName = "Anointed Procession";
    public const string PrintedManaCost = "{3}{W}";

    /// <summary>
    /// Shape-only construction (no bus). The card prints correctly and
    /// dispatches under its printed name, but the doubler is not wired.
    /// Suitable for identity / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Construct Anointed Procession. When <paramref name="replacements"/>
    /// is supplied a <see cref="TokenDoublerReplacement"/> is registered
    /// so every <see cref="TokenCreationIntent"/> with this owner as
    /// controller is doubled.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            replacements.Register<TokenCreationIntent>(new TokenDoublerReplacement(
                intent => card.Zone == Majik.Core.Zones.ZoneType.Battlefield
                          && ReferenceEquals(intent.Controller, owner)));
        }

        return card;
    }
}
