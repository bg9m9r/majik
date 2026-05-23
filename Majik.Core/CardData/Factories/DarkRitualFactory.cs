using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dark Ritual (Alpha and many reprints, {B}).
///
/// Instant. Oracle text:
///   "Add {B}{B}{B}."
///
/// ## Implementation
///
/// Card shape only here; the resolve-time effect is built on-demand via
/// <see cref="BuildResolveEffect"/> so tests / integrations can plug it
/// into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
/// directly to a <see cref="Majik.Core.Spells.Spell"/>.
///
/// Mana goes into <see cref="Player.ManaPool"/> via
/// <see cref="Player.AddManaToPool(ManaCost)"/>. The pool follows
/// CR 106.4 / CR 500.4 — produced mana lives until the end of the
/// current step/phase.
///
/// Sibling of <see cref="CabalRitualFactory"/> minus the threshold
/// clause — straight three-black ritual every resolution.
/// </summary>
public static class DarkRitualFactory
{
    public const string CardName = "Dark Ritual";
    public const string PrintedManaCost = "{B}";

    /// <summary>
    /// Output: add three black mana.
    /// </summary>
    public const string ManaProduced = "BBB";

    /// <summary>
    /// Build a Dark Ritual instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildResolveEffect"/> for the
    /// resolve-time mana production.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Dark Ritual's resolve effect. On resolution, add three black
    /// mana to <paramref name="controller"/>'s mana pool.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new IEffect[]
        {
            new Effect("Dark Ritual: add {B}{B}{B}.", () =>
            {
                controller.AddManaToPool(ManaCost.Parse(ManaProduced));
            }),
        };
    }
}
