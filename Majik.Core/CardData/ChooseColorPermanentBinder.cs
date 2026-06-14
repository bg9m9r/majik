using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.12 — the NON-LAND analogue of <see cref="ChooseColorLandBinder"/>.
/// Runs in the routed factory-build overlay
/// (<see cref="DeckCardBuilder"/>'s <c>OverlayAdditiveBinders</c>) after a
/// choose-a-color factory (Coldsteel Heart, Utopia Sprawl) has wired its
/// dynamic mana ability / triggered mana ability against a shared
/// <see cref="ColorChoice"/> holder and stashed that holder in
/// <see cref="ColorChoiceRegistry"/>. When such a holder exists, registers an
/// agent-prompting <see cref="ChooseColorReplacement"/> on the game's
/// <see cref="ReplacementBus"/> so the controller picks a colour "as this
/// enters" (CR 614.12) and that pick is stamped onto the holder the ability
/// reads — turning on the printed single-chosen-colour restriction instead of
/// the old eager hard-coded default.
///
/// <para>
/// Lands use <see cref="ChooseColorLandBinder"/> from the binder chain instead
/// (lands are never routed through their factory — <see cref="FactoryRouting"/>);
/// this binder is the live path for the artifact / Aura members of the family.
/// </para>
/// </summary>
public static class ChooseColorPermanentBinder
{
    /// <summary>
    /// Register the ETB choose-color replacement when <paramref name="card"/>'s
    /// factory stashed a <see cref="ColorChoice"/> for it in
    /// <see cref="ColorChoiceRegistry"/>. Returns <see langword="true"/> when a
    /// replacement was registered, <see langword="false"/> otherwise.
    /// </summary>
    public static bool Bind(ICard card, ReplacementBus replacements)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(replacements);

        var choice = ColorChoiceRegistry.Get(card);
        if (choice is null) return false;

        replacements.Register(new ChooseColorReplacement(card, choice));
        return true;
    }
}
