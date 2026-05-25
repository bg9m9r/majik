namespace Majik.Core.Abilities;

/// <summary>
/// CR 702.16 — Protection from <em>quality</em>. "Quality" can be a colour
/// ("red"), a card type ("creatures"), a card name ("Bolas"), or another
/// descriptor. Effects of protection (CR 702.16e — DEBT-A): the creature
/// can't be Damaged, Enchanted/Equipped, Blocked, or Targeted by anything
/// matching the quality, and any Attached objects matching are unattached.
///
/// MVP: stores quality as a normalised lowercase string. Higher-level
/// helpers in <see cref="Majik.Core.Rules.Protection"/> interpret it.
///
/// Cards whose protection clause can't be reduced to a single colour /
/// type / name token (Emrakul, the Aeons Torn — "protection from coloured
/// spells"; future "protection from multicoloured", "from non-Eldrazi", …)
/// pass an optional <see cref="SpellPredicate"/>; counter-/targeting-side
/// gates that hold a live <see cref="Majik.Core.Spells.ISpell"/> handle
/// invoke the predicate directly. The legacy string-only ctor remains the
/// canonical shape for the colour / type / name cases that
/// <see cref="Majik.Core.Rules.Protection"/> already covers.
/// </summary>
public sealed class ProtectionAbility : IAbility
{
    public string Quality { get; }

    /// <summary>
    /// Optional predicate over a resolving spell — true means
    /// "protection applies, this spell can't target / damage / etc. the
    /// protected permanent". Null for the string-only cases handled by
    /// <see cref="Majik.Core.Rules.Protection"/>.
    /// </summary>
    public Func<Majik.Core.Spells.ISpell, bool>? SpellPredicate { get; }

    /// <summary>
    /// Optional "is this protection currently active?" gate — used for
    /// conditional protection clauses like Etched Champion's Metalcraft
    /// rider ("As long as you control three or more artifacts, Etched
    /// Champion has protection from all colors"). Null means
    /// "always active" — the legacy default that every plain protection
    /// rider (Goblin Piledriver / Sword of Fire and Ice / Emrakul) uses.
    /// When supplied, every Protection-helper read (colour / type / spell
    /// predicate) skips this ability when the gate returns false.
    /// </summary>
    public Func<bool>? IsActive { get; }

    public ProtectionAbility(string quality)
        : this(quality, spellPredicate: null, isActive: null) { }

    public ProtectionAbility(
        string quality,
        Func<Majik.Core.Spells.ISpell, bool>? spellPredicate)
        : this(quality, spellPredicate, isActive: null) { }

    public ProtectionAbility(
        string quality,
        Func<Majik.Core.Spells.ISpell, bool>? spellPredicate,
        Func<bool>? isActive)
    {
        if (string.IsNullOrWhiteSpace(quality))
            throw new ArgumentException("quality required", nameof(quality));
        Quality = quality.Trim().ToLowerInvariant();
        SpellPredicate = spellPredicate;
        IsActive = isActive;
    }

    /// <summary>
    /// True when this protection clause is currently active — every
    /// always-on protection returns true; conditional shapes (Etched
    /// Champion's Metalcraft) defer to their <see cref="IsActive"/>
    /// closure.
    /// </summary>
    public bool IsCurrentlyActive => IsActive?.Invoke() ?? true;

    public string Description => $"Protection from {Quality}";
}
