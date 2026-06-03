using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 701.5b — controller-scoped "spells you control can't be countered"
/// static marker (Destiny Spinner: "Creature and enchantment spells you
/// control can't be countered."; the wider Vexing-Shusher-style cluster).
///
/// This is a value-carrier marker in the same family as
/// <see cref="KeywordAbility"/>: it performs no continuous mutation of its
/// own. Instead <see cref="Majik.Core.Game.SpellCastFlow"/> scans the
/// caster's battlefield at cast time and, when a live (battlefield-gated)
/// marker controlled by the caster <see cref="Covers"/> one of the cast
/// card's types, stamps <see cref="Majik.Core.Spells.Spell.CannotBeCountered"/>
/// on the resolving spell. Every downstream counter primitive
/// (<c>Fx.Counter</c> + the counter templates) and
/// <c>OracleSpellBinder.RemoveFromStack</c> already honour that flag, so the
/// spell becomes uncounterable.
///
/// The set of <see cref="CardTypes"/> restricts the static to a spell type
/// subset (Destiny Spinner = {Creature, Enchantment}). An empty set means the
/// static applies to <b>every</b> spell the controller casts (the unrestricted
/// "spells you control can't be countered" wording).
///
/// Battlefield gating (the static is only live while its source permanent is on
/// the battlefield, CR 603/611-style) is enforced at the read site in
/// <see cref="Majik.Core.Game.SpellCastFlow"/>, which is why this marker's
/// <see cref="IsActive"/> simply returns <c>true</c> (mirrors
/// <see cref="KeywordAbility"/>).
/// </summary>
public sealed class UncounterableControllerStatic : IStaticAbility
{
    private readonly HashSet<CardType> _cardTypes;

    public object Source { get; }
    public Player Controller { get; }

    /// <summary>
    /// Spell card types this static covers. Empty = applies to every spell the
    /// controller casts (unrestricted wording).
    /// </summary>
    public IReadOnlySet<CardType> CardTypes => _cardTypes;

    public string Description =>
        _cardTypes.Count == 0
            ? "Spells you control can't be countered"
            : $"{string.Join(" and ", _cardTypes)} spells you control can't be countered";

    public UncounterableControllerStatic(
        object source,
        Player controller,
        IEnumerable<CardType>? cardTypes = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _cardTypes = cardTypes is null ? new HashSet<CardType>() : new HashSet<CardType>(cardTypes);
    }

    /// <summary>
    /// Does this static cover <paramref name="spellCardTypes"/>? True when the
    /// static is unrestricted (empty <see cref="CardTypes"/>) or when at least
    /// one of the spell's card types is in the covered set.
    /// </summary>
    public bool Covers(IEnumerable<CardType> spellCardTypes)
    {
        if (_cardTypes.Count == 0) return true;
        return spellCardTypes.Any(_cardTypes.Contains);
    }

    public bool IsActive() => true;

    public void ApplyEffect() { /* no continuous mutation — SpellCastFlow reads the marker */ }
}
