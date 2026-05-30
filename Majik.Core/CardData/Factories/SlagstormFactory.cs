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
/// Named-card factory for Slagstorm (New Phyrexia, {1}{R}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Slagstorm deals 3 damage to each creature.
///     • Slagstorm deals 3 damage to each player."
///
/// CR 700.2d — modal "Choose one —" spell. Neither mode targets, so the
/// <see cref="SpellDefinition"/> carries no <see cref="TargetRequest"/>s; the
/// chosen mode is plumbed through <see cref="ChosenSpellParams.ModeIndex"/>
/// (and the multi-pick <see cref="ChosenSpellParams.ModeIndexes"/> list, of
/// which the first entry wins for a Choose-one card). Pattern mirrors
/// <see cref="ThrabenCharmFactory"/> for the modal dispatch shape and
/// <see cref="PyroclasmFactory"/> / <see cref="AngerOfTheGodsFactory"/> for
/// the each-creature sweep.
///
/// Card shape comes from the embedded JSON (<c>slagstorm.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/> because the sweep needs the live player
/// list, not expressible in the data-only JSON schema.
///
/// Mode 0 — "Slagstorm deals 3 damage to each creature":
///   Iterates every creature on every supplied player's battlefield and deals
///   3 damage via <see cref="Creature.TakeDamage"/> (CR 109.5 — "each
///   creature" reaches every creature regardless of controller; CR 119.2 —
///   non-combat damage). SBAs (CR 704.5f) move lethal-damaged creatures to
///   graveyards on the next priority pass.
///
/// Mode 1 — "Slagstorm deals 3 damage to each player":
///   Deals 3 damage to every supplied player via
///   <see cref="Fx.DealDamage(object, int)"/>, which routes a
///   <see cref="Player"/> through <see cref="Player.LoseLife"/> (CR 119.3 —
///   damage to a player causes that much life loss; CR 109.5 — "each player"
///   reaches every player including the caster).
/// </summary>
[CardName("Slagstorm")]
public static class SlagstormFactory
{
    public const string CardName = "Slagstorm";
    public const string Slug = "slagstorm";
    public const string PrintedManaCost = "{1}{R}{R}";

    /// <summary>CR 119 — fixed 3 damage on either mode.</summary>
    public const int Damage = 3;

    public const int ModeEachCreature = 0;
    public const int ModeEachPlayer   = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Slagstorm deals 3 damage to each creature.",
        "Slagstorm deals 3 damage to each player.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Slagstorm. Two modes, no X,
    /// no target requests — both modes resolve over the whole battlefield /
    /// all players.
    /// </summary>
    /// <param name="allPlayers">All players in the game. Mode 0 sweeps every
    /// player's battlefield; mode 1 damages every player.</param>
    public static SpellDefinition BuildDefinition(IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Wrath, // CR 700.2d mode 0 — sweep every creature.
                BotIntent.Burn,  // CR 700.2d mode 1 — burn every player.
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex
                // (CR 700.2d — exactly one mode is chosen).
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeEachCreature:
                            effectsOut.Add(BuildEachCreatureEffect(allPlayers));
                            break;
                        case ModeEachPlayer:
                            effectsOut.Add(BuildEachPlayerEffect(allPlayers));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: 3 damage to each creature.
    // -----------------------------------------------------------------------

    private static IEffect BuildEachCreatureEffect(IReadOnlyList<Player> allPlayers) =>
        new Effect($"{CardName}: deal {Damage} damage to each creature.", () =>
        {
            // CR 109.5 / CR 700 — "each creature" reaches every creature on
            // every battlefield. Snapshot to a list before applying so any
            // same-step zone-move side effects don't disturb enumeration;
            // SBAs (CR 704.5f) move lethal-damaged creatures to graveyards
            // on the next priority pass.
            var seen = new HashSet<Creature>();
            foreach (var pl in allPlayers)
            {
                foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                {
                    if (seen.Add(c)) c.TakeDamage(Damage);
                }
            }
        });

    // -----------------------------------------------------------------------
    // Mode 1: 3 damage to each player.
    // -----------------------------------------------------------------------

    private static IEffect BuildEachPlayerEffect(IReadOnlyList<Player> allPlayers) =>
        new Effect($"{CardName}: deal {Damage} damage to each player.", () =>
        {
            // CR 109.5 — "each player" reaches every player in the game,
            // including the caster. CR 119.3 — damage to a player causes
            // that much life loss; Fx.DealDamage routes a Player through
            // Player.LoseLife.
            foreach (var pl in allPlayers)
            {
                Fx.DealDamage(pl, Damage);
            }
        });
}
