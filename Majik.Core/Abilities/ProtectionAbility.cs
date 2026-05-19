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
/// </summary>
public sealed class ProtectionAbility : IAbility
{
    public string Quality { get; }

    public ProtectionAbility(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            throw new ArgumentException("quality required", nameof(quality));
        Quality = quality.Trim().ToLowerInvariant();
    }

    public string Description => $"Protection from {Quality}";
}
