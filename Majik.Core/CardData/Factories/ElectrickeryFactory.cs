using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Electrickery (Return to Ravnica, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Electrickery deals 1 damage to target creature you don't control.
///    Overload {1}{R} (You may cast this spell for its overload cost. If you
///    do, change \"target\" in its text to \"each.\")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Electrickery deals 1 damage to each creature you don't control."
///
/// ## Implementation (v1 — overload structural toggle)
///
/// Same shape as <see cref="MizziumMortarsFactory"/>, reduced to an Instant
/// dealing 1 damage with overload {1}{R}, and with the *default* (printed)
/// cast already scoped to "target creature you don't control".
///
/// CR 702.96 — Overload is an alternative cost. As documented on
/// <see cref="MizziumMortarsFactory"/>, the OverloadAlternativeCost primitive
/// is a stub: it gates the cast and carries an IsOverloaded flag but is not
/// yet plumbed through <see cref="Majik.Core.Services.SpellCastFlow"/> to the
/// resolving stack object's effect factory. Until that infra lands,
/// Electrickery ships default-not-overloaded: cast resolves for 1 damage to
/// one targeted creature the controller does not control. The overloaded
/// branch is structural — callers opt in via <c>wasOverloaded: true</c> on
/// <see cref="BuildSpellDefinition"/>, yielding the each-creature-you-don't-
/// control sweep (CR 702.96b).
///
/// Card shape comes from the embedded JSON (<c>electrickery.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time spell definition is
/// built on-demand via <see cref="BuildSpellDefinition"/> because a
/// <see cref="SpellDefinition"/> needs a target resolver supplied by the
/// caller's <see cref="GameContext"/> and the sweep needs the player list.
///
/// ## CR notes
/// - CR 702.96 / 702.96b — Overload alt-cost; "target" → "each" rewrite.
/// - CR 119.2 — non-combat damage; <see cref="Creature.TakeDamage"/> records
///   it; SBA (CR 704.5f) moves lethal-damaged creatures to graveyards on the
///   next pass.
/// - CR 109.5 — "each creature you don't control" enumerates every creature
///   on the battlefield whose controller is not the spell's controller.
/// </summary>
[CardName("Electrickery")]
public static class ElectrickeryFactory
{
    public const string CardName = "Electrickery";
    public const string Slug = "electrickery";
    public const string PrintedManaCost = "{R}";
    public const string OverloadCostText = "{1}{R}";

    /// <summary>CR 119 — fixed 1 damage.</summary>
    public const int Damage = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Electrickery is cast.
    ///
    /// Default (not overloaded): single 1..1 "target creature you don't
    /// control" request; on resolution deals <see cref="Damage"/> (1) to the
    /// chosen creature.
    ///
    /// Overloaded (<paramref name="wasOverloaded"/> = true): no target
    /// request; on resolution sweeps <see cref="Damage"/> (1) across every
    /// creature on every other player's battlefield (every creature the
    /// <paramref name="controller"/> does NOT control) — CR 702.96b "target"
    /// → "each" rewrite over the printed "each creature you don't control".
    /// </summary>
    /// <param name="controller">Spell controller — the "you" reference
    /// (CR 109.5). The sweep / target pool excludes creatures it controls.</param>
    /// <param name="allPlayers">All players whose battlefields the overloaded
    /// sweep should reach. The resolve effect filters out
    /// <paramref name="controller"/>'s own creatures.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/>. Used only for the default branch.</param>
    /// <param name="wasOverloaded">Whether the overload alt-cost was paid at
    /// cast time. Defaults to <c>false</c> — overload is not yet wired through
    /// <see cref="Majik.Core.Services.SpellCastFlow"/>, so production casts
    /// ship not-overloaded.</param>
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
            // to "each"); deal Damage to each creature the controller does NOT
            // control. Snapshot the per-player creature list before applying
            // so any same-step zone-move side effects don't disturb
            // enumeration; SBAs run on the next priority pass and move
            // lethal-damaged creatures to graveyards (CR 704.5f).
            return new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect(
                        $"Electrickery (overloaded): deal {Damage} damage to each creature you don't control.",
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

        // Default printed cast — single 1..1 "target creature you don't
        // control" request; resolve = 1 damage to that creature.
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature you don't control", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"Electrickery: {Damage} damage to target creature you don't control.", () =>
                        Fx.DealDamage(target, Damage)),
                };
            });
    }
}
