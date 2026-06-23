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
/// Named-card factory for Splash Portal (Bloomburrow, {U}).
///
/// Sorcery. Scryfall oracle (verified):
///   "Exile target creature you control, then return it to the battlefield
///    under its owner's control. If that creature is a Bird, Frog, Otter, or
///    Rat, draw a card."
///
/// Splash Portal is the blue, Bloomburrow-tribal cousin of
/// <see cref="AcrobaticManeuverFactory"/> / <see cref="CloudshiftFactory"/>:
/// the flicker half is the same exile-then-immediate-return body (CR 701.21 +
/// CR 614 — the returned creature is a new object per CR 400.7, so until-EOT
/// effects drop, ETB triggers re-fire, summoning sickness re-applies, counters
/// / damage / attached auras-and-equipment clear), but the cantrip is
/// CONDITIONAL on the returned creature's creature type.
///
/// ## Implemented (v1)
/// - Card shape (name, Sorcery, {U}) from the embedded JSON
///   (<c>splash-portal.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> — same JSON-backed posture as
///   <see cref="InspiringCallFactory"/>. No new mechanic: flicker (Cloudshift /
///   Acrobatic Maneuver) plus a conditional card draw.
/// - <b>Cast body</b> — <see cref="BuildSpellDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target creature you
///   control" <see cref="TargetRequest"/>. Live <c>CandidateGatherer</c>
///   walks the casting controller's battlefield for Creature permanents
///   (controller-scoped — CR 109.5 "you control" reads
///   <see cref="Permanent.Controller"/>). Bot intent
///   <see cref="BotIntent.Protection"/>.
/// - <b>Resolve</b>: re-checks the target is still a controller-side
///   battlefield Creature (CR 608.2b — illegal target → no effect). Moves the
///   creature Battlefield → Exile (CR 701.21), then immediately moves it
///   Exile → Battlefield under its owner's control (CR 614). Both moves are
///   owner-routed so LTB / ETB events fire on each transition.
/// - <b>Conditional draw</b>: "If that creature is a Bird, Frog, Otter, or
///   Rat, draw a card." Unlike Acrobatic Maneuver's UNCONDITIONAL cantrip,
///   this draw is contingent on "that creature" — so when the flicker half
///   fizzles to an illegal target (CR 608.2b) there is no "that creature" to
///   test and the draw does NOT fire. The subtype is read off the creature's
///   live characteristics (<see cref="Card.HasSubtype"/>); a flickered card
///   keeps its printed creature types, so the post-return object answers the
///   check correctly. Draw routes through <see cref="Fx.DrawCards"/> (CR 121.1
///   — empty library stamps the CR 704.5b loss flag without throwing).
///
/// ## Deferred (v1 gaps)
/// - <b>Token blink</b>: tokens exiled cease to exist (CR 111.8). The return
///   guards on <c>Zone == Exile</c> so a vanished token is skipped (same
///   defensive posture as <see cref="CloudshiftFactory"/> /
///   <see cref="AcrobaticManeuverFactory"/>). A vanished token also fails the
///   subtype test (no object to read), so no draw — matching the printed
///   "that creature" contingency.
/// </summary>
[CardName("Splash Portal")]
public static class SplashPortalFactory
{
    public const string CardName = "Splash Portal";
    public const string Slug = "splash-portal";
    public const string PrintedManaCost = "{U}";

    /// <summary>The creature types whose presence on the returned creature
    /// triggers the cantrip (Bloomburrow's "Splash" friends — CR 205.3m).</summary>
    public static readonly IReadOnlyList<CardSubtype> DrawSubtypes = new[]
    {
        CardSubtype.Bird,
        CardSubtype.Frog,
        CardSubtype.Otter,
        CardSubtype.Rat,
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Splash Portal. Single 1..1
    /// "target creature you control" request; on resolve, exile-then-immediate-
    /// return via owner-routed zone moves (CR 701.21 + CR 614), then — only if
    /// the returned creature is a Bird / Frog / Otter / Rat — the spell's
    /// controller draws one card (CR 121.1).
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
                        $"{CardName}: exile target creature you control, return it; if it is a Bird/Frog/Otter/Rat, draw a card",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check for
                            // the FLICKER half. If illegal, there is no "that
                            // creature" and the conditional draw does NOT fire.
                            var flickerLegal =
                                target.Zone == ZoneType.Battlefield
                                && ReferenceEquals(target.Controller, caster);

                            if (!flickerLegal) return;

                            var targetOwner = target.Owner ?? caster;

                            // CR 701.21 — Exile.
                            targetOwner.Zones.Battlefield.RemoveCard(target);
                            targetOwner.Zones.Exile.AddCard(target);
                            target.SetZone(ZoneType.Exile);

                            // CR 614 — return under the owner's control, same
                            // resolution. Re-entered card is a new object per
                            // CR 400.7. Defensive guard: a vanished token
                            // (CR 111.8) won't be in Exile.
                            if (target.Zone != ZoneType.Exile) return;

                            targetOwner.Zones.Exile.RemoveCard(target);
                            targetOwner.Zones.Battlefield.AddCard(target);
                            target.SetZone(ZoneType.Battlefield);
                            target.SetController(targetOwner);

                            // "If that creature is a Bird, Frog, Otter, or Rat,
                            // draw a card." CR 121.1 — contingent on the
                            // returned creature's creature type (CR 205.3m).
                            // The flickered card keeps its printed subtypes, so
                            // the post-return object answers correctly.
                            if (DrawSubtypes.Any(target.HasSubtype))
                            {
                                Fx.DrawCards(caster, 1);
                            }
                        }),
                };
            });
    }
}
