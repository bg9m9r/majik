using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boltwave ({R} Sorcery).
///
/// Oracle text:
///   "Boltwave deals 3 damage to each opponent."
///
/// Plain Sorcery — no targets, no trigger, no lifegain. Resolves by dealing
/// 3 damage to every opponent of the caster. Mirrors the each-opponent-damage
/// half of <see cref="CreepingChillFactory.BuildResolveEffect"/> (same
/// <see cref="Fx.DealDamageAny"/> routing, same controller-skip guard).
///
/// The "each opponent" list is supplied by the caller at resolve time —
/// identical pattern to Creeping Chill / Omnath / Meathook Massacre.
/// </summary>
[CardName("Boltwave")]
public static class BoltwaveFactory
{
    public const string CardName    = "Boltwave";
    public const string PrintedManaCost = "{R}";
    public const int    Damage      = 3;

    /// <summary>
    /// Construct Boltwave (shape only, no resolve wiring).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the cast-resolve effect: deal <see cref="Damage"/> (3) to each
    /// player in <paramref name="opponents"/> who is not the
    /// <paramref name="controller"/> (CR 800.4a — "each opponent" means
    /// every player who is currently an opponent of the caster at resolution).
    /// </summary>
    /// <param name="controller">Caster of Boltwave — skipped as a damage
    /// target even if present in the list (defensive guard).</param>
    /// <param name="opponents">Live list of opponents at resolve time.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player controller,
        IReadOnlyList<Player> opponents)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(opponents);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: deal {Damage} to each opponent",
                () =>
                {
                    foreach (var opp in opponents)
                    {
                        if (ReferenceEquals(opp, controller)) continue;
                        Fx.DealDamageAny(opp, Damage);
                    }
                }),
        };
    }
}
