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
/// Named-card factory for Split Up (Mirage, {1}{W}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy all tapped creatures.
///     • Destroy all untapped creatures."
///
/// ## Why a named factory
/// Split Up is a modal "Choose one —" sweeper (CR 700.2d) whose two modes are
/// plain creature board wipes (the <see cref="CleansingNovaFactory"/> /
/// <see cref="DayOfJudgmentFactory"/> shape) each narrowed by a tapped-state
/// predicate (CR 701.7 destroy + CR 701.x snapshot of
/// <see cref="Permanent.IsTapped"/>):
///   - Mode 0 — destroy each <b>tapped</b> creature on every battlefield.
///   - Mode 1 — destroy each <b>untapped</b> creature on every battlefield.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W}{W}, White (color derived from cost). Card
///   shape comes from the embedded JSON (<c>split-up.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Mode 0</b> — destroy each tapped creature (CR 109.5 / 700 — "all"
///   reaches every such permanent regardless of controller) across every
///   supplied player's battlefield, via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).
/// - <b>Mode 1</b> — destroy each untapped creature, same Destroy route.
///
/// ## CR notes
/// - CR 700.2d — "Choose one —" modal spell; exactly one mode is chosen.
/// - CR 109.5 / 700 — "all tapped/untapped creatures" enumerate every such
///   permanent on the battlefield regardless of controller (untargeted mass
///   removal — no "you control" clause).
/// - CR 701.7 — Destroy; indestructible (CR 702.12) / regeneration (CR 701.15)
///   honoured by the Destroy-reason gate inside MoveToGraveyard (the printed
///   text carries no "can't be regenerated" rider, so shields are honoured).
/// - The tapped/untapped partition is evaluated when the spell resolves
///   (CR 608.2 — read the game state as the effect applies). Both modes filter
///   the same battlefield snapshot, mirroring the
///   <see cref="CleansingNovaFactory"/> dedup discipline.
///
/// v1 defaults to mode 0 (destroy all tapped creatures) when no explicit mode
/// selector is provided — matches the other modal factories' default-first-mode
/// posture (<see cref="CleansingNovaFactory"/>).
/// </summary>
[CardName("Split Up")]
public static class SplitUpFactory
{
    public const string CardName = "Split Up";
    public const string Slug = "split-up";
    public const string PrintedManaCost = "{1}{W}{W}";

    /// <summary>Mode 0 — destroy all tapped creatures.</summary>
    public const int ModeDestroyTapped = 0;

    /// <summary>Mode 1 — destroy all untapped creatures.</summary>
    public const int ModeDestroyUntapped = 1;

    /// <summary>CR 700.2d — "Choose one —" picks exactly one mode.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy all tapped creatures.",
        "Destroy all untapped creatures.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Split Up.
    ///
    /// Two modes, pick one (CR 700.2d). Neither mode takes a target — both are
    /// untargeted board sweeps — so <see cref="SpellDefinition.TargetRequests"/>
    /// is empty. The chosen mode is read from
    /// <see cref="ChosenSpellParams.ModeIndexes"/> (falling back to the legacy
    /// scalar <see cref="ChosenSpellParams.ModeIndex"/>, then to
    /// <paramref name="defaultMode"/>).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep should
    /// reach (CR 109.5 — "all"). Defaults to an empty list when null, which
    /// makes the resolve a no-op; production cast paths plumb every player
    /// through <see cref="ChosenSpellParams.AllPlayers"/>.</param>
    /// <param name="defaultMode">Mode chosen when no explicit selector is
    /// supplied. Defaults to <see cref="ModeDestroyTapped"/>.</param>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<Player>? allPlayers = null,
        int defaultMode = ModeDestroyTapped)
    {
        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Removal, // mode 0 — tapped creature wipe
                BotIntent.Removal, // mode 1 — untapped creature wipe
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

                // CR 109.5 — "all" reaches every battlefield. Prefer the
                // params' AllPlayers (plumbed by the production cast flow);
                // fall back to the builder-supplied list.
                var players = (p.AllPlayers is { Count: > 0 } pa)
                    ? pa
                    : (allPlayers ?? Array.Empty<Player>());

                return mode switch
                {
                    ModeDestroyUntapped => BuildDestroyCreaturesEffect(players, tapped: false),
                    _ => BuildDestroyCreaturesEffect(players, tapped: true),
                };
            });
    }

    // -----------------------------------------------------------------------
    // Shared mode body — destroy all tapped / untapped creatures
    // -----------------------------------------------------------------------

    /// <summary>
    /// Destroy every creature whose <see cref="Permanent.IsTapped"/> equals
    /// <paramref name="tapped"/> (CR 109.5 / 700) across
    /// <paramref name="allPlayers"/>' battlefields. Untargeted mass
    /// destruction — no controller restriction. Snapshot the per-player
    /// creature list before applying so same-step zone moves don't disturb the
    /// enumeration. Destroy via
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); indestructible
    /// (CR 702.12) / regeneration (CR 701.15) honoured by the Destroy gate.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildDestroyCreaturesEffect(
        IReadOnlyList<Player> allPlayers,
        bool tapped)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        var label = tapped ? "tapped" : "untapped";
        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all {label} creatures.",
                () =>
                {
                    var seen = new HashSet<Creature>();
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards()
                                     .OfType<Creature>()
                                     // CR 608.2 — tapped state read as the
                                     // effect resolves.
                                     .Where(c => c.IsTapped == tapped)
                                     .ToList())
                        {
                            if (!seen.Add(c)) continue;
                            // CR 701.7 — Destroy.
                            OracleSpellBinder.MoveToGraveyard(c, ZoneMoveReason.Destroy);
                        }
                    }
                }),
        };
    }
}
