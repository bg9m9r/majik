using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Exorcise (Tarkir: Dragonstorm, {1}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Exile target artifact, enchantment, or creature with power 4 or greater."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {1}{W}; mana value 2</item>
///   <item>Type line: Sorcery; colors: W</item>
/// </list>
///
/// Same exile-target-removal shape as <see cref="SwordsToPlowsharesFactory"/>
/// (a 1..1 target request whose effect routes the chosen permanent to exile),
/// but with a broadened target predicate like
/// <see cref="WarpingWailFactory"/>'s modal exile: the legal target is any
/// <see cref="Permanent"/> that is an artifact, an enchantment, OR a creature
/// with power 4 or greater. Per the oracle grammar the "power 4 or greater"
/// clause binds ONLY to the creature option — artifacts and enchantments
/// qualify regardless of power (they need not even be creatures).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W} (white). The base card shape is loaded
///   from the embedded JSON definition (<c>exorcise.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/> — same posture as the other
///   data-backed factories.
/// - <b>Exile the chosen permanent</b>: <see cref="BuildDefinition"/> declares
///   one 1..1 "target artifact, enchantment, or creature with power 4 or
///   greater" <see cref="TargetRequest"/>. CR 608.2b — the predicate is
///   re-checked at resolution; an illegal target (gone, or no longer matching)
///   is a no-op. CR 701.20 — exile (not "destroy"); Indestructible does not
///   protect against exile.
///
/// ## Notes
/// - The candidate gatherer offers every matching permanent across all
///   battlefields (Removal intent pushes the opponent's biggest qualifying
///   threat to the top of the bot's ranker).
/// </summary>
[CardName("Exorcise")]
public static class ExorciseFactory
{
    public const string CardName = "Exorcise";
    public const string Slug = "exorcise";

    /// <summary>The creature-only power threshold (CR 613 — current power at
    /// the time the gate is evaluated).</summary>
    public const int CreaturePowerThreshold = 4;

    /// <summary>
    /// Construct Exorcise as a Sorcery card with owner / controller wired.
    /// The exile-target body is built on demand via
    /// <see cref="BuildDefinition"/> (mirrors
    /// <see cref="SwordsToPlowsharesFactory"/>). The base shape (name, Sorcery,
    /// {1}{W}, white) is materialised from the embedded JSON definition.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// CR 613 — a permanent is a legal Exorcise target iff it is an artifact,
    /// an enchantment, OR a creature with power 4 or greater. The power gate
    /// binds only to the creature branch.
    /// </summary>
    public static bool IsLegalTarget(Permanent permanent)
    {
        if (permanent is null) return false;
        if (permanent.HasType(CardType.Artifact)) return true;
        if (permanent.HasType(CardType.Enchantment)) return true;
        return permanent is Creature creature
            && creature.Power >= CreaturePowerThreshold;
    }

    /// <summary>
    /// Build the "exile target artifact, enchantment, or creature with power 4
    /// or greater" SpellDefinition.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact, enchantment, or creature with power 4 or greater",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live-gather every matching permanent on
                    // the battlefield. Removal intent ranks the opponent's
                    // biggest qualifying threat to the top.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(IsLegalTarget)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    Fx.Inline(
                        "Exorcise — exile target artifact, enchantment, or creature with power 4 or greater",
                        () =>
                        {
                            if (resolved is not Permanent target) return;

                            // CR 608.2b — illegal target at resolution → no-op.
                            // Re-check both that it is still on the battlefield
                            // and that it still matches the predicate (a pumped
                            // creature may have dropped below power 4).
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!IsLegalTarget(target)) return;

                            // CR 701.20 — exile (not "destroy"); Indestructible
                            // does not protect against exile.
                            Fx.MoveToExile(target);
                        }),
                };
            });
    }
}
