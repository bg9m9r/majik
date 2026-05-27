using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Acrobatic Maneuver (Kaladesh, {2}{W}).
///
/// Instant. Scryfall oracle (verified):
///   "Exile target creature you control, then return that card to the
///    battlefield under its owner's control.
///    Draw a card."
///
/// Acrobatic Maneuver is the canonical "Cloudshift plus a cantrip" — the
/// flicker half is identical to <see cref="CloudshiftFactory"/>'s body
/// (CR 701.21 + CR 614, returned creature is a new object per CR 400.7),
/// followed by a card draw under the spell's controller (CR 121.1).
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{W}, owner / controller.
/// - <b>Cast body</b> — <see cref="BuildSpellDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target creature you
///   control" <see cref="TargetRequest"/>. Live <c>CandidateGatherer</c>
///   walks the casting controller's battlefield for Creature permanents
///   (controller-scoped — CR 109.5 "you control" reads
///   <see cref="Permanent.Controller"/>). Bot intent
///   <see cref="BotIntent.Protection"/>.
/// - <b>Resolve</b>: re-checks the target is still a controller-side
///   battlefield Creature (CR 608.2b — illegal target → "no effect" for
///   the flicker half, but the draw still happens because "Draw a card"
///   is NOT contingent on the target — CR 608.2b only fizzles target-
///   contingent effect lines). Moves the creature Battlefield → Exile
///   (CR 701.21), then immediately moves it Exile → Battlefield under
///   its owner's control (CR 614). Both moves are owner-routed so LTB /
///   ETB events fire on each transition. After the flicker resolves
///   (or no-ops on an illegal target), the spell's controller draws one
///   card via <see cref="Fx.DrawCards"/> (CR 121.1 — Fx routes through
///   the shared empty-library state-loss path per CR 704.5b).
///
/// ## Deferred (v1 gaps)
/// - <b>Token blink</b>: tokens exiled cease to exist (CR 111.8). The
///   return guards on <c>Zone == Exile</c> so a vanished token is
///   skipped (same defensive posture as <see cref="CloudshiftFactory"/>
///   / <see cref="EphemerateFactory"/>).
/// - <b>Synchronous ETB-trigger ordering</b>: both the exile and the
///   re-enter publish <see cref="Majik.Core.Events.CardMovedEvent"/>
///   indirectly through the owner's zone collections; downstream ETB
///   triggers stack in registration order. Same posture as Cloudshift /
///   Ephemerate at v1.
/// </summary>
[CardName("Acrobatic Maneuver")]
public static class AcrobaticManeuverFactory
{
    public const string CardName = "Acrobatic Maneuver";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>Construct Acrobatic Maneuver as an Instant owned and
    /// controlled by <paramref name="owner"/>. Card shape only — the
    /// resolve closure is produced by <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> for Acrobatic Maneuver.
    /// Single 1..1 "target creature you control" request; on resolve,
    /// exile-then-immediate-return via owner-routed zone moves, then the
    /// spell's controller draws one card (CR 121.1). The draw fires
    /// unconditionally — CR 608.2b only fizzles the target-contingent
    /// flicker half, not the draw rider.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    // Controller-scoped gather. CR 109.5 / CR 608.2b — "you
                    // control" reads off Permanent.Controller at choose-time.
                    CandidateGatherer: ctx => caster.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => ReferenceEquals(c.Controller, caster))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                // Even an empty-target cast still resolves the draw rider —
                // but with no chosen target the spell would have failed to
                // be cast (MinTargets = 1). Bail cleanly if the target
                // shape isn't honoured by the caller.
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    return Array.Empty<IEffect>();
                }
                if (chosen.Targets[0][0] is not Creature target)
                {
                    return Array.Empty<IEffect>();
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target creature you control, return it, then draw a card",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check
                            // for the FLICKER half. The draw is unconditional.
                            var flickerLegal =
                                target.Zone == ZoneType.Battlefield
                                && ReferenceEquals(target.Controller, caster);

                            if (flickerLegal)
                            {
                                var targetOwner = target.Owner ?? caster;

                                // CR 701.21 — Exile.
                                targetOwner.Zones.Battlefield.RemoveCard(target);
                                targetOwner.Zones.Exile.AddCard(target);
                                target.SetZone(ZoneType.Exile);

                                // CR 614 — return under the owner's control,
                                // same resolution. Re-entered card is a new
                                // object per CR 400.7.
                                if (target.Zone == ZoneType.Exile)
                                {
                                    targetOwner.Zones.Exile.RemoveCard(target);
                                    targetOwner.Zones.Battlefield.AddCard(target);
                                    target.SetZone(ZoneType.Battlefield);
                                    target.SetController(targetOwner);
                                }
                            }

                            // CR 121.1 — "Draw a card." Unconditional —
                            // fires whether the flicker half succeeded or
                            // fizzled to an illegal target. Fx.DrawCards
                            // routes through the empty-library CR 704.5b
                            // loss flag automatically.
                            Fx.DrawCards(caster, 1);
                        }),
                };
            });
    }
}
