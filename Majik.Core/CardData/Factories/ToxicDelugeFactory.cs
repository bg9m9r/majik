using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Toxic Deluge (Commander 2013, {2}{B}).
///
/// Sorcery. Oracle text (Scryfall):
///   "As an additional cost to cast this spell, pay X life.
///    All creatures get -X/-X until end of turn."
///
/// ## Implementation (v1)
///
/// Card shape at the dispatcher; the sweep is built on demand via
/// <see cref="BuildResolveEffect"/>. The caller supplies the chosen
/// X value (the printed "pay X life" additional cost) at resolve-build
/// time — see <see cref="DefaultX"/> for the v1 fallback.
///
/// Resolve has two halves:
///
/// 1. <b>Pay X life</b> (CR 118.8 + CR 601.2f — additional cost paid at
///    cast time; in v1 the payment is folded into the resolve effect
///    because the engine doesn't yet expose a spell-time "additional
///    cost" hook for sorceries — see the deferred section below). Routes
///    through <see cref="Player.LoseLife"/> so any life-loss replacement
///    / triggers (Sanguine Bond, Vito, Vizkopa Guildmage, etc.) fire as
///    expected. Skipped when <paramref name="caster"/> is null
///    (suitable for the simplest shape tests — sweep still applies).
/// 2. <b>-X/-X EOT sweep</b>: register a
///    <see cref="PumpUntilEndOfTurnEffect"/>(c, -X, -X) per
///    <see cref="Creature"/> on every supplied player's battlefield
///    against the engine's per-creature continuous-effects service
///    (<see cref="Card.ActiveEffects"/>). Layer 7c modify with EOT
///    expiry (CR 613.4 / CR 514.2). Same shape every -N/-N sweep uses
///    (mirrors <see cref="DecreeOfPainFactory.BuildCycleEffect"/> and
///    <see cref="LanguishFactory.BuildResolveEffect"/>).
///
/// ## Why a named factory (over the existing template)
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Counters.AllCreaturesPumpTemplate"/>
/// only binds fixed -N/-N text (e.g. Languish, Infest); the printed
/// Toxic Deluge text references "X" twice (additional cost + sweep
/// magnitude) which the regex template doesn't model. The named factory
/// carries both halves (life payment + magnitude-driven sweep) in one
/// <see cref="IEffect"/> sequence with the caller-chosen X plumbed
/// through explicitly.
///
/// ## v1 simplifications (deferred)
/// - <b>Variable X at cast time</b>: the engine doesn't yet expose a
///   spell-time "as an additional cost" hook for sorceries (today only
///   activated abilities take <see cref="Majik.Core.Costs.PayLifeCost"/>
///   in their cost list). The factory therefore takes X as a parameter
///   on <see cref="BuildResolveEffect"/>; callers (the bot, tests,
///   future spell-cost pipeline) pick X. Default is
///   <see cref="DefaultX"/> (5) — a reasonable midrange wrath value.
///   When the spell-cost pipeline adds a variable-X life additional-cost
///   shape, the factory should switch to driving X off the cast-time
///   choice instead of <see cref="BuildResolveEffect"/>'s parameter, and
///   the life payment should move out of the resolve effect into the
///   cast-time cost step (CR 601.2f).
/// - <b>Pay-life ordering</b>: in proper play the life payment happens
///   at cast time, not on resolve, so a Sanguine Bond-style life-loss
///   trigger fires when the spell is cast — not when it resolves. The
///   v1 fold-into-resolve approximation gets the totals right but the
///   trigger queues onto a resolving spell instead of an empty-after-
///   cast stack. Same surface every "additional cost folded into
///   resolve" deferral takes in the existing factory pool.
/// - <b>Indestructible / protection</b>: not handled here — the layer
///   system applies -X/-X uniformly; SBA death is the standard
///   toughness-0 check (CR 704.5f). Indestructible creatures with
///   positive base toughness don't die from -X/-X going to ≤ 0 unless
///   X ≥ base toughness AND not indestructible (Rule 704.5f — checks
///   toughness, not damage). Standard layer-system semantics.
///
/// CR rule references: 107.1b (X in costs / effects), 109.5 (symmetric
/// sweep), 117.5 (mana cost), 118.8 (life payment), 514.2 (EOT cleanup),
/// 601.2f (additional costs at cast time), 613.4 (continuous-effects
/// layer 7c), 704.5f (toughness 0 creature-death SBA).
/// </summary>
[CardName("Toxic Deluge")]
public static class ToxicDelugeFactory
{
    public const string CardName = "Toxic Deluge";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>
    /// Default X used when the caller doesn't supply one. Midrange
    /// wrath value — wipes most aggro / midrange creatures (toughness
    /// ≤ 5) without burning the caster down. Used by the v1 fallback;
    /// the bot / future spell-cost pipeline can override.
    /// </summary>
    public const int DefaultX = 5;

    /// <summary>
    /// Build a Toxic Deluge sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect (pay X life + -X/-X sweep)
    /// is built on demand via <see cref="BuildResolveEffect"/>.
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
    /// Build Toxic Deluge's resolve effect.
    ///
    /// Two halves, sequenced:
    /// <list type="number">
    ///   <item>If <paramref name="caster"/> is non-null, pay
    ///     <paramref name="x"/> life via
    ///     <see cref="Player.LoseLife"/> (v1 fold-into-resolve; see
    ///     class doc for the cast-time deferral). The payment is
    ///     capped to the caster's remaining life total — at zero life
    ///     the loop short-circuits, matching the engine's
    ///     <see cref="Player.LifeTotal"/> floor.</item>
    ///   <item>Register a <see cref="PumpUntilEndOfTurnEffect"/>(c,
    ///     -X, -X) per creature on every supplied player's battlefield
    ///     (CR 109.5 — symmetric sweep). EOT cleanup is handled by the
    ///     shared layer-system expiry (CR 514.2).</item>
    /// </list>
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>.</param>
    /// <param name="caster">The casting player who pays X life. When
    /// null, the life payment is skipped (shape-only tests — sweep
    /// still applies).</param>
    /// <param name="x">The chosen X — magnitude of the life payment AND
    /// the -X/-X sweep. Must be non-negative; defaults to
    /// <see cref="DefaultX"/>.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers,
        Player? caster = null,
        int x = DefaultX)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        if (x < 0)
            throw new ArgumentOutOfRangeException(nameof(x),
                "Toxic Deluge X must be non-negative (CR 107.1b — X in costs).");

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: pay {x} life; all creatures -{x}/-{x} EOT",
                () =>
                {
                    // ---------------------------------------------------------
                    // Step 1 — pay X life (CR 118.8). Folded into resolve in
                    // v1; the proper cast-time additional-cost hook isn't
                    // wired yet (see class doc). Routes through LoseLife so
                    // any life-loss replacement / trigger fires.
                    // ---------------------------------------------------------
                    if (caster != null && x > 0)
                    {
                        var paid = Math.Min(x, caster.LifeTotal);
                        if (paid > 0) caster.LoseLife(paid);
                    }

                    // ---------------------------------------------------------
                    // Step 2 — symmetric -X/-X sweep (CR 109.5 + CR 613.4).
                    // Same shape every -N/-N sweep uses; EOT cleanup runs
                    // through the shared layer-system expiry (CR 514.2).
                    // ---------------------------------------------------------
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                        {
                            if (c.ActiveEffects != null)
                            {
                                c.ActiveEffects.Register(
                                    new PumpUntilEndOfTurnEffect(c, -x, -x));
                            }
                        }
                    }
                }),
        };
    }
}
