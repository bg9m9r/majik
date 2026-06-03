using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent all damage that {colours} sources would deal to creatures
/// you control this turn." Cancels every <see cref="DamageIntent"/> whose
/// <see cref="DamageIntent.TargetCreature"/> is controlled by the beneficiary
/// player AND whose <see cref="DamageIntent.Source"/> is a card sharing any of
/// the named colours. Backs Surge of Salvation's "black and/or red sources"
/// rider; generalises
/// <see cref="PreventAllDamageFromColoredSourcesToCreatureShield"/> (single
/// fixed creature, single colour) to "every creature the player currently
/// controls" across a SET of colours.
///
/// Source-colour resolution (CR 105.3):
///   - <see cref="ICard"/> source: a battlefield <see cref="Permanent"/> reads
///     its <em>effective</em> colour set (a Layer-5 colour-changing effect is
///     honoured); any other ICard reads its printed colour via
///     <see cref="CardColors.GetColors"/>.
///   - <see cref="Player"/> / unknown source: colourless — the shield does not
///     apply (the printed clause names colours, not "any source").
///
/// Auto-drops at cleanup via <see cref="IEndOfTurnExpirable"/>.
/// </summary>
public sealed class PreventAllDamageFromColoredSourcesToControlledCreaturesShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    private readonly Player _beneficiary;
    private readonly IReadOnlySet<ManaColor> _colors;

    public PreventAllDamageFromColoredSourcesToControlledCreaturesShield(
        Player beneficiary,
        IEnumerable<ManaColor> colors)
    {
        _beneficiary = beneficiary ?? throw new ArgumentNullException(nameof(beneficiary));
        ArgumentNullException.ThrowIfNull(colors);
        _colors = colors.ToHashSet();
    }

    /// <summary>The player whose creatures are shielded.</summary>
    public Player Beneficiary => _beneficiary;

    /// <summary>Colours a source must share (any one) to be prevented.</summary>
    public IReadOnlySet<ManaColor> Colors => _colors;

    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (intent.Amount <= 0) return false;

        // Target must be a creature the beneficiary controls.
        if (intent.TargetCreature is not Permanent target) return false;
        if (!ReferenceEquals(target.Controller, _beneficiary)) return false;

        // CR 105.3 — source colour. ICard sources route through the effective
        // colour (a battlefield Permanent's colour can be changed by a Layer-5
        // effect); Player / unknown sources are colourless and skip the shield.
        if (intent.Source is not ICard sourceCard) return false;
        IReadOnlySet<ManaColor> sourceColors = sourceCard is Permanent perm
            ? perm.GetEffectiveColors()
            : CardColors.GetColors(sourceCard);

        foreach (var c in sourceColors)
        {
            if (_colors.Contains(c)) return true;
        }
        return false;
    }

    // CR 615.1 — prevention cancels the damage entirely.
    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) => null;
}
