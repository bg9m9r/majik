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
/// Named-card factory for Cleansing Nova (Core Set 2019, {3}{W}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy all creatures.
///     • Destroy all artifacts and enchantments."
///
/// ## Why a named factory
/// Cleansing Nova is a modal "Choose one —" sweeper (CR 700.2d) that combines
/// two unfiltered mass-destruction shapes the engine already ships — the same
/// modal shell as <see cref="BrotherhoodsEndFactory"/>, only both modes are
/// plain "destroy all [type]" sweeps with no mana-value filter:
///   - Mode 0 — destroy each creature on every battlefield (a Wrath-of-God /
///     Damnation-style board wipe — <see cref="WrathOfGodFactory"/>).
///   - Mode 1 — destroy each artifact (CR 301) AND each enchantment (CR 303)
///     on every battlefield (a Bane-of-Progress-style artifact+enchantment
///     sweep).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {3}{W}{W}, White (color derived from cost). Card
///   shape comes from the embedded JSON (<c>cleansing-nova.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Mode 0</b> — destroy each creature (CR 109.5 / 700 — "all" reaches
///   every such permanent regardless of controller) across every supplied
///   player's battlefield, via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).
/// - <b>Mode 1</b> — destroy each artifact (CR 301) and each enchantment
///   (CR 303) across every supplied player's battlefield, same Destroy route.
///
/// ## CR notes
/// - CR 700.2d — "Choose one —" modal spell; exactly one mode is chosen.
/// - CR 109.5 / 700 — "all creatures" / "all artifacts and enchantments"
///   enumerate every such permanent on the battlefield regardless of
///   controller (this is untargeted mass removal — no "you control" clause).
/// - CR 701.7 — Destroy; indestructible (CR 702.12) / regeneration (CR 701.15)
///   honoured by the Destroy-reason gate inside MoveToGraveyard (the printed
///   text carries no "can't be regenerated" rider, so shields are honoured).
///
/// v1 defaults to mode 0 (destroy all creatures) when no explicit mode
/// selector is provided — matches the other modal factories' default-first-mode
/// posture (<see cref="BrotherhoodsEndFactory"/>).
/// </summary>
[CardName("Cleansing Nova")]
public static class CleansingNovaFactory
{
    public const string CardName = "Cleansing Nova";
    public const string Slug = "cleansing-nova";
    public const string PrintedManaCost = "{3}{W}{W}";

    /// <summary>Mode 0 — destroy all creatures.</summary>
    public const int ModeDestroyCreatures = 0;

    /// <summary>Mode 1 — destroy all artifacts and enchantments.</summary>
    public const int ModeDestroyArtifactsAndEnchantments = 1;

    /// <summary>CR 700.2d — "Choose one —" picks exactly one mode.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy all creatures.",
        "Destroy all artifacts and enchantments.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Cleansing Nova.
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
    /// supplied. Defaults to <see cref="ModeDestroyCreatures"/>.</param>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<Player>? allPlayers = null,
        int defaultMode = ModeDestroyCreatures)
    {
        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Removal, // mode 0 — creature board wipe
                BotIntent.Removal, // mode 1 — artifact + enchantment wipe
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
                    ModeDestroyArtifactsAndEnchantments => BuildDestroyArtifactsAndEnchantmentsEffect(players),
                    _ => BuildDestroyCreaturesEffect(players),
                };
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy all creatures
    // -----------------------------------------------------------------------

    /// <summary>
    /// Destroy every creature (CR 109.5 / 700) across
    /// <paramref name="allPlayers"/>' battlefields. Untargeted mass
    /// destruction — no controller restriction. Snapshot the per-player
    /// creature list before applying so same-step zone moves don't disturb the
    /// enumeration. Destroy via
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); indestructible
    /// (CR 702.12) / regeneration (CR 701.15) honoured by the Destroy gate.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildDestroyCreaturesEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all creatures.",
                () =>
                {
                    var seen = new HashSet<Creature>();
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards()
                                     .OfType<Creature>()
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

    // -----------------------------------------------------------------------
    // Mode 1 — destroy all artifacts and enchantments
    // -----------------------------------------------------------------------

    /// <summary>
    /// Destroy every artifact (CR 301) and every enchantment (CR 303) across
    /// <paramref name="allPlayers"/>' battlefields. Untargeted mass
    /// destruction — no controller restriction. A permanent that is BOTH an
    /// artifact and an enchantment (an "artifact enchantment") is destroyed
    /// once (HashSet-deduped). Destroy via
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); indestructible
    /// (CR 702.12) / regeneration (CR 701.15) honoured by the Destroy gate.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildDestroyArtifactsAndEnchantmentsEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all artifacts and enchantments.",
                () =>
                {
                    var seen = new HashSet<Permanent>();
                    foreach (var pl in allPlayers)
                    {
                        foreach (var perm in pl.Zones.Battlefield.GetCards()
                                     .OfType<Permanent>()
                                     .Where(c => c.HasType(CardType.Artifact)
                                              || c.HasType(CardType.Enchantment))
                                     .ToList())
                        {
                            if (!seen.Add(perm)) continue;
                            // CR 701.7 — Destroy.
                            OracleSpellBinder.MoveToGraveyard(perm, ZoneMoveReason.Destroy);
                        }
                    }
                }),
        };
    }
}
