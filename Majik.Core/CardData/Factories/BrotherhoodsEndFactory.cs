using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brotherhood's End (The Brothers' War, {1}{R}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall + the embedded seed):
///   "Choose one —
///     • Brotherhood's End deals 3 damage to each creature and each planeswalker.
///     • Destroy all artifacts with mana value 3 or less."
///
/// ## Why a named factory
/// Brotherhood's End is a modal "Choose one —" sweeper that combines two
/// shapes the engine already ships:
///   - Mode 0 — a Pyroclasm-style untargeted sweep
///     (<see cref="PyroclasmFactory"/> / <see cref="FieryCannonadeFactory"/>),
///     widened to also hit each planeswalker (loyalty removal —
///     CR 119.3 / 306.7). Both halves route through
///     <see cref="Fx.DealDamageAny(object, int)"/>, which deals 3 damage to a
///     creature and removes 3 loyalty from a planeswalker.
///   - Mode 1 — a mass artifact destruction filtered by mana value
///     (<see cref="VandalblastFactory"/>'s overloaded sweep), restricted to
///     artifacts with mana value 3 or less (CR 202.3 / 765 — mana value is the
///     total of the mana cost; this is mass removal, not targeted, so no
///     "you don't control" clause).
///
/// The modal shell is the same shape as <see cref="KolaghansCommandFactory"/>,
/// only PickCount = 1 ("Choose one —", CR 700.2d) and neither mode takes a
/// target — both are untargeted board sweeps.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{R}{R}, Red. Card shape comes from the
///   embedded JSON (<c>brotherhoods-end.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Mode 0</b> — 3 damage to each creature AND each planeswalker across
///   every supplied player's battlefield (CR 109.5 / 700 — "each" without a
///   controller restriction reaches every such permanent). Creatures take
///   damage (SBA CR 704.5g moves lethal-damaged creatures to graveyards on the
///   next SBA pass); planeswalkers lose 3 loyalty (CR 306.7; SBA CR 704.5i
///   moves 0-loyalty planeswalkers to graveyards).
/// - <b>Mode 1</b> — destroy each artifact (CR 301) with mana value 3 or less
///   (CR 202.3) across every supplied player's battlefield. Destroy via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); indestructible
///   (CR 702.12) / regeneration (CR 701.15) honoured by the Destroy-reason
///   gate.
///
/// ## CR notes
/// - CR 700.2d — "Choose one —" modal spell; exactly one mode is chosen.
/// - CR 109.5 / 700 — "each creature" / "each planeswalker" enumerate every
///   such permanent on the battlefield regardless of controller.
/// - CR 202.3 — mana value = total mana cost (here, <= 3).
/// - CR 701.7 — Destroy; CR 119.3 / 306.7 — damage to a planeswalker removes
///   that much loyalty.
///
/// v1 defaults to mode 0 (the sweep) when no explicit mode selector is
/// provided — matches the other modal factories' default-first-mode posture.
/// </summary>
[CardName("Brotherhood's End")]
public static class BrotherhoodsEndFactory
{
    public const string CardName = "Brotherhood's End";
    public const string Slug = "brotherhoods-end";
    public const string PrintedManaCost = "{1}{R}{R}";

    /// <summary>Mode 0 — 3 damage to each creature and each planeswalker.</summary>
    public const int ModeDamageSweep = 0;

    /// <summary>Mode 1 — destroy all artifacts with mana value 3 or less.</summary>
    public const int ModeDestroyArtifacts = 1;

    /// <summary>CR 700.2d — "Choose one —" picks exactly one mode.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>CR 700 — damage / loyalty removal amount for mode 0.</summary>
    public const int Damage = 3;

