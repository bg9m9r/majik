using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.49 — Ninjutsu [cost]: a value-carrier ability marking a card as
/// having Ninjutsu and recording its mana cost. The actual special action
/// (return an unblocked attacker to hand, put this onto the battlefield tapped
/// and attacking) is performed by <see cref="NinjutsuAction.Execute"/>; this
/// marker exposes the mana portion of the cost (<see cref="ManaCost"/>) and
/// keeps the card discoverable on the bot / inspection rails (mirrors the
/// <see cref="Majik.Core.Abilities.KeywordAbility"/> marker posture for
/// evergreen keywords).
///
/// Several Modern cards carry Ninjutsu (Ninja of the Deep Hours
/// {1}{U}, Yuriko, Kaito Bane of Nightmares {1}{U}{B}, …); each attaches one
/// of these with its printed ninjutsu cost.
/// </summary>
public sealed class NinjutsuAbility : Majik.Core.Abilities.IStaticAbility
{
    /// <summary>The mana portion of the ninjutsu cost (CR 702.49 — paid in
    /// addition to returning an unblocked attacker to hand).</summary>
    public ManaCost ManaCost { get; }

    /// <summary>The Ninja card this ability belongs to.</summary>
    public Card Source { get; }

    private readonly Player? _controller;

    public NinjutsuAbility(Card source, ManaCost manaCost, Player? controller = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ManaCost = manaCost ?? throw new ArgumentNullException(nameof(manaCost));
        _controller = controller;
    }

    public NinjutsuAbility(Card source, string manaCost, Player? controller = null)
        : this(source, ManaCost.Parse(manaCost), controller)
    {
    }

    public string Description => $"Ninjutsu {ManaCost}";

    object Majik.Core.Abilities.IStaticAbility.Source => Source;

    Player Majik.Core.Abilities.IStaticAbility.Controller => _controller!;

    public bool IsActive() => true;

    public void ApplyEffect() { /* no continuous mutation — special action read directly */ }
}
