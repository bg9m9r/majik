using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shaman of the Pack (Magic Origins, {1}{B}{G}).
///
/// Creature — Elf Shaman 3/2. Oracle text (verified against Scryfall):
///   "When this creature enters, target opponent loses life equal to the
///    number of Elves you control."
///
/// A staple Elf-tribal finisher: with a wide Elf board the ETB drains a
/// huge chunk of an opponent's life. Same Elf-count predicate as
/// <see cref="ElvishArchdruidFactory"/>'s tribal mana ability; same
/// "target opponent loses life" effect shape (targeted ETB →
/// <see cref="Fx.LoseLife"/>) as the drain half of
/// <see cref="ArchonOfCrueltyFactory"/> / <see cref="KambalConsulOfAllocationFactory"/>.
///
/// The base shape (name, Creature, Elf + Shaman subtypes, {1}{B}{G}, 3/2)
/// is materialised from the embedded JSON definition
/// (<c>shaman-of-the-pack.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB trigger is layered
/// on here (the JSON ability schema doesn't yet express targeted
/// count-driven life loss) — same posture as
/// <see cref="StormscaleScionFactory"/>.
///
/// ## Implemented (v1)
/// - 3/2 Creature — Elf Shaman at {1}{B}{G}. Elf + Shaman subtypes so the
///   Shaman counts itself toward its own Elf-count drain.
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>: "When this
///   creature enters, …" keyed on <see cref="Triggers.OnEnterBattlefieldSelf"/>,
///   active on the battlefield. Same ETB-self condition shape as
///   <see cref="ArchonOfCrueltyFactory"/>.
/// - <b>Targeted "target opponent" (CR 115.1 / 608.2c)</b>: a 1..1
///   <see cref="TargetRequest"/> populated at resolution from
///   <see cref="TriggeredAbility.SetChosenTargets"/>. No target chosen
///   (shape-only / test fixtures) → the body no-ops cleanly (CR 608.2b —
///   an ability with no legal target on resolution does nothing).
/// - <b>Life loss = Elves you control (CR 119.3 / 109.5)</b>: the chosen
///   opponent loses life equal to the number of Elf permanents on the
///   controller's battlefield. Counted at resolution (CR 608.2h — the
///   amount is determined as the effect resolves). INCLUDES the Shaman
///   itself when it is on the battlefield — oracle reads "Elves you
///   control" with no "other" qualifier (same counting convention as
///   <see cref="ElvishArchdruidFactory"/>'s mana ability). "You control"
///   filters to the controller's battlefield only (CR 109.5 — opponents'
///   Elves don't count). Routed through <see cref="Fx.LoseLife"/> →
///   <see cref="Player.LoseLife"/> so <see cref="Player.LifeLostThisTurn"/>
///   ticks (spectacle / revolt / lifegain observers see the loss).
///
/// ## Deferred (v1 gaps)
/// - <b>Multi-opponent targeting</b>: "target opponent" limits the
///   trigger to exactly one chosen opponent. In multiplayer the
///   controller picks which opponent; v1 test fixtures target a single
///   opponent directly (same posture as <see cref="ArchonOfCrueltyFactory"/>).
/// - <b>Count snapshot vs. interaction during resolution</b>: the Elf
///   count is read once when the body runs (CR 608.2h). Nothing can
///   change the board mid-resolution in v1, so the snapshot is exact.
///
/// CR references: 603.1 / 603.6a (ETB trigger), 115.1 / 608.2c (target
/// opponent), 608.2h (count determined on resolution), 119.3 (life loss),
/// 109.5 ("you control" = controller's battlefield).
/// </summary>
[CardName("Shaman of the Pack")]
public static class ShamanOfThePackFactory
{
    public const string CardName = "Shaman of the Pack";
    public const string Slug = "shaman-of-the-pack";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Shaman of the Pack with the ETB trigger attached for
    /// shape inspection. The trigger is NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / shape
    /// tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Shaman of the Pack with optional
    /// <see cref="TriggerManager"/> wiring. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the card entering the battlefield lands the drain on
    /// the stack automatically.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied the ETB
    /// trigger is registered for live <see cref="Events.CardMovedEvent"/>
    /// dispatch.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf + Shaman subtypes, {1}{B}{G}, 3/2). The JSON carries no
        // abilities — the ETB drain is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB trigger — "When this creature enters, target opponent loses
        // life equal to the number of Elves you control."
        // CR 603.1 / CR 603.6a — fires on CardMovedEvent → Battlefield for
        // this card specifically.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var drainEffect = new Effect(
            $"{CardName}: target opponent loses life equal to Elves you control (CR 119.3 / 109.5)",
            () =>
            {
                var opponent = ResolveTargetOpponent(etbTrigger);
                if (opponent is null) return; // CR 608.2b — no target → no-op.

                var controller = card.Controller ?? owner;

                // CR 608.2h — count determined as the effect resolves.
                // CR 109.5 — "you control" = controller's battlefield only.
                // INCLUDES the Shaman itself (no "other" qualifier).
                var elfCount = controller.Zones.Battlefield.GetCards()
                    .Count(c => c.HasSubtype(CardSubtype.Elf));

                // CR 119.3 — life loss. Routes through Fx.LoseLife →
                // Player.LoseLife so LifeLostThisTurn ticks.
                Fx.LoseLife(opponent, elfCount);
            });

        var targetRequest = new TargetRequest(
            Description: "target opponent",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { drainEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    private static Player? ResolveTargetOpponent(TriggeredAbility? trigger)
    {
        if (trigger is null
            || trigger.ChosenTargets.Count == 0
            || trigger.ChosenTargets[0].Count == 0)
        {
            return null;
        }
        return trigger.ChosenTargets[0][0] as Player;
    }
}
