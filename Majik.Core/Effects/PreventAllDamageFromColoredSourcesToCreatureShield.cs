using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent all damage that would be dealt to target creature this
/// turn by sources of {color}." Cancels every <see cref="DamageIntent"/>
/// whose <see cref="DamageIntent.TargetCreature"/> is the chosen creature
/// AND whose <see cref="DamageIntent.Source"/> is a card with the named
/// colour among its <see cref="CardColors.GetColors"/> set.
///
/// Backs Burrenton Forge-Tender's sac-self activated ability ("Prevent all
/// damage that would be dealt to target creature this turn by red sources",
/// CR 615 + CR 105) and any future "from {color} sources" prevention. The
/// shield is registered per activation and auto-drops at cleanup via
/// <see cref="IEndOfTurnExpirable"/>.
///
/// Source-colour resolution (CR 105):
///   - <see cref="ICard"/> source: read colour set via
///     <see cref="CardColors.GetColors"/>. Tokens use their explicit
///     <see cref="Card.TokenColorsOverride"/> when set.
///   - <see cref="Player"/> source (legacy spell-damage threading): the
///     player is treated as colourless — the printed clause speaks of
///     "sources of {color}", and a Player is not a coloured source. This
///     matches the Soul-Scar Mage source-routing convention; once spell
///     damage threads the ISpell / casting card as <see cref="DamageIntent.Source"/>
///     the colour read just works.
///   - Anything else: colourless, shield does not apply.
/// </summary>
public sealed class PreventAllDamageFromColoredSourcesToCreatureShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    private readonly Creature _target;
    private readonly ManaColor _color;

    public PreventAllDamageFromColoredSourcesToCreatureShield(
        Creature target,
        ManaColor color)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _color = color;
    }

    /// <summary>The creature being shielded.</summary>
    public Creature Target => _target;

    /// <summary>Colour the source must share to be prevented.</summary>
    public ManaColor Color => _color;

    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (intent.Amount <= 0) return false;
        if (!ReferenceEquals(intent.TargetCreature, _target)) return false;

        // CR 105.3 — source colour. ICard sources route through the
        // effective colour (a battlefield Permanent's colour can be changed
        // by a Layer-5 effect); Player or unknown sources are treated as
        // colourless and skip the shield (the printed clause names a colour,
        // not "any source").
        if (intent.Source is not ICard sourceCard) return false;
        IReadOnlySet<ManaColor> colors = sourceCard is Permanent perm
            ? perm.GetEffectiveColors()
            : CardColors.GetColors(sourceCard);
        return colors.Contains(_color);
    }

    // CR 615.1 — prevention cancels the damage entirely.
    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) => null;
}
