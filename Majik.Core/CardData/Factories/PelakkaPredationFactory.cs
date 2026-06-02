using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Pelakka Predation // Pelakka Caverns (Zendikar Rising, {2}{B}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Target opponent reveals their hand. You choose a card from it with mana
///    value 3 or greater. That player discards that card."
///
/// Back face — <see cref="PelakkaCavernsFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {B}.").
///
/// ## Shape
///
/// A <see cref="DespiseFactory"/>-style "target opponent" reveal-and-choose
/// discard, gated by the <see cref="InquisitionOfKozilekFactory"/>-style mana
/// value filter — except the gate is mana value <b>3 or GREATER</b> (not 3 or
/// less) and there is <b>no card-type restriction</b> (any card qualifies,
/// including lands with mana value ≥ 3 such as MDFC land backs, though in
/// practice lands are mana value 0). No life cost.
///
/// On resolution:
///   1. <b>Reveal</b> (CR 701.16) — the target opponent's hand becomes public
///      via <see cref="RevealHelper.RevealHand"/>, one
///      <see cref="CardRevealedEvent"/> per card.
///   2. <b>Caster picks a card with mana value ≥ 3</b> (CR 700.2) — the
///      candidate list is pre-filtered by mana value; the caster's
///      <see cref="IPlayerAgent.ChooseFromHandAsync"/> drives the pick
///      (intent <see cref="BotIntent.HandHate"/>), with a deterministic
///      first-legal fallback (parity with Despise / Inquisition of Kozilek).
///   3. <b>Discard</b> (CR 701.16 — "discards that card") — Hand → Graveyard.
///      No-op when the hand has no mana-value-≥-3 card.
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="MalakirRebirthFactory"/> / <see cref="MalakirMireFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>pelakka-predation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time spell behaviour are attached in code (the JSON
/// schema models neither MDFC faces nor the reveal/choose/discard effect).
/// </summary>
[CardName("Pelakka Predation")]
public static class PelakkaPredationFactory
{
    public const string CardName = "Pelakka Predation";
    public const string BackName = "Pelakka Caverns";

    /// <summary>Printed mana-value floor on the discard pick (CR 700.2).</summary>
    public const int ManaValueFloor = 3;

    /// <summary>
    /// Construct Pelakka Predation as a Sorcery (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached (front = "Pelakka
    /// Predation", back = the castable "Pelakka Caverns" land). The
    /// resolve-time <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("pelakka-predation");
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker WITH a castable
        // back-face land descriptor. The back face is the LAND back face
        // played with no stack; MdfcCastFlow offers the controller a face
        // choice at cast time and materializes a fresh back-face land instance
        // when chosen. No transform happens. (Mirrors Malakir Rebirth.)
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, _) =>
                PelakkaCavernsFactory.Create(landOwner));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the reveal → pick (mana value ≥ 3) → discard
    /// <see cref="SpellDefinition"/>. Single 1..1 "target opponent" request.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="agent">Optional player-agent used for the pick. When null,
    /// the pick falls back deterministically to the first legal (mana value ≥
    /// 3) card in the revealed hand.</param>
    /// <param name="eventBus">Optional event bus for publishing
    /// <see cref="CardRevealedEvent"/> per card in the revealed hand. No-op
    /// when null.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IPlayerAgent? agent,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target opponent", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Pelakka Predation: reveal → caster picks card mv≥3 → discard", () =>
                    {
                        // CR 608.2b — single illegal target → spell does
                        // nothing (the cast-flow's own pass catches most of
                        // these; guard defensively).
                        if (raw is not Player victim) return;

                        // CR 701.16 — "Target opponent reveals their hand."
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 700.2 — "a card from it with mana value 3 or
                        // greater." No card-type restriction.
                        var legal = victim.Zones.Hand.GetCards()
                            .Where(c => ManaCost.Parse(c.ManaCost).TotalValue >= ManaValueFloor)
                            .ToList();

                        // Agent pick (intent HandHate) with deterministic
                        // first-legal fallback — same posture as Despise /
                        // Inquisition of Kozilek.
                        ICard? pick = null;
                        if (legal.Count > 0)
                        {
                            if (agent != null)
                            {
                                pick = agent
                                    .ChooseFromHandAsync(victim, legal, BotIntent.HandHate)
                                    .GetAwaiter().GetResult();
                                if (pick == null
                                    || pick.Zone != ZoneType.Hand
                                    || ManaCost.Parse(pick.ManaCost).TotalValue < ManaValueFloor
                                    || !ReferenceEquals(pick.Owner, victim))
                                {
                                    pick = legal[0];
                                }
                            }
                            else
                            {
                                pick = legal[0];
                            }
                        }

                        // CR 701.16 — "That player discards that card." No-op
                        // when the hand has no mana-value-≥-3 card.
                        if (pick != null)
                        {
                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                    }),
                };
            });
    }
}
