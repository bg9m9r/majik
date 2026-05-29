using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sheoldred's Edict (Phyrexia: All Will Be One,
/// {1}{B}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Choose one —
///     • Each opponent sacrifices a nontoken creature of their choice.
///     • Each opponent sacrifices a creature token of their choice.
///     • Each opponent sacrifices a planeswalker of their choice."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}.
/// - CR 700.2d — modal "Choose one —"; one mode picked at cast time. No
///   targets (the card affects "each opponent", not a chosen target), so the
///   <see cref="SpellDefinition.TargetRequests"/> list is empty and the
///   chosen mode is read from <see cref="ChosenSpellParams.ModeIndex"/> /
///   <see cref="ChosenSpellParams.ModeIndexes"/>.
/// - Each mode iterates every opponent of the caster (via
///   <see cref="ChosenSpellParams.AllPlayers"/>, mirroring
///   <c>EachOpponentSacrificesCreatureTemplate</c>) and makes that opponent
///   sacrifice one permanent matching the mode's filter "of their choice":
///     - Mode 0 (<see cref="ModeNontokenCreature"/>): a creature that is not
///       a token.
///     - Mode 1 (<see cref="ModeCreatureToken"/>): a creature that is a token.
///     - Mode 2 (<see cref="ModePlaneswalker"/>): a planeswalker.
/// - "Of their choice": the affected player's agent drives the pick (via
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>, intent
///   <see cref="BotIntent.Removal"/>), exactly as
///   <see cref="DiabolicEdictFactory"/>. Deterministic fallback (no agent, or
///   an illegal pick) = first matching permanent in battlefield order.
/// - CR 701.16 — "sacrifice" moves the permanent from the battlefield to its
///   owner's graveyard, bypassing Indestructible / regeneration. An opponent
///   with no permanent matching the filter sacrifices nothing (no-op); the
///   spell still resolves.
///
/// ## Why a named factory
/// The modal choose-one shape is shared with <see cref="IzzetCharmFactory"/>
/// (per-mode body switched on ModeIndex). The per-mode edict body mirrors
/// <see cref="DiabolicEdictFactory"/> (agent-driven "of their choice" pick)
/// extended to the "each opponent" iteration of
/// <c>EachOpponentSacrificesCreatureTemplate</c>. No new engine mechanic is
/// introduced — token detection (<see cref="Permanent.IsToken"/>),
/// planeswalker typing (<see cref="CardType.Planeswalker"/>), and
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
/// <see cref="ZoneMoveReason.Sacrifice"/> all pre-exist.
///
/// ## Deferred (v1 gaps)
/// - <b>Forced sacrifice prompt UI</b>: the affected player's agent receives
///   the full filtered list; surfacing the choice to the portal decision
///   panel is deferred (same queue as Diabolic Edict).
/// </summary>
[CardName("Sheoldred's Edict")]
public static class SheoldredsEdictFactory
{
    public const string CardName = "Sheoldred's Edict";
    public const string PrintedManaCost = "{1}{B}";

    public const int ModeNontokenCreature = 0;
    public const int ModeCreatureToken    = 1;
    public const int ModePlaneswalker     = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Each opponent sacrifices a nontoken creature of their choice.",
        "Each opponent sacrifices a creature token of their choice.",
        "Each opponent sacrifices a planeswalker of their choice.",
    };

    /// <summary>
    /// Build Sheoldred's Edict as an Instant owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time modal
    /// body is built on demand via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Sheoldred's Edict is
    /// cast. No target requests (the card is "each opponent" / "of their
    /// choice"); the chosen mode comes from
    /// <see cref="ChosenSpellParams.ModeIndex"/>.
    /// </summary>
    /// <param name="caster">The spell's controller — excluded from the
    /// "each opponent" iteration.</param>
    /// <param name="allPlayers">All players in turn order (used to find the
    /// caster's opponents at resolution). Falls back to
    /// <see cref="ChosenSpellParams.AllPlayers"/> when the runtime supplies a
    /// fresher list at resolution time.</param>
    /// <param name="agent">Optional agent used to drive each affected
    /// player's "of their choice" pick. When null, the pick falls back
    /// deterministically to the first matching permanent in battlefield
    /// order (matches <see cref="DiabolicEdictFactory"/>).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Removal,
                BotIntent.Removal,
                BotIntent.Removal,
            },
            EffectFactory: p =>
            {
                // CR 700.2d — single chosen mode. Honor the multi-pick list
                // (first entry wins for a Choose-one card) or the legacy
                // scalar ModeIndex (mirrors IzzetCharmFactory).
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;      // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break; // CR 700.2d — pick count cap

                    effectsOut.Add(BuildModeEffect(raw, caster, allPlayers, agent, p));
                }
                return effectsOut;
            });
    }

    private static IEffect BuildModeEffect(
        int mode,
        Player caster,
        IReadOnlyList<Player> allPlayers,
        IPlayerAgent? agent,
        ChosenSpellParams p)
    {
        var (label, filter) = mode switch
        {
            ModeNontokenCreature =>
                ("nontoken creature",
                 (Func<ICard, bool>)(c => c.HasType(CardType.Creature)
                                          && c is Permanent { IsToken: false })),
            ModeCreatureToken =>
                ("creature token",
                 (Func<ICard, bool>)(c => c.HasType(CardType.Creature)
                                          && c is Permanent { IsToken: true })),
            ModePlaneswalker =>
                ("planeswalker",
                 (Func<ICard, bool>)(c => c.HasType(CardType.Planeswalker))),
            _ => ("(unknown)", (Func<ICard, bool>)(_ => false)),
        };

        return new Effect($"{CardName}: each opponent sacrifices a {label} of their choice", () =>
        {
            // Prefer the resolution-time player list when the runtime supplies
            // it; otherwise use the list captured at cast time.
            var players = p.AllPlayers is { Count: > 0 } fresh ? fresh : allPlayers;
            if (players == null) return;

            foreach (var pl in players)
            {
                // "Each opponent" — exclude the caster (CR 102.1).
                if (ReferenceEquals(pl, caster)) continue;

                // Pre-filter this player's battlefield to legal picks for the
                // chosen mode.
                var candidates = pl.Zones.Battlefield.GetCards()
                    .Where(filter)
                    .ToList();

                // No matching permanent → this player sacrifices nothing
                // (CR 701.16 can't be executed with no eligible permanent).
                if (candidates.Count == 0) continue;

                // "Of their choice" — the affected player's agent drives the
                // pick (BotIntent.Removal) with a deterministic fallback to
                // the first matching permanent in battlefield order.
                ICard pick;
                if (agent != null)
                {
                    var chosen = agent
                        .ChooseFromBattlefieldAsync(pl, candidates, BotIntent.Removal)
                        .GetAwaiter().GetResult();

                    // Validate the agent pick: must still be on this player's
                    // battlefield and match the mode filter. Invalid →
                    // deterministic fallback (mirrors DiabolicEdictFactory).
                    pick = (chosen != null
                            && chosen.Zone == ZoneType.Battlefield
                            && ReferenceEquals(chosen.Controller, pl)
                            && filter(chosen))
                        ? chosen
                        : candidates[0];
                }
                else
                {
                    pick = candidates[0];
                }

                // CR 701.16 — sacrifice: move permanent from battlefield to
                // its owner's graveyard. Bypasses Indestructible / regen.
                OracleSpellBinder.MoveToGraveyard(pick, ZoneMoveReason.Sacrifice);
            }
        });
    }
}
