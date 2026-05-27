using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Parallel Lives (Innistrad, {3}{G}).
///
/// Enchantment. Oracle text:
///   "If an effect would create one or more tokens under your control,
///    it creates twice that many of those tokens instead."
///
/// ## Implementation
///
/// Functionally identical to <see cref="AnointedProcessionFactory"/> —
/// CR 614 replacement on <see cref="TokenCreationIntent"/>, gated on
/// controller-match. The two cards stack multiplicatively (CR 616.1c —
/// each replacement fires once per intent), so Parallel Lives +
/// Anointed Procession shipped-1 = 4 tokens.
/// </summary>
[CardName("Parallel Lives")]
public static class ParallelLivesFactory
{
    public const string CardName = "Parallel Lives";
    public const string PrintedManaCost = "{3}{G}";

    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            replacements.Register<TokenCreationIntent>(new TokenDoublerReplacement(
                intent => card.Zone == Majik.Core.Zones.ZoneType.Battlefield
                          && ReferenceEquals(intent.Controller, owner)));
        }

        return card;
    }
}
