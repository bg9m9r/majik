using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rite of Flame (Coldsnap, {R}).
///
/// Sorcery. Oracle text:
///   "Add {R}{R}. Then add {R} for each card named Rite of Flame in
///    your graveyard."
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
/// Net-mana posture: Rite of Flame costs {R} and produces {R}{R} +
/// {R} per copy already in the graveyard. The first cast nets +{R}
/// (same as Dark Ritual); each subsequent cast adds another +{R} on
/// top, so a chain of three Rites of Flame nets +{R}{R}{R}{R}{R}{R}
/// across the chain (storm-fuel scaling).
///
/// ## Self-counting note (CR 608.2)
///
/// The resolving Rite of Flame itself is on the stack — NOT yet in the
/// graveyard — when its effect resolves. CR 608.2f: a spell's effects
/// resolve before the spell is put into its owner's graveyard
/// (CR 608.2m moves it after). So a Rite of Flame cast with N copies
/// already in the graveyard produces {R}{R} + {R}×N; the spell itself
/// doesn't contribute to its own bonus.
///
/// Banned in Modern (Coldsnap-era ritual), but kept in the embedded
/// card pool for Legacy / EDH coverage and to mirror the ritual cycle
/// alongside <see cref="DesperateRitualFactory"/> /
/// <see cref="PyreticRitualFactory"/> / <see cref="SeethingSongFactory"/>.
/// </summary>
[CardName("Rite of Flame")]
public static class RiteOfFlameFactory
{
    public const string CardName = "Rite of Flame";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Base output: two red mana.
    /// </summary>
    public const string BaseManaProduced = "RR";

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time mana production with the
    /// graveyard-scaling bonus.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Rite of Flame's resolve effect. On resolution, add {R}{R}
    /// then add {R} for each card named "Rite of Flame" in
    /// <paramref name="controller"/>'s graveyard (CR 608.2f — count is
    /// sampled at resolution before the spell hits the graveyard).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new IEffect[]
        {
            new Effect("Rite of Flame: add {R}{R}, then {R} per Rite of Flame in graveyard.", () =>
            {
                controller.AddManaToPool(ManaCost.Parse(BaseManaProduced));

                var bonus = CountCopiesInGraveyard(controller);
                for (var i = 0; i < bonus; i++)
                {
                    controller.AddManaToPool(ManaCost.Parse("R"));
                }
            }),
        };
    }

    /// <summary>
    /// Count of cards named <see cref="CardName"/> in
    /// <paramref name="controller"/>'s graveyard. Used to compute the
    /// graveyard-scaling bonus on resolution.
    /// </summary>
    public static int CountCopiesInGraveyard(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard.GetCards()
            .Count(c => string.Equals(c.Name, CardName, StringComparison.Ordinal));
    }
}
