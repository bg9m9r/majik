using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Suppression Ray // Orderly Plaza (Murders at Karlov Manor,
/// {3}{W/U}{W/U}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Tap all creatures target player controls. You may pay any amount of
///    {E}. If you do, choose up to that many creatures tapped this way. Put a
///    stun counter on each of them."
///
/// Back face — <see cref="OrderlyPlazaFactory"/> (Land — "This land enters
/// tapped."; "{T}: Add {W} or {U}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6) — real cast-either-face
///
/// This card is a Modal Double-Faced Card: the two faces share a physical
/// card but each face has its own complete characteristics (cost, type,
/// effect). At cast / play time the controller CHOOSES which face to use
/// (CR 712.3), the cost / effect of that face is what applies, and the
/// resulting stack object / permanent is the chosen face. No transform
/// happens (CR 712.4 — MDFC faces don't transform); the OTHER face simply
/// isn't there.
///
/// The front-face card built here carries an <see cref="MdfcState"/> with a
/// castable <see cref="MdfcFace"/> back-face descriptor (the back face is the
/// LAND Orderly Plaza). At cast time <see cref="MdfcCastFlow"/> reads that
/// descriptor and prompts the controller to pick a face:
/// <list type="bullet">
///   <item><b>Front</b> — cast this <see cref="Sorcery"/> via the normal spell
///     path with {3}{W/U}{W/U} and the mass-tap + optional-energy stun
///     effect.</item>
///   <item><b>Back (Orderly Plaza)</b> — played as a LAND with no stack
///     (CR 305): <see cref="MdfcCastFlow"/> materializes a fresh Orderly Plaza
///     land instance via
///     <see cref="OrderlyPlazaFactory.Create(Player, Majik.Core.Effects.ReplacementBus?)"/>
///     (wired to the live <see cref="Majik.Core.Effects.ReplacementBus"/> so
///     its "enters tapped" ETB fires), and the front-face card is removed from
///     hand — only the chosen land enters.</item>
/// </list>
/// Mirrors <see cref="WaterloggedTeachingsFactory"/> — the spell-front +
/// land-back MDFC posture (instant there, sorcery here).
///
/// ## Implemented (v1)
/// - Sorcery identity at {3}{W/U}{W/U} (identity + printed cost from JSON;
///   the two {W/U} hybrid pips parse to two hybrid pips, CMC 5), owner /
///   controller wired.
/// - <see cref="MdfcState"/> attached (front = "Suppression Ray",
///   back = "Orderly Plaza") WITH a castable back-land descriptor so the
///   land face is playable via the cast-either-face flow / the bot's back-land
///   enumeration.
/// - <b>Front-face effect</b> (CR 701.21a tap; CR 107.16 energy; CR 122.1g
///   stun) — build via <see cref="BuildSpellDefinition"/>:
///   <list type="number">
///     <item>One 1..1 "target player" <see cref="TargetRequest"/>.</item>
///     <item>On resolution, TAP every creature that player controls that is
///       not already tapped — these are the creatures "tapped this way"
///       (CR 701.21a; already-tapped creatures are excluded since they were
///       not tapped by this spell).</item>
///     <item>"You may pay any amount of {E}." then "choose up to that many
///       creatures tapped this way" — modelled as a single PickN prompt over
///       the tapped-this-way set, capped at <c>min(caster energy, count
///       tapped this way)</c>. The number of creatures the caster picks IS the
///       energy paid (there is never a reason to pay energy beyond the
///       creatures you stun), so the caster pays exactly that much {E} via
///       <see cref="Player.PayEnergy"/> and a stun counter
///       (<see cref="CounterType.Stun"/>) is placed on each chosen creature
///       (CR 122.1g — the stun counter replaces the creature's next untap).</item>
///   </list>
///   When the caster has 0 energy, or declines, no stun counters are placed
///   (the tap still happened). The prompt routes through the caster's agent
///   (<see cref="IPlayerAgent.ChooseAsync"/> with
///   <see cref="Players.Agents.ChoiceKind.PickN"/>); a no-agent / decline
///   posture leaves the creatures tapped without stun counters.
///
/// ## Notes
/// - "creatures tapped this way" is captured as a snapshot at the moment the
///   tap happens, so a creature that was already tapped before the spell is
///   never eligible for a stun counter (CR 701.21a).
/// </summary>
[CardName("Suppression Ray")]
public static class SuppressionRayFactory
{
    public const string CardName = "Suppression Ray";
    public const string BackName = "Orderly Plaza";
    public const string Slug = "suppression-ray";

