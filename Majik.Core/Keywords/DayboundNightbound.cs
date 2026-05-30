using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.145 — Daybound and Nightbound. Daybound is found on the front
/// faces of some double-faced cards; nightbound on the back faces. Together
/// they model the MID/VOW Werewolf transform pattern (CR 730 "Day and
/// Night"):
///   702.145b — "If it is night and this permanent is represented by a
///               double-faced card, it enters transformed." (the daybound
///               static ability, applied at entry).
///   702.145c — "Any time a player controls a permanent that is front face
///               up with daybound and it's night, that player transforms
///               that permanent." (not a state-based action).
///   702.145d — "Any time a player controls a permanent with daybound, if
///               it's neither day nor night, it becomes day."
///   702.145e/f — Nightbound mirror: back face up + day → transform to
///               front.
///
/// This helper is the transform engine: it scans the daybound/nightbound
/// <see cref="KeywordAbility"/> markers a factory attaches and flips the
/// card's <see cref="CardData.MDFCs.MdfcState"/> face accordingly. As with
/// every other v1 DFC (Delver, Ajani), the MdfcState flip is the observation
/// surface — full Layer-0 per-face characteristic hot-swap (back-face P/T,
/// keywords) is deferred.
///
/// Both faces of a Werewolf carry a marker (daybound on the front face,
/// nightbound on the back face), so transform decisions are gated on the
/// CURRENT face (CR 702.145c "front face up", 702.145f "back face up"), not
/// merely on marker presence.
/// </summary>
public static class DayboundNightbound
{
    public const string DayboundKeyword = "Daybound";
    public const string NightboundKeyword = "Nightbound";

    /// <summary>True iff <paramref name="card"/> has the daybound keyword
    /// marker (CR 702.145b — front face of a transforming DFC).</summary>
    public static bool HasDaybound(Card card) => HasKeyword(card, DayboundKeyword);

    /// <summary>True iff <paramref name="card"/> has the nightbound keyword
    /// marker (CR 702.145e — back face of a transforming DFC).</summary>
    public static bool HasNightbound(Card card) => HasKeyword(card, NightboundKeyword);

    private static bool HasKeyword(Card card, string keyword) =>
        card?.Abilities?.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase)) ?? false;

    /// <summary>
    /// CR 702.145c / 702.145f — apply the day/night transform across a set of
    /// permanents. When it becomes night, every front-face-up daybound
    /// permanent transforms to its back face. When it becomes day, every
    /// back-face-up nightbound permanent transforms to its front face.
    /// "Neither" never transforms anything (CR 730.2c).
    /// </summary>
    public static void OnDayNightChanged(IEnumerable<Card> permanents, DayNightDesignation designation)
    {
        if (permanents == null) return;
        foreach (var card in permanents)
        {
            var mdfc = card?.MdfcState;
            if (mdfc == null) continue;

            // CR 702.145c — daybound + front face up + night → transform.
            if (designation == DayNightDesignation.Night
                && !mdfc.IsBackFace
                && HasDaybound(card!))
            {
                mdfc.Transform();
                continue;
            }

            // CR 702.145f — nightbound + back face up + day → transform.
            if (designation == DayNightDesignation.Day
                && mdfc.IsBackFace
                && HasNightbound(card!))
            {
                mdfc.Transform();
            }
        }
    }

    /// <summary>
    /// CR 702.145b — a daybound double-faced permanent enters transformed if
    /// it's night. Daybound permanents enter front-face up when it's day or
    /// neither. Idempotent: only flips to the back face when it's night and
    /// the card is currently front-face up.
    /// </summary>
    public static void OnEnter(Card card, DayNightDesignation designation)
    {
        var mdfc = card?.MdfcState;
        if (mdfc == null) return;

        if (designation == DayNightDesignation.Night
            && !mdfc.IsBackFace
            && HasDaybound(card!))
        {
            mdfc.Transform();
        }
    }

    /// <summary>
    /// CR 702.145d / 702.145g — the designation a permanent forces when it's
    /// neither day nor night. A daybound permanent makes it day (702.145d);
    /// a nightbound-only permanent makes it night (702.145g, simplified: no
    /// daybound-on-battlefield cross-check is needed for a single Werewolf
    /// whose active face is nightbound). When it's already day or night this
    /// returns the current designation unchanged (the check only fires while
    /// it's neither).
    /// </summary>
    public static DayNightDesignation EntryDesignation(Card card, DayNightDesignation current)
    {
        if (current != DayNightDesignation.Neither) return current;

        var mdfc = card?.MdfcState;

        // CR 702.145d — a permanent whose active (front) face is daybound
        // makes it day. A Werewolf carries both markers; the daybound face
        // is the front, so it forces day while front-face up.
        if (HasDaybound(card!) && (mdfc == null || !mdfc.IsBackFace))
        {
            return DayNightDesignation.Day;
        }

        // CR 702.145g — a permanent whose active (back) face is nightbound
        // makes it night.
        if (HasNightbound(card!) && mdfc != null && mdfc.IsBackFace)
        {
            return DayNightDesignation.Night;
        }

        return current;
    }
}
