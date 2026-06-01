using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reality Shift (Fate Reforged, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Exile target creature. Its controller manifests the top card of
///    their library. (That player puts the top card of their library
///    onto the battlefield face down as a 2/2 creature. If it's a
///    creature card, it can be turned face up any time for its mana
///    cost.)"
///
/// ## Implemented (v1)
///
/// - <b>Instant — {1}{U}</b>, mana value 2. Card shape comes from the
///   embedded JSON (<c>reality-shift.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> — same data-driven shape as
///   <see cref="PlayWithFireFactory"/>.
/// - <b>On-resolve "Exile target creature" + manifest</b>, exposed via
///   <see cref="BuildSpellDefinition"/>. Single 1..1 "target creature"
///   <see cref="TargetRequest"/> (Intent <see cref="BotIntent.Removal"/>,
///   live gatherer = all battlefield creatures across every player),
///   mirroring <see cref="FellFactory"/>.
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. <b>Exile the targeted creature</b> (CR 701.31a / CR 110.2 — the
///      creature is put into exile) via
///      <see cref="Fx.MoveToExile(ICard)"/>. CR 608.2b illegal-target
///      guard: if the targeted creature is no longer a creature on the
///      battlefield at resolution, the whole effect is a no-op (so no
///      manifest happens either — the manifest is part of the same
///      one-shot sentence chained off the exile).
///   2. <b>"Its controller manifests the top card of their library"</b>
///      (CR 701.31) via <see cref="ManifestEffect.Resolve(Player, ZoneService?)"/>.
///      The manifesting player is the exiled creature's <em>controller</em>
///      read BEFORE the exile (CR 608.2 — the effect uses the last-known
///      controller of a permanent that left the battlefield), NOT the
///      caster. The top card becomes a face-down 2/2
///      <see cref="ManifestedCreature"/>; a creature underneath gets the
///      "turn face up for its mana cost" activated ability (CR 701.31c /
///      CR 708.6). An empty library makes the manifest a clean no-op
///      (the exile still happened).
///
/// CR rule references: 110.2 / 701.31a (exile target creature),
/// 701.31 (manifest), 701.31c / 708.6 (turn face up), 708.2 (face-down
/// 2/2), 608.2b (illegal target → no-op), 608.2 (last-known controller).
/// </summary>
[CardName("Reality Shift")]
public static class RealityShiftFactory
{
    public const string CardName = "Reality Shift";
    public const string Slug = "reality-shift";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature" request; on resolution the targeted creature is
    /// exiled (CR 701.31a) iff it is still a creature on the battlefield
    /// (CR 608.2b — illegal target → no-op), then that creature's
    /// controller manifests the top card of their library (CR 701.31).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="zones">Optional <see cref="ZoneService"/> for
    /// event-routed manifest resolution (ETB triggers / replacement
    /// effects). When null a raw-zone fallback is used.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: all creatures on the battlefield across
                    // every player. Bot ranks opponent creatures highest via
                    // Removal intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target creature; its controller manifests",
                        () =>
                        {
                            if (raw is not Creature target) return;

                            // CR 608.2b — resolution-time legality check.
                            // Target must still be a creature on the
                            // battlefield; otherwise the whole effect is a
                            // no-op (no exile, no manifest).
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 608.2 — capture the controller BEFORE the
                            // creature leaves the battlefield; that player
                            // is the one who manifests (not the caster).
                            var controller = target.Controller ?? target.Owner;

                            // CR 701.31a / CR 110.2 — exile the targeted
                            // creature.
                            Fx.MoveToExile(target);

                            // CR 701.31 — its controller manifests the top
                            // card of their library. Empty library → clean
                            // no-op.
                            if (controller is not null)
                            {
                                ManifestEffect.Resolve(controller, zones);
                            }
                        }),
                };
            });
    }
}
