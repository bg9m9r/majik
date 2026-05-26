using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seething Song (Mirage / Modern Masters,
/// {2}{R}).
///
/// Instant. Oracle text:
///   "Add {R}{R}{R}{R}{R}."
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
/// Net-mana posture: Seething Song costs {2}{R} and produces
/// {R}{R}{R}{R}{R}. Net = +{R}{R} (two red mana ahead of cost) per
/// cast. Same colour-of-mana net as <see cref="DesperateRitualFactory"/>
/// / <see cref="PyreticRitualFactory"/>, but front-loaded for higher
/// burst — the canonical Storm ritual when the deck needs a five-red
/// jump to fuel Past in Flames / Grapeshot / Empty the Warrens.
///
/// Banned in Modern, but lives in Legacy Storm and the embedded card
/// pool needs full coverage of the ritual cycle for completeness.
/// </summary>
[CardName("Seething Song")]
public static class SeethingSongFactory
{
    public const string CardName = "Seething Song";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>
    /// Output: add five red mana.
    /// </summary>
    public const string ManaProduced = "RRRRR";

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time {R}{R}{R}{R}{R} mana production.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Seething Song's resolve effect. On resolution, add five red
    /// mana to <paramref name="controller"/>'s mana pool.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new IEffect[]
        {
            new Effect("Seething Song: add {R}{R}{R}{R}{R}.", () =>
            {
                controller.AddManaToPool(ManaCost.Parse(ManaProduced));
            }),
        };
    }
}
