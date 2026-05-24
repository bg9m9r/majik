using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mizzium Mortars (Return to Ravnica, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Mizzium Mortars deals 4 damage to target creature.
///    Overload {4}{R}{R} (You may cast this spell for its overload cost.
///    If you do, change its text by replacing all instances of 'target'
///    with 'each'.)"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Mizzium Mortars deals 4 damage to each creature you don't control."
///
/// ## Implementation (v1 — overload structural toggle)
///
/// CR 702.96 — Overload is an alternative cost. The
/// <see cref="OverloadAlternativeCost"/> primitive in <c>Majik.Core/Costs/</c>
/// exists as a Stub (per <c>MODERN_COVERAGE.md</c>): it gates the cast
/// from-hand and carries an <c>IsOverloaded</c> flag, but the cost is not
/// yet plumbed through <c>SpellCastFlow</c> to the resolving stack
/// object's effect factory. Wiring overload properly requires:
///   - The alt-cost selection at cast time honoured by
///     <see cref="Majik.Core.Services.SpellCastFlow"/>'s payment loop
///     (analogous to how Buyback adds an <see cref="IAdditionalCost"/>).
///   - A "was overloaded?" state bit plumbed from cast-time decision
///     through to the resolving stack object so the EffectFactory can
///     pick the each-creature branch (CR 702.96b).
///   - <see cref="OracleSpellBinder"/> awareness so data-driven cards
///     (Vandalblast, Skewer the Critics(no), etc.) discover the overload
///     alt-cost from oracle text.
///
/// Until that infra lands, Mizzium Mortars ships with default-not-overloaded
/// behavior: cast resolves for 4 damage to one target creature. The
/// overloaded branch is structural — callers can opt in by passing
/// <c>wasOverloaded: true</c> to <see cref="BuildSpellDefinition"/>,
/// which yields the each-creature-you-don't-control sweep. Bots driving
/// the cost-payment side won't choose to overload today (no alt-cost
/// probe), so the practical effect at the table is "{1}{R} Sorcery: 4
/// damage to target creature".
///
/// Card-shape only here; the resolve-time spell definition (target +
/// damage effect with the overloaded branch) is built on-demand via
/// <see cref="BuildSpellDefinition(Player, IReadOnlyList{Player}, Func{object, object}, bool)"/>
/// because <see cref="SpellDefinition"/> needs a target resolver supplied
/// by the caller's <see cref="GameContext"/> and the sweep needs the
/// player list.
///
/// ## CR notes
/// - CR 702.96 / 702.96b — Overload alt-cost; "target" → "each" rewrite.
/// - CR 119.2 — non-combat damage; <see cref="Creature.TakeDamage"/>
///   records it; SBA (CR 704.5f) moves lethal-damaged creatures to
///   graveyards on the next pass.
/// - CR 109.5 — "each creature you don't control" enumerates every
///   creature on the battlefield whose controller is not the spell's
///   controller (i.e., every other player's creatures).
/// </summary>
[CardName("Mizzium Mortars")]
public static class MizziumMortarsFactory
{
    public const string CardName = "Mizzium Mortars";
    public const string PrintedManaCost = "{1}{R}";
    public const string OverloadCostText = "{4}{R}{R}";
    public const int Damage = 4;

    /// <summary>
    /// Build a Mizzium Mortars sorcery owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time damage effect.
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
    /// Build the <see cref="SpellDefinition"/> used when Mizzium Mortars
    /// is cast.
    ///
    /// Default (not overloaded): single 1..1 "target creature" request;
    /// on resolution deals <see cref="Damage"/> (4) to the chosen target
    /// creature.
    ///
    /// Overloaded (<paramref name="wasOverloaded"/> = true): no target
    /// request; on resolution sweeps <see cref="Damage"/> (4) damage
    /// across every creature on every other player's battlefield (i.e.,
    /// every creature the <paramref name="controller"/> does NOT
    /// control) — CR 702.96b "target" → "each" rewrite over the printed
    /// "each creature you don't control".
    ///
    /// CR 702.96b — the overload decision is locked in when the spell is
    /// cast (CR 601.2b). Until overload is a real primitive wired
    /// through <see cref="Majik.Core.Services.SpellCastFlow"/>, the flag
    /// is supplied by the caller (kicker has since shipped as a real
    /// primitive — see <see cref="Costs.KickerAdditionalCost"/>; overload
    /// remains structural-flag-only).
    /// </summary>
    /// <param name="controller">Spell controller — used to scope the
    /// overloaded sweep to creatures the controller does NOT control
    /// (CR 702.96b "each creature you don't control").</param>
    /// <param name="allPlayers">All players whose battlefields the
    /// overloaded sweep should reach. Typically every player in the
    /// game; the resolve effect filters out
    /// <paramref name="controller"/>'s own creatures.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).
    /// Used only for the default (not-overloaded) branch.</param>
    /// <param name="wasOverloaded">Whether the overload alt-cost was
    /// paid at cast time. Defaults to <c>false</c> — overload is not
    /// yet wired through <see cref="Majik.Core.Services.SpellCastFlow"/>,
    /// so production casts ship as not-overloaded.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        IReadOnlyList<Player> allPlayers,
        Func<object, object> resolver,
        bool wasOverloaded = false)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(resolver);

        if (wasOverloaded)
        {
            // CR 702.96b — overloaded branch. No "target" anymore (rewritten
            // to "each"); deal Damage to each creature the controller does
            // NOT control. Snapshot the per-player creature list before
            // applying so any same-step zone-move side effects don't
            // disturb enumeration; SBAs run on the next priority pass and
            // move lethal-damaged creatures to graveyards (CR 704.5f).
            return new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect(
                        $"Mizzium Mortars (overloaded): deal {Damage} damage to each creature you don't control.",
                        () =>
                        {
                            var seen = new HashSet<Creature>();
                            foreach (var pl in allPlayers)
                            {
                                if (ReferenceEquals(pl, controller)) continue;
                                foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                                {
                                    if (seen.Add(c)) c.TakeDamage(Damage);
                                }
                            }
                        }),
                });
        }

        // Default printed cast — single 1..1 "target creature" request;
        // resolve = 4 damage to that creature.
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"Mizzium Mortars: {Damage} damage to target creature.", () =>
                        Fx.DealDamage(target, Damage)),
                };
            });
    }
}
