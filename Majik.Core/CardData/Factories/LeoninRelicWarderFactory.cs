using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leonin Relic-Warder (New Phyrexia, {W}{W}).
///
/// Creature — Cat Cleric 2/2. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "When this creature enters, you may exile target artifact or
///    enchantment.
///    When this creature leaves the battlefield, return the exiled card
///    to the battlefield under its owner's control."
///
/// This is the creature-bodied member of the "Oblivion Ring" exile-and-
/// return family (see <see cref="BanishingLightFactory"/> /
/// <see cref="JourneyToNowhereFactory"/>). Two printed differences from
/// Journey to Nowhere's "exile target creature" pair:
/// <list type="bullet">
///   <item>The exile rider lives on a <b>creature</b>, so the LTB "leaves
///     the battlefield" trigger fires on death / bounce / blink just like
///     the enchantment variants.</item>
///   <item>The ETB target is "target artifact or enchantment" — ANY
///     controller, not restricted to "an opponent controls" (Rule 115.4 —
///     self-targeting is legal absent a "you don't control" clause). The
///     "artifact or enchantment" filter mirrors
///     <see cref="ReclamationSageFactory"/>'s target request.</item>
/// </list>
///
/// ## Implemented (v1)
/// - <b>Creature {W}{W} 2/2 Cat Cleric</b>. Base shape materialised from
///   the embedded JSON definition (<c>leonin-relic-warder.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BorrowedTimeFactory"/> (the JSON schema doesn't express the
///   exile-and-return triggers, so they are layered on here).
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21): single 1..1
///   "target artifact or enchantment". On resolve, after a CR 608.2b
///   legality re-check (still an artifact or enchantment on the
///   battlefield), the target is exiled and captured (paired with its
///   owner) in a per-instance closure shared with the LTB ability.
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   Leonin Relic-Warder moves OUT of the battlefield (any destination —
///   covers dies + bounce + blink, matching "leaves the battlefield"
///   wording). On resolve the still-exiled captured card returns to the
///   battlefield under its owner's control (CR 110.2 — Controller := Owner
///   on the way back).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" decline</b>: the printed ETB is optional ("you may
///   exile"). At v1 the ability exiles whenever a legal target was chosen,
///   same posture as <see cref="ReclamationSageFactory"/> / Eternal Witness.
///   A no-chosen-target resolution is a clean no-op (the closure stays
///   empty, so the LTB also no-ops) — that path covers a declined "may"
///   and the no-legal-target case identically.
/// - <b>Flicker race</b>: identical posture to Journey to Nowhere. A
///   blinked Leonin Relic-Warder re-enters as a new object (CR 400.7) with
///   an empty closure — matching real MTG.
/// </summary>
[CardName("Leonin Relic-Warder")]
public static class LeoninRelicWarderFactory
{
    public const string CardName = "Leonin Relic-Warder";
    public const string Slug = "leonin-relic-warder";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Leonin Relic-Warder with no runtime services. Both
    /// triggered abilities are attached to the card shape; neither is
    /// registered with a <see cref="TriggerManager"/>. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Leonin Relic-Warder with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both ETB and
    /// LTB abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (CardDefinitionFactory.Build(Definition, owner) is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature.");
        }
        card.SetOwner(owner);
        card.SetController(owner);

        WireExileArtifactOrEnchantmentTriggers(card, owner, triggers);
        return card;
    }

    /// <summary>
    /// Wiring for the "exile target artifact or enchantment until this
    /// leaves" ETB / LTB pair. Shares the per-source closure shape with
    /// <see cref="JourneyToNowhereFactory"/> but with an
    /// artifact-or-enchantment target (and no "an opponent controls" gate —
    /// the printed text targets any artifact or enchantment).
    /// </summary>
    private static void WireExileArtifactOrEnchantmentTriggers(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21.
        //   "When this creature enters, you may exile target artifact or
        //    enchantment."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{card.Name}: exile target artifact or enchantment (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                // "You may" / no legal target → clean no-op (closure stays
                // empty so the LTB also no-ops).
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks. The target must
                // still be an artifact or enchantment on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!(target.HasType(CardType.Artifact)
                      || target.HasType(CardType.Enchantment))) return;

                // CR 701.21 — exile (Battlefield → Exile). Routed through the
                // target's owner's zones — same posture as Journey to Nowhere
                // / Banishing Light.
                var targetOwner = target.Owner;
                if (targetOwner != null)
                {
                    targetOwner.Zones.Battlefield.RemoveCard(target);
                    targetOwner.Zones.Exile.AddCard(target);
                }
                target.SetZone(ZoneType.Exile);

                exiled = target;
                exiledOwner = targetOwner;
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When this creature leaves the battlefield, return the exiled
        //    card to the battlefield under its owner's control."
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{card.Name}: return the exiled card to the battlefield under its owner's control",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile, skip.
                if (exiled.Zone != ZoneType.Exile) return;

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
                // CR 110.2 — "under its owner's control" maps Controller :=
                // Owner on the way back.
                if (exiled is Card returned) returned.ChangeController(exiledOwner);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed
            // on the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);
    }
}
