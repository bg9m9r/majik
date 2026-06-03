using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.168 — Cloak. The cloak keyword action, printed on cards such as
/// Cryptic Coat:
/// <blockquote>
///   To cloak a card, put it onto the battlefield face down as a 2/2
///   creature with ward {2}. Turn it face up any time for its mana cost if
///   it's a creature card.
/// </blockquote>
///
/// <para>
/// <b>Relationship to Manifest (CR 701.31).</b> Cloak is the near-twin of
/// manifest: both put a card onto the battlefield face down as a 2/2 that
/// can be turned face up for its mana cost if it's a creature card
/// (CR 708.6). The single difference is that the <em>cloaked</em>
/// permanent additionally has <b>ward {2}</b> (CR 702.168a) — an ability
/// it has by virtue of being cloaked, distinct from the underlying card's
/// printed (and CR-708.2-hidden) abilities. So this primitive builds the
/// shared face-down wrapper via
/// <see cref="ManifestEffect.ManifestCard"/> and then layers the cloak
/// ward {2} onto it, flagged as face-down-intrinsic so it survives the
/// CR 708.2 ability suppression (see
/// <see cref="Permanent.MarkFaceDownIntrinsicAbility"/>).
/// </para>
///
/// <para>
/// <b>Ward modelling.</b> Ward is currently a marker keyword across the
/// engine (the <see cref="Majik.Core.Keywords.WardEffect"/> trigger
/// primitive exists as a stand-alone check but is not yet wired into the
/// spell-resolution path — same treatment as Kappa Cannoneer / Lavaspur
/// Boots and the rest of the ward cards). The cloak ward is the same
/// parameterised <see cref="KeywordAbility"/>("Ward", arg: 2) marker the
/// resolution-path consultation will read once that wiring lands.
/// </para>
///
/// <para>
/// <b>Empty library.</b> Clean no-op — mirrors manifest / manifest dread /
/// Scry / Mill (no empty-library loss is stamped here; that's the
/// draw-from-empty SBA's job).
/// </para>
/// </summary>
public static class CloakEffect
{
    /// <summary>CR 702.168a — a cloaked permanent has ward {2}.</summary>
    public const int CloakWardAmount = 2;

    /// <summary>
    /// CR 702.168 — cloak the top card of <paramref name="controller"/>'s
    /// library. Routes zone moves through <paramref name="zones"/> when
    /// supplied so ETB triggers / replacement effects fire; otherwise falls
    /// back to raw-zone moves. Returns the face-down wrapper put onto the
    /// battlefield, or null when the library was empty.
    /// </summary>
    public static ManifestedCreature? Cloak(Player controller, ZoneService? zones = null)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));

        // Top of library = index 0 (matches Fx.LookAtTopN). Peek one.
        var top = Fx.LookAtTopN(controller, 1);
        if (top.Count == 0) return null;

        var toCloak = top[0];
        controller.Zones.Library.RemoveCard(toCloak);

        return CloakCard(controller, toCloak, zones);
    }

    /// <summary>
    /// CR 702.168 — cloak <paramref name="card"/> (already removed from its
    /// previous zone) under <paramref name="controller"/>: build the shared
    /// face-down 2/2 wrapper (with the manifest "turn face up for its mana
    /// cost if it's a creature" activated ability, CR 708.6) and layer the
    /// cloak ward {2} (CR 702.168a) onto it. Exposed for callers (e.g.
    /// Cryptic Coat's ETB) that cloak a specific card and then need the
    /// resulting wrapper (to attach an Equipment to it, etc.).
    /// </summary>
    public static ManifestedCreature CloakCard(
        Player controller,
        ICard card,
        ZoneService? zones)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));
        if (card is null) throw new ArgumentNullException(nameof(card));

        // Shared manifest wrapper construction — face-down 2/2 + the
        // CR 708.6 "turn face up for its mana cost" activated ability iff
        // the underlying card is a creature.
        var wrapper = ManifestEffect.ManifestCard(controller, card, zones);

        // CR 702.168a — the cloaked permanent additionally has ward {2}.
        // Flagged as face-down-intrinsic so EffectiveAbilities surfaces it
        // even though the underlying card's printed abilities are hidden by
        // CR 708.2.
        var cloakWard = new KeywordAbility(
            "Ward", wrapper, controller, arg: CloakWardAmount);
        wrapper.AddAbility(cloakWard);
        wrapper.MarkFaceDownIntrinsicAbility(cloakWard);

        return wrapper;
    }
}
