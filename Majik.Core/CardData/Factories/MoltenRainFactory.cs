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
/// Named-card factory for Molten Rain (Tempest / many reprints, {1}{R}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy target land. If that land was nonbasic, Molten Rain deals 2
///    damage to the land's controller."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{R}{R}, red (two {R} pips, CR 105.2a).
///   Card shape comes from the embedded JSON (<c>molten-rain.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Destroy target land</b> — <see cref="BuildDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target land"
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>)
///   whose live <c>CandidateGatherer</c> enumerates every land permanent on
///   every player's battlefield (CR 305).
/// - On resolution (CR 608.2b illegal-target re-check): if the target is still
///   a land permanent on the battlefield it is destroyed (CR 701.7); otherwise
///   the entire spell is a no-op (no destroy, no damage).
/// - <b>Conditional damage rider</b> — "If that land was nonbasic, ... deals 2
///   damage to the land's controller." The land's nonbasic-ness AND its
///   controller are both captured BEFORE the destroy moves it off the
///   battlefield (Controller / Supertypes become unreliable after the card
///   changes zones). "Nonbasic" = the land does NOT have the Basic supertype
///   (CR 205.4a — Basic is a supertype). If nonbasic, deal 2 damage to the
///   captured controller via <see cref="Fx.DealDamage"/> (CR 119).
///
/// Same destroy-land shape as <see cref="BefoulFactory"/> /
/// <see cref="SunderingEruptionFactory"/>; same "destroy + conditional damage
/// to the destroyed permanent's controller" rider shape as
/// <see cref="SmashToSmithereensFactory"/> (which destroys an artifact and
/// deals damage to its controller).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are honoured
/// by the Destroy-reason gate in <see cref="OracleSpellBinder.MoveToGraveyard"/>.
/// </summary>
[CardName("Molten Rain")]
public static class MoltenRainFactory
{
    public const string CardName = "Molten Rain";
    public const string Slug = "molten-rain";
    public const string PrintedManaCost = "{1}{R}{R}";

    /// <summary>CR 119 — 2 damage dealt to a nonbasic land's controller.</summary>
    public const int Damage = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "destroy target land; if nonbasic, deal 2 to its controller"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve:
    /// <list type="number">
    ///   <item>Validate the resolved target is still a <see cref="Permanent"/>
    ///     on the Battlefield AND has type <see cref="CardType.Land"/>
    ///     (CR 608.2b — illegal target → entire spell no-op; no destroy,
    ///     no damage).</item>
    ///   <item>Capture the land's controller AND nonbasic-ness BEFORE
    ///     destroying (the card leaves the battlefield on destroy, after which
    ///     Controller / Supertypes are unreliable).</item>
    ///   <item>Destroy via <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    ///     <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).</item>
    ///   <item>If the destroyed land was nonbasic (no Basic supertype,
    ///     CR 205.4a), deal <see cref="Damage"/> (2) to the captured controller
    ///     via <see cref="Fx.DealDamage"/> (CR 119).</item>
    /// </list>
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
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: all land permanents across every player.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: destroy target land; if nonbasic deal {Damage} to its controller",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // Target must still be a land on the battlefield.
                            // If not, the entire spell does nothing (no destroy,
                            // no damage).
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Land)) return;

                            // Capture BEFORE destroying: the permanent leaves the
                            // battlefield during destruction, after which
                            // Controller (and the live Supertypes) may be stale.
                            // CR 205.4a — "Basic" is a supertype; nonbasic = no
                            // Basic supertype.
                            var controller = target.Controller;
                            var wasNonbasic = !target.HasSupertype(CardSupertype.Basic);

                            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
                            // regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);

                            // CR 119 — "If that land was nonbasic, Molten Rain
                            // deals 2 damage to the land's controller." Both
                            // clauses key on the same legal-target check
                            // (CR 608.2b), so damage only fires when the destroy
                            // was attempted on a nonbasic land.
                            if (wasNonbasic && controller is not null)
                                Fx.DealDamage(controller, Damage);
                        }),
                };
            });
    }
}
