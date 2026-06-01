using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghostly Flicker (Commander 2013 / various reprints,
/// {2}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Exile two target artifacts, creatures, and/or lands you control, then
///    return those cards to the battlefield under your control."
///
/// Ghostly Flicker is the two-target, self-blink ("flicker") variant in the
/// <see cref="EphemerateFactory"/> / <see cref="FlickerwispFactory"/> family.
/// Unlike Ephemerate (single creature) it targets exactly two permanents
/// drawn from the artifact/creature/land types you control, and unlike
/// Flickerwisp it returns the cards immediately in the same resolution (no
/// delayed end-step trigger).
///
/// ## Implemented (v1)
/// - Instant shape {2}{U}, blue — built from the embedded JSON definition
///   (<c>ghostly-flicker.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>. Same load path as
///   <see cref="ThisTownAintBigEnoughFactory"/>.
/// - <b>Blink (CR 701.21 + CR 614)</b>: <see cref="BuildDefinition"/> declares
///   one 2..2 "two target artifacts, creatures, and/or lands you control"
///   <see cref="TargetRequest"/>. "Two target" is exactly two (MinTargets=2,
///   MaxTargets=2 — CR 601.2c); the gatherer scopes to the caster's own
///   battlefield permanents whose card type is Artifact, Creature, or Land
///   (CR 109.5 / CR 608.2b — "you control" reads Permanent.Controller at
///   choose-time). On resolution each chosen target that is still a
///   controller-side battlefield permanent is exiled then immediately
///   returned to the battlefield under the caster's control (the controller,
///   per "under your control" — CR 614). Each target resolves independently;
///   one that has left the battlefield is a no-op without affecting the other
///   (CR 608.2c). CR 400.7 — the returned card is a new object.
///
/// ## Notes
/// - Single 2..2 request shape (not two 1..1 requests) mirrors
///   <see cref="ThisTownAintBigEnoughFactory"/>'s "up to two" single-request
///   multi-target gather (and <see cref="ElectrolyzeFactory"/>). A spell that
///   needs two *distinct* targets is enforced by the targeting subsystem's
///   distinct-pick rule for a single multi-target request (CR 601.2c — "the
///   same object can't be chosen to satisfy more than one targeting
///   instance"); a 2..2 single request supplies exactly two distinct picks.
/// - "Under your control" (CR 614) — the return routes the card back under the
///   caster's control. Because the targets must be permanents the caster
///   already controls, owner and controller coincide here; the resolve still
///   re-asserts the caster as controller for correctness.
/// </summary>
[CardName("Ghostly Flicker")]
public static class GhostlyFlickerFactory
{
    public const string CardName = "Ghostly Flicker";
    public const string Slug = "ghostly-flicker";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>CR 601.2c — "Exile two target ..." — exactly two targets.</summary>
    public const int TargetCount = 2;

    /// <summary>
    /// Build the card shape from the embedded JSON definition. The resolve-time
    /// SpellDefinition (the blink) is built on demand via
    /// <see cref="BuildDefinition"/> — mirrors
    /// <see cref="ThisTownAintBigEnoughFactory.Create"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "Exile two target artifacts, creatures, and/or lands you
    /// control, then return those cards to the battlefield under your control"
    /// SpellDefinition. Single 2..2 request scoped to the caster's own
    /// artifact/creature/land permanents.
    /// </summary>
    /// <param name="caster">The spell's controller. "You control" + "under
    /// your control" both read against this player (CR 109.5 / CR 614).</param>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "two target artifacts, creatures, and/or lands you control",
                    MinTargets: TargetCount,
                    MaxTargets: TargetCount,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    // Controller-scoped gather. CR 109.5 / CR 608.2b — "you
                    // control" reads Permanent.Controller at choose-time, and
                    // the type filter accepts only Artifact / Creature / Land.
                    CandidateGatherer: ctx => caster.Zones.Battlefield.GetCards()
                        .OfType<Permanent>()
                        .Where(p => ReferenceEquals(p.Controller, caster)
                                    && (p.HasType(CardType.Artifact)
                                        || p.HasType(CardType.Creature)
                                        || p.HasType(CardType.Land)))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var rawTargets = chosen.Targets.Count > 0
                    ? chosen.Targets[0]
                    : Array.Empty<object>();

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile two target permanents you control, then return them under your control",
                        () =>
                        {
                            // CR 608.2c — resolve each chosen target
                            // independently; an illegal-at-resolution pick
                            // (left the battlefield, or no longer controlled)
                            // is a no-op without affecting the other target.
                            foreach (var raw in rawTargets)
                            {
                                Blink(raw, caster);
                            }
                        }),
                };
            });
    }

    /// <summary>
    /// Exile then immediately return one chosen permanent under the caster's
    /// control (CR 701.21 + CR 614). Mirrors the raw owner-routed zone moves in
    /// <see cref="EphemerateFactory.BuildSpellDefinition"/>.
    /// </summary>
    private static void Blink(object raw, Player caster)
    {
        // CR 608.2b — target must still be a permanent on the battlefield that
        // the caster controls ("you control").
        if (raw is not Permanent target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (!ReferenceEquals(target.Controller, caster)) return;

        var targetOwner = target.Owner ?? caster;

        // CR 701.21 — Exile via owner-routed zone moves so LTB events fire.
        targetOwner.Zones.Battlefield.RemoveCard(target);
        targetOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);

        // CR 111.8 — tokens cease to exist when they leave the battlefield;
        // guard defensively so a token blink no-ops rather than resurrecting a
        // nonexistent object (same posture as Ephemerate / Charming Prince).
        if (target.Zone != ZoneType.Exile) return;

        // CR 614 — return to the battlefield "under your control" in the same
        // resolution (no delayed trigger). CR 400.7 — a new object.
        targetOwner.Zones.Exile.RemoveCard(target);
        caster.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);
        target.SetController(caster);   // "under your control" (CR 614)
    }
}
