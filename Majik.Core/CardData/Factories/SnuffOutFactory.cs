using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snuff Out (Mercadian Masques / reprints, {3}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "If you control a Swamp, you may pay 4 life rather than pay this
///    spell's mana cost.
///    Destroy target nonblack creature. It can't be regenerated."
///
/// ## Why it gets its own factory
/// Snuff Out is the free-removal black staple: a Terror-style
/// "destroy target nonblack creature, can't be regenerated" whose pay-4-life
/// alternative cost (gated on controlling a Swamp) lets it be cast for no mana.
/// It combines the destroy-nonblack-creature resolve of
/// <see cref="TerrorFactory"/> (CR 701.7 + CR 701.15c — minus Terror's
/// nonartifact rider; Snuff Out only filters on colour) with a new pay-life
/// alternative cost (<see cref="Majik.Core.Costs.PayLifeIfControlSwampAlternativeCost"/>)
/// that mirrors the life-rider posture of <see cref="ForceOfWillFactory"/> /
/// <c>PitchAlternativeCost</c>. All primitives already ship — no new engine
/// mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{B}, black. Card shape comes from the
///   embedded JSON (<c>snuff-out.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Pay-life alternative cost (CR 118.9)</b>: callers supply
///   <see cref="Majik.Core.Costs.PayLifeIfControlSwampAlternativeCost"/>
///   (pay 4 life, requires controlling a Swamp) to
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>. The alt-cost's
///   <c>CanCastFor</c> gates on the caster controlling a Swamp and having
///   &gt;= 4 life (CR 119.4); the life is paid on resolution.
/// - <b>Destroy target nonblack creature (CR 701.7 + CR 701.15c)</b> —
///   <see cref="BuildDefinition"/> builds a single 1..1 "target nonblack
///   creature" <see cref="TargetRequest"/>. On resolution the chosen creature
///   is validated as still a battlefield Creature and nonblack
///   (CR 105 — colour derived from mana cost pips; cards with no {B} pip are
///   nonblack), then destroyed via
///   <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/> so
///   indestructible (CR 702.12b) still cancels the destroy but any
///   regeneration shield is bypassed ("it can't be regenerated").
///   CR 608.2b — illegal target at resolution → effect does nothing.
///
/// ## Rules citations
/// - CR 118.9 — alternative cost ("pay 4 life rather than ... mana cost").
/// - CR 119.4 — can't pay life you don't have.
/// - CR 105 — colour from mana-cost pips (the nonblack filter).
/// - CR 701.7 + CR 701.15c — Destroy; "can't be regenerated".
/// - CR 702.12b — Indestructible still prevents the destroy.
/// - CR 608.2b — illegal target at resolution → no-op.
/// </summary>
[CardName("Snuff Out")]
public static class SnuffOutFactory
{
    public const string CardName = "Snuff Out";
    public const string Slug = "snuff-out";
    public const string PrintedManaCost = "{3}{B}";

    /// <summary>CR 118.9 — life paid by the alternative cost.</summary>
    public const int AlternativeLifeCost = 4;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "destroy target nonblack creature; it can't be regenerated"
    /// spell definition used when Snuff Out resolves.
    ///
    /// On resolve: validates the target is still a Creature on the
    /// Battlefield and is nonblack (CR 105 colour filter, CR 608.2b illegal-
    /// target filter at resolution). When valid, destroys it via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
    /// (CR 701.7 + CR 701.15c) so indestructible prevents the destroy
    /// (CR 702.12b) but regeneration shields are bypassed.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
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
                    Description: "target nonblack creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Gather every nonblack creature on any battlefield.
                    // Removal intent in the bot's ranker pushes the
                    // opponent's biggest qualifying threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !CardColors.GetColors(c).Contains(ManaColor.Black))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target nonblack creature (can't be regenerated)",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 105 — nonblack filter (no {B} pip in mana cost).
                            if (CardColors.GetColors(target).Contains(ManaColor.Black)) return;

                            // CR 701.7 + CR 701.15c — Destroy with "can't be
                            // regenerated". Indestructible (CR 702.12b) still
                            // cancels the destroy; any regeneration shield is
                            // bypassed (DestroyNoRegeneration).
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
