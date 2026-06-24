using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elspeth's Smite (March of the Machine, {W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Elspeth's Smite deals 3 damage to target attacking or blocking creature.
///    If that creature would die this turn, exile it instead."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}, white. Card shape comes from the embedded
///   JSON (<c>elspeths-smite.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same load path as
///   <see cref="TorchTheTowerFactory"/>).
/// - <b>"Target attacking or blocking creature"</b> — single 1..1
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>),
///   gated to creatures currently in combat. Same combat-creature injection
///   shape as <see cref="RazorgrassAmbushFactory"/>: production callers wire a
///   delegate that reads <see cref="Majik.Core.Combat.CombatManager.CurrentCombat"/>
///   (attackers + all blockers); test callers inject the list directly; a null
///   delegate yields an empty candidate pool (shape-only / dispatcher path).
///   Unlike Scorching Dragonfire / Torch the Tower this card never targets a
///   planeswalker — only creatures in combat are legal (CR 115.4 / CR 509),
///   so the resolve body and exile rider are creature-only.
/// - <b>Resolve — deal 3 damage</b> to the chosen creature via
///   <see cref="Fx.DealDamage(object, int)"/> (CR 119). CR 608.2b — a target
///   that is no longer a Creature on the battlefield at resolution is a no-op
///   (illegal-target fizzle).
/// - <b>Exile rider</b> — when a <see cref="ReplacementBus"/> is supplied,
///   register an EOT-expirable <see cref="AngerOfTheGodsExileInsteadReplacement"/>
///   (the shared "damaged-this-way dies → exile" replacement, CR 700.3 /
///   CR 514.2) scoped to the single damaged creature, so its lethal
///   battlefield→graveyard move is rewritten to exile until end of turn. Null
///   bus → damage only (shape tests). Mirrors
///   <see cref="ScorchingDragonfireFactory"/>'s exile rider; here it is
///   unconditionally creature-scoped because the target is always a creature.
/// </summary>
[CardName("Elspeth's Smite")]
public static class ElspethsSmiteFactory
{
    public const string CardName = "Elspeth's Smite";
    public const string Slug = "elspeths-smite";
    public const string PrintedManaCost = "{W}";

    /// <summary>Damage dealt to the chosen attacking or blocking creature.</summary>
    public const int Damage = 3;

    /// <summary>Construct Elspeth's Smite from its embedded JSON shape.</summary>
    public static Cards.Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Cards.Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target attacking or blocking creature" request; on resolution the chosen
    /// creature is dealt <see cref="Damage"/> (3) via
    /// <see cref="Fx.DealDamage(object, int)"/> (CR 119), and — when a
    /// <paramref name="replacements"/> bus is supplied — its lethal
    /// battlefield→graveyard move is rewritten to exile until end of turn
    /// (CR 700.3 / CR 514.2).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand permanents directly.</param>
    /// <param name="combatCreatureLookup">Returns all attacking and blocking
    /// creatures currently in combat. Production callers wire this from
    /// <see cref="Majik.Core.Combat.CombatManager.CurrentCombat"/>; test callers
    /// inject a list directly. Null (or a delegate returning null/empty) yields
    /// an empty candidate pool (shape-only / dispatcher path — no live combat).</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> the
    /// exile rider registers onto; null → damage only.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<IReadOnlyList<Creature>>? combatCreatureLookup = null,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target attacking or blocking creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal,
                    // Live gatherer: all attacking and blocking creatures from
                    // the current combat (CR 508 attackers / CR 509 blockers),
                    // injected by the caller so the factory stays testable
                    // without a live combat loop. Null delegate → empty pool.
                    CandidateGatherer: _ =>
                    {
                        if (combatCreatureLookup == null)
                            return Array.Empty<object>();

                        var pool = combatCreatureLookup() ?? Array.Empty<Creature>();
                        return pool.Cast<object>().ToList();
                    }),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: {Damage} damage to target attacking or blocking creature; if that creature would die this turn, exile it instead.",
                        () =>
                        {
                            // CR 608.2b — illegal-target re-check: only deal
                            // damage when the resolved target is still a
                            // Creature on the battlefield.
                            if (target is not Creature creature) return;
                            if (creature.Zone != ZoneType.Battlefield) return;

                            // CR 119 — deal 3 damage to the target creature.
                            Fx.DealDamage(creature, Damage);

                            // CR 700.3 / CR 514.2 — exile-instead rider, scoped
                            // to the single damaged creature. The target is
                            // always a creature, so the rider is unconditionally
                            // creature-scoped (cf. Scorching Dragonfire, which
                            // also targets planeswalkers and arms the rider only
                            // for a Creature target).
                            if (replacements != null)
                            {
                                replacements.Register<ZoneMoveIntent>(
                                    new AngerOfTheGodsExileInsteadReplacement(
                                        new HashSet<Creature> { creature }));
                            }
                        }),
                };
            });
    }
}
