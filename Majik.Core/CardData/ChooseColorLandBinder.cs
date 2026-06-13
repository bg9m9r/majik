using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.12 — binder-chain wiring for the "as this land enters, choose a
/// color" ETB choice. Runs after <see cref="OracleManaBinder"/> has bound a
/// chosen-colour land's dynamic mana abilities (Sunken Citadel, Temple of the
/// Dragon Queen) and stashed its shared <see cref="ColorChoice"/> holder; when
/// such a holder exists, registers a <see cref="ChooseColorReplacement"/> on the
/// <see cref="ReplacementBus"/> so the controller's agent is prompted to pick a
/// colour as the land enters, and that pick is stamped onto the holder the mana
/// abilities read.
///
/// <para>
/// Lands are never routed through their <c>[CardName]</c> factory in prod
/// (<see cref="FactoryRouting"/>), so this binder is the only live path that
/// turns the printed single-chosen-colour restriction on — closing the gap
/// where the binder bound all five WUBRG colours (strictly more permissive than
/// the card). The artifact (Coldsteel Heart) / aura (Utopia Sprawl) members of
/// the family route through their factories with an eager colour choice and
/// don't need this binder.
/// </para>
/// </summary>
public static class ChooseColorLandBinder
{
    /// <summary>
    /// Register the ETB choose-color replacement when <paramref name="card"/> is
    /// a chosen-colour land (i.e. <see cref="OracleManaBinder"/> created a
    /// <see cref="ColorChoice"/> for it). Returns <see langword="true"/> when a
    /// replacement was registered, <see langword="false"/> otherwise.
    /// </summary>
    public static bool Bind(ICard card, ReplacementBus replacements)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(replacements);

        if (card is not Land land) return false;
        var choice = OracleManaBinder.GetColorChoice(land);
        if (choice is null) return false;

        replacements.Register(new ChooseColorReplacement(card, choice));
        return true;
    }
}
