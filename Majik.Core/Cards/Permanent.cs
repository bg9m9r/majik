using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Base class for permanent cards (cards that stay on the battlefield).
/// Includes: Creatures, Lands, Enchantments, Artifacts, Planeswalkers.
/// </summary>
public class Permanent : Card
{
    private bool _isTapped;
    private bool _hasSummoningSickness;

    /// <summary>
    /// Whether the permanent is tapped.
    /// </summary>
    public bool IsTapped
    {
        get => _isTapped;
        private set => _isTapped = value;
    }

    /// <summary>
    /// Whether this permanent has summoning sickness.
    /// </summary>
    public bool HasSummoningSickness
    {
        get => _hasSummoningSickness;
        set => _hasSummoningSickness = value;
    }

    public Permanent(string name, string manaCost, IEnumerable<CardType> cardTypes, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, cardTypes, supertypes, subtypes)
    {
        _isTapped = false;
        _hasSummoningSickness = true;
    }

    /// <summary>
    /// Tap the permanent.
    /// </summary>
    public void Tap()
    {
        if (_isTapped)
        {
            throw new InvalidOperationException("Permanent is already tapped");
        }

        _isTapped = true;
    }

    /// <summary>
    /// Untap the permanent.
    /// </summary>
    public void Untap()
    {
        if (!_isTapped)
        {
            throw new InvalidOperationException("Permanent is not tapped");
        }

        _isTapped = false;
    }
}