    /// <summary>
    /// Construct Suppression Ray as a Sorcery (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached, carrying a castable
    /// back-face land descriptor. The resolve-time mass-tap + stun
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at cast time and materializes a fresh
        // back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                OrderlyPlazaFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> used when
    /// Suppression Ray is cast: one 1..1 "target player" request; on
    /// resolution all that player's creatures are tapped, then the caster may
    /// pay {E} to put stun counters on up to that many of the creatures tapped
    /// this way.
    /// </summary>
    /// <param name="caster">The spell's controller (chooses how much {E} to
    /// pay and which tapped creatures to stun).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Tests pass
    /// the identity resolver <c>raw =&gt; raw</c>.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? resolver(chosen.Targets[0][0])
                    : null;
                return new IEffect[]
                {
                    new Effect(
                        "Suppression Ray — tap all target player's creatures, "
                        + "then pay {E} to stun up to that many",
                        async ctx =>
                        {
                            // CR 608.2b — illegal-target check.
                            if (raw is not Player victim) return;

                            // CR 701.21a — tap every UNTAPPED creature the
                            // target player controls. The set we actually
                            // tap = "creatures tapped this way", captured as a
                            // snapshot so already-tapped creatures stay
                            // ineligible for a stun counter.
                            var tappedThisWay = new List<Creature>();
                            foreach (var creature in victim.Zones.Battlefield
                                         .GetCards().OfType<Creature>().ToList())
                            {
                                if (creature.IsTapped) continue;
                                Fx.Tap(creature);
                                tappedThisWay.Add(creature);
                            }

                            // "You may pay any amount of {E}. If you do, choose
                            // up to that many creatures tapped this way." The
                            // caster never benefits from paying more {E} than
                            // creatures they stun, so the cap is
                            // min(energy, tappedThisWay) and the count chosen
                            // equals the energy paid.
                            var maxStun = Math.Min(caster.EnergyCounters, tappedThisWay.Count);
                            if (maxStun <= 0) return;

                            var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                            if (agent == null) return;

                            var candidates = tappedThisWay.Cast<object>().ToList();
                            var req = new ChoiceRequest(
                                ChoiceKind.PickN,
                                $"Pay up to {maxStun} {{E}} to stun that many "
                                + "creatures tapped this way",
                                Min: 0,
                                Max: maxStun,
                                Candidates: candidates,
                                Intent: BotIntent.Removal,
                                Optional: true);

                            IReadOnlyList<object> picked;
                            try
                            {
                                picked = await agent
                                    .ChooseAsync(ctx.Game!, req, ctx.Ct)
                                    .ConfigureAwait(false);
                            }
                            catch
                            {
                                // Defensive: any agent failure → no stun (the
                                // tap already happened; the optional payment is
                                // simply declined).
                                return;
                            }

                            var chosenCreatures = picked
                                .OfType<Creature>()
                                .Where(c => tappedThisWay.Contains(c))
                                .Distinct()
                                .Take(maxStun)
                                .ToList();
                            if (chosenCreatures.Count == 0) return;

                            // CR 107.16 — pay {E} equal to the number of
                            // creatures chosen. PayEnergy is atomic; guard it
                            // even though maxStun already bounds the count.
                            if (!caster.PayEnergy(chosenCreatures.Count)) return;

                            // CR 122.1g — put one stun counter on each chosen
                            // creature. The untap-step replacement in
                            // TurnDriver consumes one counter instead of
                            // untapping.
                            foreach (var creature in chosenCreatures)
                            {
                                creature.Counters.Add(CounterType.Stun, 1);
                            }
                        }),
                };
            });
    }
}
