using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cogwork Wrestler (Modern Horizons 3, {U}).
///
/// Artifact Creature — Gnome 1/2. Oracle text (Scryfall, verified):
///   "Flash
///    When this creature enters, target creature an opponent controls gets
///    -2/-0 until end of turn."
///
/// ## Shape source
/// Card identity (name, {U}, 1/2, Artifact Creature — Gnome, Flash) is loaded
/// from <c>Majik.Core/CardData/Cards/cogwork-wrestler.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (the <c>keywords</c> array carries Flash,
/// CR 702.8). The ETB target trigger is attached in code below — same posture as
/// <see cref="MerfolkTricksterFactory"/> (an ETB "target creature an opponent
/// controls …" trigger with a resolution-time opponent-control + battlefield
/// re-check) and <see cref="NightshadeAssassinFactory"/> (which feeds a delta
/// into the shared Layer-7c <see cref="PumpUntilEndOfTurnEffect"/> primitive).
///
/// ## Implemented (v1)
/// - 1/2 Artifact Creature — Gnome at {U} with Flash (from JSON).
/// - <b>Flash</b> (CR 702.8) — KeywordAbility marker from the JSON
///   <c>keywords</c> array; the cast-flow consults it for instant-speed casting.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature enters,
///   target creature an opponent controls gets -2/-0 until end of turn." Wired
///   via <see cref="Triggers.OnEnterBattlefieldSelf"/> with a single 1..1
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>).
///   On resolution the effect reads the trigger's
///   <see cref="TriggeredAbility.ChosenTargets"/>, validates the chosen Creature
///   is still on the battlefield (CR 608.2b — clean no-op on fizzle) and
///   controlled by anyone OTHER than Cogwork Wrestler's controller ("an opponent
///   controls" re-check, CR 608.2b), then registers a −2/+0
///   <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c, CR 611 / CR 514.2) on the
///   target's own <see cref="Permanent.ActiveEffects"/>. The effect auto-expires
///   in the cleanup step (CR 514.2). −2/-0 only lowers power, so it never kills
///   the creature directly — it is a combat-tempo debuff.
/// - "Creature an opponent controls" filter: <see cref="TargetRequest.LegalCandidates"/>
///   left empty so the targeting prompt accepts any Creature (same posture as
///   <see cref="MerfolkTricksterFactory"/> / Solitude); the resolve-time recheck
///   enforces opponent-control + battlefield-zone.
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time legality filter</b>: <see cref="TargetRequest.LegalCandidates"/>
///   is empty — production callers wanting agent-side filtering populate it
///   themselves (same posture as <see cref="MerfolkTricksterFactory"/>).
/// - <b>Single-arg dispatcher fallback</b>: when the target has no live
///   <see cref="ContinuousEffectsService"/> wired (shape-only tests) the −2/-0
///   registration silently no-ops (same posture as <see cref="MerfolkTricksterFactory"/>).
/// </summary>
[CardName("Cogwork Wrestler")]
public static class CogworkWrestlerFactory
{
    public const string CardName = "Cogwork Wrestler";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cogwork-wrestler");

    /// <summary>
    /// Construct Cogwork Wrestler owned and controlled by <paramref name="owner"/>.
    /// Flash is attached from the JSON keyword array; the ETB triggered ability is
    /// attached to the card shape with a 1..1 "target creature an opponent
    /// controls" <see cref="TargetRequest"/>.
    ///
    /// On ETB resolution the chosen target is gated by a still-on-battlefield +
    /// opponent-control recheck (CR 608.2b). When both pass, a −2/-0
    /// <see cref="PumpUntilEndOfTurnEffect"/> is registered against the target's
    /// <see cref="Permanent.ActiveEffects"/>. When the target has no live
    /// <see cref="ContinuousEffectsService"/> wired (shape-only tests) the grant
    /// silently no-ops.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.6a — ETB triggered ability with target.
        //   "When this creature enters, target creature an opponent controls gets
        //    -2/-0 until end of turn."
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName} — target creature an opponent controls gets -2/-0 until end of turn",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — illegal-target check at resolution.
                if (target.Zone != ZoneType.Battlefield) return;

                // "Creature an opponent controls" — re-validate the controller
                // relationship at resolution (CR 608.2b). The target must be
                // controlled by anyone OTHER than Cogwork Wrestler's controller.
                var myController = card.Controller ?? owner;
                if (target.Controller is null) return;
                if (ReferenceEquals(target.Controller, myController)) return;

                // CR 611 / CR 514.2 — Layer 7c −2/-0 until end of turn, scoped to
                // the chosen creature, expiring in the cleanup step. Registered
                // against the target's own ContinuousEffectsService
                // (Permanent.ActiveEffects) — when null (shape-only path) silently
                // no-op (same posture as MerfolkTricksterFactory).
                if (target.ActiveEffects == null) return;
                target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, -2, 0));
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
