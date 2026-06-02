using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ovalchase Daredevil (Kaladesh, {3}{B}).
///
/// Creature — Human Pilot 4/2. Oracle text (verified against Scryfall):
///   "Whenever an artifact you control enters, you may return this card from
///    your graveyard to your hand."
///
/// The base shape (name, Creature type, Human + Pilot subtypes, {3}{B}, 4/2)
/// is materialised from the embedded JSON definition
/// (<c>ovalchase-daredevil.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="ScrapheapScroungerFactory"/>). The artifact-enters recursion
/// trigger is layered on here — the declarative JSON <c>AbilityDefinition</c>
/// schema does not yet express a graveyard-resident "return this card to your
/// hand" trigger (same documented gap as <see cref="SqueeGoblinNabobFactory"/>
/// and <see cref="BloodghastFactory"/>). All the underlying engine primitives
/// already exist; this factory composes them, mirroring those two analogues.
///
/// ## Implemented (v1)
/// - 4/2 Human Pilot at printed cost {3}{B}; owner / controller stamped.
/// - <b>Artifact-enters recursion trigger (CR 603.1 / CR 603.3 / CR 603.6e —
///   a graveyard-resident trigger)</b>: fires on
///   <see cref="Triggers.OnArtifactYouControlEnters"/> (a
///   <see cref="Majik.Core.Events.CardMovedEvent"/> → Battlefield where the
///   entering card has the Artifact type and its controller is the Daredevil's
///   owner) and is active <b>only while the Daredevil is in its owner's
///   Graveyard</b> (<c>activeZones = {Graveyard}</c>). On resolution the
///   resident zone is re-checked (CR 603.6d) and, if the Daredevil is still in
///   the graveyard, it moves Graveyard → Hand. When a <see cref="ZoneService"/>
///   is wired the move goes through <see cref="ZoneService.MoveCard"/> so
///   zone-change events fire; otherwise a raw zone move is performed.
/// - <b>"You may"</b>: when an <see cref="IPlayerAgent"/> is supplied the
///   return consults
///   <see cref="IPlayerAgent.ChooseYesNoAsync(string,BotIntent,System.Threading.CancellationToken)"/>
///   (<see cref="BotIntent.Reanimate"/> | <see cref="BotIntent.CardAdvantage"/>
///   — a pure upside, so the deterministic bot auto-accepts); a false answer
///   declines and leaves the Daredevil in the graveyard. The no-agent path
///   preserves the legacy auto-accept posture (same as the Squee /
///   Bloodghast graveyard returns).
///
/// ## Notes
/// - The trigger fires once per artifact that enters under the owner's
///   control (one trigger object per event), matching the printed card — it
///   does not matter whether the entering artifact is also a creature, a
///   Vehicle, etc.; any artifact ETB under your control fires it (CR 603.3).
/// - The "enters" (reminder-free) modern wording means a permanent entering
///   the battlefield (CR 603.6e), which is what
///   <see cref="Triggers.OnArtifactYouControlEnters"/> matches.
///
/// ## Deferred (v1 gaps)
/// - Async agent path: the return prompt is bridged sync-over-async at effect
///   execution time (the trigger effect is a synchronous <see cref="Effect"/>),
///   matching the Squee / Bloodghast graveyard returns. A fully async
///   resolution threading a live <see cref="ResolutionContext"/> through
///   <see cref="ZoneService.MoveCardAsync"/> is deferred until the trigger
///   resolution path is uniformly async.
/// </summary>
[CardName("Ovalchase Daredevil")]
public static class OvalchaseDaredevilFactory
{
    public const string CardName = "Ovalchase Daredevil";
    public const string Slug = "ovalchase-daredevil";

    /// <summary>
    /// Construct Ovalchase Daredevil with no runtime service wiring. The card
    /// has the correct shape (name, type, subtypes, P/T, mana cost) and the
    /// artifact-enters recursion trigger is attached for structural
    /// inspection, but the trigger is not registered with a
    /// <see cref="TriggerManager"/> (fire it manually in tests) and the
    /// "you may" return auto-accepts. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, agent: null);

    /// <summary>
    /// Construct Ovalchase Daredevil with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service used by the trigger to move the
    /// Daredevil from graveyard to hand so zone-change events fire. May be
    /// null — a raw zone move is performed instead.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident trigger
    /// registration (CR 603.6d). May be null — the trigger is attached to the
    /// card for shape but not registered with the bus.</param>
    /// <param name="agent">Optional agent consulted for the "you may" return
    /// (<see cref="BotIntent.Reanimate"/> | <see cref="BotIntent.CardAdvantage"/>).
    /// Null preserves the legacy auto-accept posture.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Pilot subtypes, {3}{B}, 4/2).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Artifact-enters recursion trigger — CR 603.1, CR 603.3, CR 603.6e.
        //   "Whenever an artifact you control enters, you may return this card
        //    from your graveyard to your hand."
        // Active only while the Daredevil is in its owner's Graveyard
        // (activeZones = {Graveyard}). Triggers.OnArtifactYouControlEnters
        // filters the CardMovedEvent on (→ Battlefield, has Artifact type,
        // controller == owner) so it fires only for artifacts entering under
        // your control.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return from graveyard to hand (artifact-enters trigger)",
            async ctx =>
            {
                // CR 603.6d — re-check zone at resolution. If the Daredevil
                // has left the graveyard since the trigger was put on the
                // stack, do nothing.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                // "You may" — consult the agent when wired; else auto-accept
                // (legacy posture, same as the Squee / Bloodghast returns).
                if (agent != null)
                {
                    var yes = await agent.ChooseYesNoAsync(
                        "Return Ovalchase Daredevil from your graveyard to your hand?",
                        BotIntent.Reanimate | BotIntent.CardAdvantage).ConfigureAwait(false);
                    if (!yes) return;
                }

                if (zoneService != null)
                {
                    // ZoneService.MoveCard fires zone-change events (CR 603.6a)
                    // so portal/log subscribers see the recursion.
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Hand, owner);
                }
                else
                {
                    // Raw zone move — no zone-change event published.
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });

        var artifactTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnArtifactYouControlEnters(owner),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(artifactTrigger);
        triggers?.RegisterTriggeredAbility(artifactTrigger);

        return card;
    }
}
