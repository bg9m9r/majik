using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.1d — "this permanent enters the battlefield with N +1/+1 counters
/// on it" replacement. Watches the card's own ETB
/// <see cref="ZoneMoveIntent"/> and sets
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> = <c>n</c> so
/// <see cref="Services.ZoneService"/> adds the counters after landing.
///
/// Covers cards like Strangleroot Geist (enters with one), Triskelion
/// (three), Hangarback Walker (zero — irrelevant), Glimmer Bairn, Yuna
/// Grand Summoner, etc. Variable-X variants (Walking Ballista's {X})
/// require threading <c>ChosenSpellParams.X</c> through the intent —
/// deferred to a follow-up.
/// </summary>
public sealed class EntersWithCountersReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;
    private readonly int _amount;

    public EntersWithCountersReplacement(ICard card, int amount)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _amount = amount;
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        // Additive — a card that stacks two ETB-counter sources (printed +
        // anthem-style external buff) accumulates counts instead of
        // clobbering. Today only one source per card so this is moot, but
        // the additive shape future-proofs Hardened Scales-style interactions.
        intent with { PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + _amount };
}
