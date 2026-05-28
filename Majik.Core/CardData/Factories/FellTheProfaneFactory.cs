using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Fell the Profane // Fell Mire.
///
/// Instant. Oracle text (front):
///   "Destroy target creature or planeswalker."
///
/// Back face — <see cref="FellMireFactory"/> (Land — "As this land enters,
/// you may pay 3 life. If you don't, it enters tapped." / "{T}: Add {B}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
/// Two-factory dispatch: casting the front face resolves "Fell the Profane"
/// → this factory → an <see cref="Instant"/> with the destroy effect.
/// Playing the back face resolves "Fell Mire" → <see cref="FellMireFactory"/>
/// → a painland-style <see cref="Land"/>.
/// Both faces carry an <see cref="MdfcState"/> tracker so callers can see
/// the printed back/front name without holding two object handles.
///
/// ## Implemented (v1)
/// - Instant identity at {2}{B}{B}, black (mono-B from the printed pips),
///   mana value 4. Owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Fell the Profane",
///   back = "Fell Mire"); starts on the front face.
/// - <b>Destroy target creature or planeswalker</b> — single 1..1
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>)
///   whose candidate gatherer enumerates every creature + planeswalker on
///   the battlefield across all players.
///   On resolution the targeted permanent is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7) iff it is
///   still a creature or planeswalker on the battlefield at resolution
///   (CR 608.2b — illegal target → no-op).
///   Indestructible (CR 702.12) cancels the destroy. Regeneration shields
///   (CR 701.15) are consumed — no "can't be regenerated" rider.
///
/// ## Design references
/// - MDFC factory pair: <see cref="SinkIntoStuporFactory"/> /
///   <see cref="SoporificSpringsFactory"/> (PR #969).
/// - Destroy creature-or-planeswalker target shape:
///   <see cref="BitterTriumphFactory"/> (PR #997, minus the additional cost).
/// </summary>
[CardName("Fell the Profane")]
public static class FellTheProfaneFactory
{
    public const string CardName = "Fell the Profane";
    public const string BackName = "Fell Mire";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>
    /// Construct the front face of Fell the Profane as an Instant with
    /// owner / controller wired and the <see cref="MdfcState"/> face tracker
    /// attached (starts on the front face).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Fell Mire) is observable from the front-face card
        // object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);
        return card;
    }

    /// <summary>
    /// Build the resolve-time "destroy target creature or planeswalker"
    /// <see cref="SpellDefinition"/>.
    ///
    /// CR 608.2b — illegal-target re-check at resolution: if the chosen
    /// target is no longer a creature or planeswalker on the battlefield
    /// the effect does nothing.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: all creatures + planeswalkers on the
                    // battlefield across every player.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
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
                        $"{CardName}: destroy target creature or planeswalker",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Planeswalker)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // cancels; regeneration shields (CR 701.15) consumed.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
