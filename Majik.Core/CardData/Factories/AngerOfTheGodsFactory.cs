using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anger of the Gods (Theros, {2}{R}).
///
/// Sorcery. Oracle text:
///   "Anger of the Gods deals 3 damage to each creature. If a creature
///    dealt damage this way would die this turn, exile it instead."
///
/// ## Implementation
///
/// Card shape only at the dispatcher; the on-resolve effect is built on
/// demand via <see cref="BuildResolveEffect"/>. Two halves, sequenced:
///
/// 1. <b>Sweep</b>: deal 3 damage to every creature on every supplied
///    player's battlefield (CR 109.5) — same shape as
///    <see cref="PyroclasmFactory.BuildResolveEffect"/>. Damaged creatures
///    are tracked in a "damaged this way" set so the rider only catches
///    creatures the sweep itself hit (CR 700.3 — "dealt damage this way"
///    refers back to the specific damage event of the same spell's
///    resolution).
/// 2. <b>Exile rider</b>: register an
///    <see cref="AngerOfTheGodsExileInsteadReplacement"/> on the supplied
///    <see cref="ReplacementBus"/>. The replacement watches every
///    <see cref="ZoneMoveIntent"/> headed to <see cref="ZoneType.Graveyard"/>
///    whose source is the battlefield, gated to the "damaged this way"
///    set populated by step 1. On match it rewrites the destination to
///    <see cref="ZoneType.Exile"/>. The replacement is
///    <see cref="IEndOfTurnExpirable"/>, so the bus's
///    <see cref="ReplacementBus.ExpireEndOfTurn"/> cleanup-step sweep
///    drops it at the end of the turn Anger of the Gods resolved
///    (CR 514.2).
///
/// ## Why a named factory (over the existing template)
/// The shared <c>DealsDamageEachCreatureTemplate</c> already binds the
/// first sentence of Anger of the Gods by shape, but it (a) scans only
/// the caster's battlefield (same gap as Pyroclasm — see that factory)
/// and (b) doesn't carry the "would die → exile" rider at all. The named
/// factory fixes both: wider scan + rider registration in one
/// <see cref="IEffect"/>.
///
/// ## v1 simplifications
/// - <b>"Damaged this way" scope</b>: tracked via reference identity on a
///   <see cref="HashSet{Creature}"/> populated during the sweep. A
///   creature damaged by Anger that then leaves the battlefield and
///   returns later in the same turn would lose the "would die" rider
///   even if the printed text could be read to keep tracking it. Modern
///   judging treats the "damaged this way" rider as expiring on zone
///   change (CR 400.7), so this is correct.
/// - <b>SBA path</b>: the printed "would die" reaches
///   <see cref="ZoneType.Graveyard"/> via
///   <see cref="Majik.Core.Rules.Sba.Checks.CreatureDeathCheck"/>, which
///   routes the move through
///   <see cref="Majik.Core.Services.ZoneService.MoveCardTo"/> →
///   <see cref="ReplacementBus.Apply{TIntent}"/> on a
///   <see cref="ZoneMoveIntent"/>. The rider therefore catches "lethal
///   damage from Anger's sweep" naturally without needing to hook
///   <see cref="DestroyIntent"/>. Sacrifices / hard-destroys hitting the
///   tagged creatures later in the turn are also redirected to exile,
///   which matches the printed rider.
/// - <b>Cross-controller scope</b>: the rider rewrites every tagged
///   creature's graveyard-bound zone move regardless of who controls it
///   — matching the printed "If a creature dealt damage this way would
///   die" (no controller restriction).
/// </summary>
[CardName("Anger of the Gods")]
public static class AngerOfTheGodsFactory
{
    public const string CardName = "Anger of the Gods";
    public const string PrintedManaCost = "{2}{R}";
    public const int Damage = 3;

    /// <summary>
    /// Build an Anger of the Gods sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect (sweep + exile rider) is
    /// built on demand via <see cref="BuildResolveEffect"/>.
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
    /// Build Anger of the Gods's resolve effect. Two halves:
    ///   1. Sweep 3 damage to every creature on every supplied player's
    ///      battlefield (CR 109.5), recording each hit creature so the
    ///      rider can scope its "would die → exile" replacement.
    ///   2. If <paramref name="replacements"/> is non-null, register an
    ///      EOT-expirable <see cref="AngerOfTheGodsExileInsteadReplacement"/>
    ///      that redirects each tagged creature's graveyard move to
    ///      exile. When <paramref name="replacements"/> is null, the
    ///      rider half is skipped (suitable for the simplest shape
    ///      tests — sweep still applies).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"Anger of the Gods: deal {Damage} damage to each creature; tagged dies → exile until EOT.",
                () =>
                {
                    // ---------------------------------------------------------
                    // Step 1 — sweep. Track every creature the sweep actually
                    // damages so the rider can gate its replacement on that
                    // set (CR 700.3 — "dealt damage this way" = damaged by
                    // this specific damage event).
                    // ---------------------------------------------------------
                    var damaged = new HashSet<Creature>();
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                        {
                            if (damaged.Add(c)) c.TakeDamage(Damage);
                        }
                    }

                    // ---------------------------------------------------------
                    // Step 2 — exile rider. Register the EOT-expirable
                    // replacement; the bus's cleanup-step sweep removes it
                    // automatically (CR 514.2).
                    // ---------------------------------------------------------
                    if (replacements != null)
                    {
                        replacements.Register<ZoneMoveIntent>(
                            new AngerOfTheGodsExileInsteadReplacement(damaged));
                    }
                }),
        };
    }
}

/// <summary>
/// Replacement effect: if a creature damaged by Anger of the Gods would
/// move from <see cref="ZoneType.Battlefield"/> to
/// <see cref="ZoneType.Graveyard"/>, rewrite the destination to
/// <see cref="ZoneType.Exile"/>. Scoped to the tagged set populated by
/// the sweep so other creatures that die this turn are unaffected
/// (CR 700.3). EOT-expirable per CR 514.2.
/// </summary>
public sealed class AngerOfTheGodsExileInsteadReplacement
    : IReplacementEffect<ZoneMoveIntent>, IEndOfTurnExpirable
{
    private readonly HashSet<Creature> _damaged;

    public AngerOfTheGodsExileInsteadReplacement(HashSet<Creature> damaged)
    {
        _damaged = damaged ?? throw new ArgumentNullException(nameof(damaged));
    }

    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    /// <summary>The "damaged this way" set the replacement is scoped to.</summary>
    public IReadOnlyCollection<Creature> Damaged => _damaged;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (intent.FromZone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Graveyard) return false;
        return intent.Card is Creature creature && _damaged.Contains(creature);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
