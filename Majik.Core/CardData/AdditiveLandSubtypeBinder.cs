using System.Text.RegularExpressions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;

namespace Majik.Core.CardData;

/// <summary>
/// Binds the additive "Each land is a [basic land type] in addition to its
/// other [land] types" Layer 4 static (CR 305.7 / 613.1d) onto a Land card
/// from its oracle text.
///
/// <para>This is the production wiring for Urborg, Tomb of Yawgmoth ("Each
/// land is a Swamp in addition to its other land types.") and Yavimaya,
/// Cradle of Growth ("Each land is a Forest in addition to its other land
/// types.").</para>
///
/// <para>Lands are NEVER routed through their <c>[CardName]</c> factory in
/// production (see <see cref="FactoryRouting"/>) — they arrive as binder-chain
/// shells and rely on the binders in <c>GameFacade.BindCardAbilities</c> for
/// their abilities. The Urborg / Yavimaya factories therefore only wire their
/// <see cref="GrantLandSubtypeStaticEffect"/> on a test-only overload that prod
/// never calls, so without this binder the additive grant was silently dropped
/// at the live table. This binder closes that gap: it detects the oracle text
/// and attaches a live <see cref="GrantLandSubtypeStaticEffect"/> against the
/// game's <see cref="ContinuousEffectsService"/> + <see cref="IEventBus"/>, so
/// the static registers/unregisters as the source land enters/leaves the
/// battlefield (CR 305.7).</para>
///
/// <para>The granted basic land subtype carries its intrinsic mana ability
/// (CR 305.6): a Mountain under Urborg taps for {R} AND {B} via PR #155's
/// <see cref="EffectiveManaAbilities"/> additive-vs-replacement detection.
/// A land that already has the granted subtype is unaffected — the additive
/// effect just re-adds an already-present subtype, and
/// <see cref="EffectiveManaAbilities"/> synthesizes no duplicate ability for
/// a printed basic.</para>
/// </summary>
public static class AdditiveLandSubtypeBinder
{
    // Matches: "Each land is a[n] <BasicType> in addition to its other [land] types."
    // The "land" before "types" is optional — Urborg/Yavimaya read "other land
    // types"; older templating reads "other types". Both are CR 305.7 grants.
    private static readonly Regex EachLandIsBasic = new(
        @"Each\s+land\s+is\s+an?\s+(?<type>Plains|Island|Swamp|Mountain|Forest)\s+" +
        @"in\s+addition\s+to\s+its\s+other(?:\s+land)?\s+types",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, CardSubtype> SubtypeByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Plains"]   = CardSubtype.Plains,
            ["Island"]   = CardSubtype.Island,
            ["Swamp"]    = CardSubtype.Swamp,
            ["Mountain"] = CardSubtype.Mountain,
            ["Forest"]   = CardSubtype.Forest,
        };

    /// <summary>
    /// If <paramref name="entity"/>'s oracle text is an "Each land is a
    /// [basic] in addition to its other [land] types" grant and
    /// <paramref name="card"/> is a <see cref="Land"/>, attach a live
    /// <see cref="GrantLandSubtypeStaticEffect"/> scoped to every Land on the
    /// battlefield (including the source itself, CR 305.7) and call
    /// <see cref="GrantLandSubtypeStaticEffect.Attach"/> so it tracks the
    /// source's zone changes via <paramref name="eventBus"/>.
    /// </summary>
    /// <returns><c>true</c> when the static was attached; <c>false</c>
    /// otherwise.</returns>
    public static bool Bind(
        ICard card,
        CardEntity entity,
        ContinuousEffectsService effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(effects);

        if (card is not Land land) return false;

        var text = entity.OracleText;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = EachLandIsBasic.Match(text);
        if (!m.Success) return false;

        if (!SubtypeByName.TryGetValue(m.Groups["type"].Value, out var subtype))
        {
            return false;
        }

        // CR 305.7 — "Each land is a [basic] in addition to its other land
        // types." Scope every Land (including the source itself); additively
        // grant the basic land subtype. The intrinsic mana ability of the
        // granted subtype (CR 305.6) is derived by EffectiveManaAbilities.
        var lifecycle = new GrantLandSubtypeStaticEffect(
            land,
            effects,
            eventBus,
            scope: p => p is Land,
            subtypeToGrant: subtype);
        lifecycle.Attach();

        return true;
    }
}
