using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Diabolic Edict (Tempest / many reprints, {1}{B}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Target player sacrifices a creature of their choice."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target
///   player" request. On resolution the target player sacrifices a
///   creature of their choice (CR 701.16 — sacrifice bypasses
///   Indestructible / regeneration):
///     1. Pre-filter the target's battlefield to creatures they control.
///     2. Ask the target player's agent via
///        <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>
///        (intent = <see cref="BotIntent.Removal"/>) for the pick.
///     3. Deterministic fallback when no agent is supplied or the agent
///        returns an illegal pick: first creature in battlefield order
///        (same posture as <c>TargetPlayerSacrificesCreatureTemplate</c>).
///     4. Move the picked creature to the target's graveyard via
///        <see cref="OracleSpellBinder.MoveToGraveyard"/> with reason
///        <see cref="ZoneMoveReason.Sacrifice"/> (CR 701.16).
///     5. No creatures on the target's battlefield → no-op; the spell
///        still resolves normally.
///
/// ## Why a named factory
/// <c>TargetPlayerSacrificesCreatureTemplate</c> covers this oracle-text
/// pattern with a deterministic first-creature pick. The named factory
/// upgrades the pick to agent-driven
/// (<see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>) — the victim's
/// bot can now prefer sacrificing the weakest creature. Source-generated
/// dispatch (<see cref="NamedCardFactory"/>) prefers the factory; the
/// template remains as fallback for Cruel Edict / Innocent Blood-shaped
/// oracle text not yet listed by name.
///
/// ## Deferred (v1 gaps)
/// - <b>Forced sacrifice prompt UI</b>: the agent receives the full
///   creature list. A future revision could surface the choice to the
///   portal's decision panel.
/// </summary>
[CardName("Diabolic Edict")]
public static class DiabolicEdictFactory
{
    public const string CardName = "Diabolic Edict";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>
    /// Build a Diabolic Edict instant owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// target request + sacrifice body is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Diabolic Edict is
    /// cast. Single 1..1 "target player" request; on resolution that player
    /// sacrifices a creature of their choice (CR 701.16). No-op when the
    /// target controls no creatures.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="agent">Optional agent for the <em>target player</em>
    /// (the one who must sacrifice). When null, the pick falls back
    /// deterministically to the first creature in battlefield order —
    /// matches <c>TargetPlayerSacrificesCreatureTemplate</c> and keeps
    /// Annihilator / Bone Splinters test-fixture parity.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: target player sacrifices a creature", () =>
                    {
                        // CR 608.2b — illegal-target guard. If the resolver
                        // returns something that is not a Player the spell
                        // fizzles; no sacrifice occurs.
                        if (raw is not Player victim) return;

                        // Gather every creature the target player controls
                        // on the battlefield (pre-filtered to legal picks).
                        var creatures = victim.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .Cast<ICard>()
                            .ToList();

                        // No creatures → no-op (CR 701.16 can't be executed
                        // when the player controls no creatures; the spell
                        // still resolves without effect — same as Innocent
                        // Blood / Cruel Edict against a creatureless board).
                        if (creatures.Count == 0) return;

                        // "Of their choice" — agent drives the pick when
                        // available (BotIntent.Removal so the bot prefers
                        // the least-valuable creature for the opponent's
                        // agent, or the weakest own creature if the target
                        // is the caster). Deterministic fallback = first
                        // creature in battlefield order.
                        ICard? pick = null;
                        if (agent != null)
                        {
                            pick = agent
                                .ChooseFromBattlefieldAsync(victim, creatures, BotIntent.Removal)
                                .GetAwaiter().GetResult();

                            // Validate the agent pick: must be a creature
                            // still on the battlefield and controlled by
                            // the victim. Invalid → deterministic fallback.
                            if (pick == null
                                || pick.Zone != ZoneType.Battlefield
                                || !pick.HasType(CardType.Creature)
                                || !ReferenceEquals(pick.Controller, victim))
                            {
                                pick = creatures[0];
                            }
                        }
                        else
                        {
                            pick = creatures[0];
                        }

                        // CR 701.16 — sacrifice: move permanent from
                        // battlefield to its owner's graveyard. Bypasses
                        // Indestructible and regeneration shields.
                        OracleSpellBinder.MoveToGraveyard(pick, ZoneMoveReason.Sacrifice);
                    }),
                };
            });
    }
}
