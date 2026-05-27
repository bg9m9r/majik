using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ephemerate (Modern Horizons, {W}).
///
/// Instant. Oracle text:
///   "Exile target creature you control, then return that card to the
///    battlefield under its owner's control.
///    Rebound (If you cast this spell from your hand, exile it as it
///    resolves. At the beginning of your next upkeep, you may cast this
///    card from exile without paying its mana cost.)"
///
/// CR 701.21 + CR 614 — Ephemerate is Cloudshift with Rebound (CR 702.88).
/// The flicker half is identical to <see cref="CloudshiftFactory"/>; the
/// Rebound rider gives Modern decks the doubled-ETB engine that makes
/// Ephemerate a Stoneforge Mystic / Skyclave Apparition staple.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}.
/// - <b>Cast body</b>: single 1..1 "target creature you control"
///   <see cref="TargetRequest"/> via <see cref="BuildSpellDefinition"/>.
///   Same controller-scoped gather as Cloudshift (CR 109.5).
/// - <b>Resolve</b>: re-checks the target is still a controller-side
///   battlefield Creature (CR 608.2b — illegal target → no effect). Moves
///   the creature Battlefield → Exile (CR 701.21), then immediately moves
///   it Exile → Battlefield under its owner's control (CR 614). Token
///   blink defensively guards on <c>Zone == Exile</c> before the return.
/// - <b>Rebound keyword marker</b>: <see cref="KeywordAbility"/>("Rebound")
///   so oracle audits + future bot-decision layers can detect the keyword
///   without scanning the spell-definition shape (matches the
///   <see cref="DriftOfPhantasmsFactory"/> Transmute-marker convention).
///
/// ## Deferred (v1 gaps)
/// - <b>Rebound mechanic</b> (CR 702.88): "If you cast this spell from
///   your hand, exile it as it resolves. At the beginning of your next
///   upkeep, you may cast this card from exile without paying its mana
///   cost." Requires:
///     (1) a hand-source replacement on resolution (instead of routing
///         Stack → Graveyard, route Stack → Exile when the spell was cast
///         from hand — CR 702.88a), and
///     (2) a delayed triggered ability registered on resolve that fires
///         on the controller's next upkeep <see cref="Events.StepStartedEvent"/>
///         and offers a free-cast prompt from exile (CR 702.88b).
///   Neither half exists as a reusable primitive today. The flicker body
///   is shape-correct without Rebound — when the engine surfaces the
///   "cast from exile without paying" cost path, the marker keyword here
///   becomes the wiring point. Tracked alongside Snapcaster Mage's "cast
///   from graveyard" rider as a single "alternative-zone-cast" primitive.
/// </summary>
[CardName("Ephemerate")]
public static class EphemerateFactory
{
    public const string CardName = "Ephemerate";
    public const string PrintedManaCost = "{W}";

    /// <summary>Construct Ephemerate as an Instant owned and controlled by
    /// <paramref name="owner"/>. Attaches the Rebound keyword marker; the
    /// resolve closure (flicker half only — Rebound rider deferred) is
    /// produced by <see cref="BuildSpellDefinition"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword marker — CR 702.88 (Rebound). Attached so oracle audits
        // + KeywordRegistry consumers see the keyword without parsing the
        // SpellDefinition. The actual Rebound rider is deferred (see
        // class xmldoc "Deferred" gap).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Rebound", card, owner));

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Ephemerate. Same shape
    /// as <see cref="CloudshiftFactory.BuildSpellDefinition"/> — single
    /// 1..1 "target creature you control" + immediate exile/return body.
    /// The Rebound exile-on-resolve rider is NOT modelled at this surface
    /// (see class xmldoc — needs a reusable "alternative-zone-cast"
    /// primitive shared with Snapcaster's grave-flashback).
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
                    // control" reads Permanent.Controller at choose-time.
                    CandidateGatherer: ctx => caster.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => ReferenceEquals(c.Controller, caster))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
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
                        $"{CardName}: exile target creature you control, then return it",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!ReferenceEquals(target.Controller, caster)) return;

                            var targetOwner = target.Owner ?? caster;

                            // CR 701.21 — Exile via owner-routed zone moves.
                            targetOwner.Zones.Battlefield.RemoveCard(target);
                            targetOwner.Zones.Exile.AddCard(target);
                            target.SetZone(ZoneType.Exile);

                            // CR 614 — return to battlefield under owner's
                            // control, same resolution (no delayed trigger).
                            // CR 400.7 — the returned card is a new object.
                            if (target.Zone != ZoneType.Exile) return; // token cleanup defensively

                            targetOwner.Zones.Exile.RemoveCard(target);
                            targetOwner.Zones.Battlefield.AddCard(target);
                            target.SetZone(ZoneType.Battlefield);
                            target.SetController(targetOwner);
                        }),
                };
            });
    }
}
