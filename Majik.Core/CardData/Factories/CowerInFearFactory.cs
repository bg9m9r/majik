using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cower in Fear (Innistrad / reprints, {1}{B}{B}).
///
/// Instant. Oracle text:
///   "Creatures your opponents control get -1/-1 until end of turn."
///
/// ## Implementation (v1)
///
/// Card shape: Instant, mana cost {1}{B}{B}, no targets — built via the
/// fluent <see cref="CardDef"/> DSL.
///
/// Resolve effect (<see cref="BuildDefinition"/>): on resolution, iterate
/// every creature on every opponent's battlefield and register a
/// <see cref="PumpUntilEndOfTurnEffect"/>(-1, -1) on each creature's
/// <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at end of
/// turn). The caster's own creatures are explicitly excluded
/// (CR 109.5 — "your opponents" scopes to every player except the
/// caster). Creatures whose <c>ActiveEffects</c> is null (shape-only
/// tests without a live <see cref="ContinuousEffectsService"/>) silently
/// skip registration so shape tests don't need a full effects stack.
///
/// The <see cref="ChosenSpellParams.AllPlayers"/> field supplied by
/// <see cref="Majik.Core.Game.SpellCastFlow"/> carries the full player list
/// at resolution time; callers that invoke <see cref="BuildDefinition"/>
/// directly (tests, bots) supply it via the same constructor parameter.
///
/// ## CR notes
/// - CR 109.5 — "your opponents" = every player other than the caster.
/// - CR 514.2 — Layer 7c continuous effects expire at end of turn.
/// - CR 613 — Layer 7c: characteristic-modifying effects (P/T adjustment).
/// - CR 608.2b — no targets → no legality check needed; the effect
///   applies to every matching creature that is on the battlefield when
///   the spell resolves.
/// </summary>
[CardName("Cower in Fear")]
public static class CowerInFearFactory
{
    public const string CardName = "Cower in Fear";
    public const string PrintedManaCost = "{1}{B}{B}";

    /// <summary>CardDef DSL — card shape only. The mass -1/-1 pump body
    /// lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "creatures opponents control get -1/-1 until end of turn"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: for each player in <paramref name="allPlayers"/> that is
    /// not the <paramref name="caster"/>, iterate every
    /// <see cref="Creature"/> on their <c>Battlefield</c> zone and register
    /// a <see cref="PumpUntilEndOfTurnEffect"/>(-1, -1) (CR 514.2).
    /// Creatures whose <see cref="Creature.ActiveEffects"/> is null are
    /// silently skipped — same pattern as <see cref="DisfigureFactory"/>.
    /// </summary>
    /// <param name="caster">The spell's controller. Used to exclude the
    /// caster's own creatures from the sweep (CR 109.5 — "your
    /// opponents").</param>
    /// <param name="allPlayers">All players in the game. Passed at cast
    /// time via <see cref="ChosenSpellParams.AllPlayers"/>; callers that
    /// skip the full cast flow supply it here directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                // Prefer the live AllPlayers snapshot from ChosenSpellParams
                // (populated by SpellCastFlow); fall back to the closed-over
                // list when the caller didn't plumb AllPlayers through
                // (e.g. bot probes that build SpellDefinition directly).
                var players = p.AllPlayers ?? allPlayers;
                return new IEffect[]
                {
                    new Effect(
                        "Cower in Fear — opponents' creatures get -1/-1 until end of turn",
                        () => Resolve(caster, players)),
                };
            });
    }

    private static void Resolve(Player caster, IReadOnlyList<Player> allPlayers)
    {
        // CR 109.5 — "your opponents" = every player that is not the caster.
        // Snapshot each player's creature list before applying effects so
        // any same-step zone-move side effects don't disturb enumeration
        // (matching Pyroclasm / AngerOfTheGods snapshot pattern).
        foreach (var player in allPlayers)
        {
            if (ReferenceEquals(player, caster)) continue;

            foreach (var creature in player.Zones.Battlefield
                         .GetCards()
                         .OfType<Creature>()
                         .ToList())
            {
                // CR 608.2b analogue — only apply to creatures still on
                // the battlefield (the zone check after snapshot accounts
                // for any creature moved during the same effect chain).
                if (creature.Zone != ZoneType.Battlefield) continue;

                // Shape-only guard: when ActiveEffects is null (test fixtures
                // without a live ContinuousEffectsService), skip silently
                // rather than throwing. Same pattern as DisfigureFactory.
                if (creature.ActiveEffects == null) continue;

                // CR 514.2 + CR 613 Layer 7c — register a -1/-1 EOT-scoped
                // continuous effect on the target creature.
                creature.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(creature, -1, -1));
            }
        }
    }
}