    /// <summary>CR 202.3 — mode 1 destroys artifacts of mana value at most this.</summary>
    public const int MaxArtifactManaValue = 3;

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Brotherhood's End deals 3 damage to each creature and each planeswalker.",
        "Destroy all artifacts with mana value 3 or less.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Brotherhood's End.
    ///
    /// Two modes, pick one (CR 700.2d). Neither mode takes a target — both are
    /// untargeted board sweeps — so <see cref="SpellDefinition.TargetRequests"/>
    /// is empty. The chosen mode is read from
    /// <see cref="ChosenSpellParams.ModeIndexes"/> (falling back to the legacy
    /// scalar <see cref="ChosenSpellParams.ModeIndex"/>, then to
    /// <paramref name="defaultMode"/>).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep should
    /// reach (CR 109.5 — "each"). Defaults to an empty list when null, which
    /// makes the resolve a no-op; production cast paths plumb every player
    /// through <see cref="ChosenSpellParams.AllPlayers"/>.</param>
    /// <param name="defaultMode">Mode chosen when no explicit selector is
    /// supplied. Defaults to <see cref="ModeDamageSweep"/>.</param>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<Player>? allPlayers = null,
        int defaultMode = ModeDamageSweep)
    {
        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Removal, // mode 0 — board wipe
                BotIntent.Removal, // mode 1 — artifact wipe
            },
            EffectFactory: p =>
            {
                // CR 700.2d — exactly one mode. Prefer ModeIndexes; fall back to
                // the legacy scalar ModeIndex; finally to defaultMode.
                int mode;
                if (p.ModeIndexes is { Count: > 0 } list)
                {
                    mode = list[0];
                }
                else if (p.ModeIndex.HasValue)
                {
                    mode = p.ModeIndex.Value;
                }
                else
                {
                    mode = defaultMode;
                }

                // CR 109.5 — "each" reaches every battlefield. Prefer the
                // params' AllPlayers (plumbed by the production cast flow);
                // fall back to the builder-supplied list.
                var players = (p.AllPlayers is { Count: > 0 } pa)
                    ? pa
                    : (allPlayers ?? Array.Empty<Player>());

                return mode switch
                {
                    ModeDestroyArtifacts => BuildDestroyArtifactsEffect(players),
                    _ => BuildDamageSweepEffect(players),
                };
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0 — 3 damage to each creature and each planeswalker
    // -----------------------------------------------------------------------

    /// <summary>
    /// 3 damage to every creature and 3 loyalty removed from every planeswalker
    /// across <paramref name="allPlayers"/>' battlefields (CR 109.5 / 700).
    /// Both halves route through <see cref="Fx.DealDamageAny(object, int)"/>,
    /// which deals creature damage and removes planeswalker loyalty
    /// (CR 119.3 / 306.7). Snapshot to a list before applying so same-step zone
    /// moves don't disturb the enumeration; SBAs (CR 704.5g / 704.5i) sweep the
    /// dead permanents on the next priority pass.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildDamageSweepEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: deal {Damage} damage to each creature and each planeswalker.",
                () =>
                {
                    var seen = new HashSet<Permanent>();
                    foreach (var pl in allPlayers)
                    {
                        foreach (var perm in pl.Zones.Battlefield.GetCards()
                                     .OfType<Permanent>()
                                     .Where(c => c is Creature || c is Planeswalker)
                                     .ToList())
                        {
                            if (!seen.Add(perm)) continue;
                            // CR 119.3 / 306.7 — DealDamageAny removes loyalty
                            // from a planeswalker and deals damage to a creature.
                            Fx.DealDamageAny(perm, Damage);
                        }
                    }
                }),
        };
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy all artifacts with mana value 3 or less
    // -----------------------------------------------------------------------

    /// <summary>
    /// Destroy every artifact (CR 301) with mana value 3 or less (CR 202.3)
    /// across <paramref name="allPlayers"/>' battlefields. Untargeted mass
    /// destruction — no controller restriction. Destroy via
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); indestructible
    /// (CR 702.12) / regeneration (CR 701.15) honoured by the Destroy gate.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildDestroyArtifactsEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all artifacts with mana value {MaxArtifactManaValue} or less.",
                () =>
                {
                    var seen = new HashSet<Permanent>();
                    foreach (var pl in allPlayers)
                    {
                        foreach (var perm in pl.Zones.Battlefield.GetCards()
                                     .OfType<Permanent>()
                                     .Where(c => c.HasType(CardType.Artifact))
                                     .ToList())
                        {
                            // CR 202.3 — mana value is the total mana cost.
                            if (perm.ManaCostValue.TotalValue > MaxArtifactManaValue) continue;
                            if (!seen.Add(perm)) continue;
                            // CR 701.7 — Destroy.
                            OracleSpellBinder.MoveToGraveyard(perm, ZoneMoveReason.Destroy);
                        }
                    }
                }),
        };
    }
}
