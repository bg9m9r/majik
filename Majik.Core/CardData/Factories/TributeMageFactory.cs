using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tribute Mage (Modern Horizons, {1}{U}).
///
/// Creature — Human Wizard 2/2. Oracle text:
///   "When Tribute Mage enters, you may search your library for an
///    artifact card with mana value 2, reveal it, put it into your
///    hand, then shuffle."
///
/// ## Implemented (v1)
/// - 2/2 Human Wizard with mana cost {1}{U}, owner/controller wired.
/// - <b>ETB tutor (CR 701.19a / 603.1)</b>: When Tribute Mage enters,
///   the controller's library is searched for an artifact card with
///   <see cref="ValueObjects.ManaCost.TotalValue"/> == 2. The picker
///   prompts the controller's registered <see cref="IPlayerAgent"/>
///   via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> and falls
///   back to the first eligible card when no agent is registered
///   (same posture as Drift of Phantasms / Stoneforge Mystic). On a
///   pick the card moves Library → Hand and the library is shuffled
///   (CR 701.20a) — the shuffle fires even on decline because a
///   "search" still happened. The single-arg factory attaches the
///   trigger to the card but does NOT register it with a
///   TriggerManager — mirrors <see cref="TrinketMageFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the picked card moves Library → Hand without
///   publishing a <see cref="CardRevealedEvent"/> — same gap as every
///   other tutor factory (Drift of Phantasms / Trinket Mage / Stoneforge
///   Mystic / Mystical Tutor).
/// - <b>"You may" decline branch</b>: with no agent registered the
///   deterministic picker auto-takes the first eligible artifact;
///   declining is only reachable through the agent path. The library
///   is still shuffled in both branches (CR 701.20a).
/// </summary>
[CardName("Tribute Mage")]
public static class TributeMageFactory
{
    public const string CardName = "Tribute Mage";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int TargetManaValue = 2;

    /// <summary>
    /// Construct Tribute Mage with no live TriggerManager wiring (the
    /// shape / dispatcher path). The ETB trigger is attached but not
    /// registered — suitable for unit / shape tests that execute the
    /// effect body directly.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Tribute Mage with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is
    /// registered so a <see cref="CardMovedEvent"/> to the battlefield
    /// places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1 / CR 701.19a.
        //   "When Tribute Mage enters, you may search your library for an
        //    artifact card with mana value 2, reveal it, put it into your
        //    hand, then shuffle."
        // Agent-driven pick via ChooseLibraryPickAsync with a deterministic
        // first-match fallback (mirrors Drift of Phantasms / Stoneforge
        // Mystic). The "may" surface is satisfied by the agent's nullable
        // return — null = decline. Reveal-event emission is the only
        // outstanding gap (see class xmldoc).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor an artifact card with mana value {TargetManaValue} to hand",
            () =>
            {
                var candidates = owner.Zones.Library.GetCards()
                    .OfType<Card>()
                    .Where(c => c.HasType(CardType.Artifact)
                                && c.ManaCostValue.TotalValue == TargetManaValue)
                    .Cast<ICard>()
                    .ToList();

                if (candidates.Count == 0)
                {
                    // CR 701.19a — empty candidate set is a clean no-op;
                    // CR 701.20a still requires a shuffle since the
                    // "search" happened.
                    LibraryShuffle.ShuffleLibrary(owner, "tribute-mage");
                    return;
                }

                var agent = AgentRegistry.Get(owner);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates,
                        $"artifact card with mana value {TargetManaValue}")
                        .GetAwaiter().GetResult()
                    : candidates[0];

                if (pick == null)
                {
                    // CR 701.19a — caster declined; CR 701.20a still
                    // requires a shuffle.
                    LibraryShuffle.ShuffleLibrary(owner, "tribute-mage");
                    return;
                }

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                LibraryShuffle.ShuffleLibrary(owner, "tribute-mage");
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
