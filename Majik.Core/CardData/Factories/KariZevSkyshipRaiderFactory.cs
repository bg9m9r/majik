using System;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kari Zev, Skyship Raider (Aether Revolt, {1}{R}).
///
/// Legendary Creature — Human Pirate, 1/3. Oracle text (verified against
/// Scryfall 2026-06-01):
///   "First strike, menace
///    Whenever Kari Zev attacks, create Ragavan, a legendary 2/1 red Monkey
///    creature token. Ragavan enters tapped and attacking. Exile that token at
///    end of combat."
///
/// ## Implemented (v1)
/// - 1/3 red Legendary Human Pirate at {1}{R}, owner / controller wired. Base
///   shape materialised from the embedded JSON definition
///   (<c>kari-zev-skyship-raider.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (same posture as
///   <see cref="LegionWarbossFactory"/>).
/// - <b>First strike (CR 702.7) + menace (CR 702.111)</b> — attached as
///   <see cref="KeywordAbility"/> markers consumed by CombatValidator /
///   CombatAbilities (same posture as <see cref="SireOfSevenDeathsFactory"/>;
///   these evergreen combat keywords are not expressible in the JSON
///   AbilityDefinition schema).
/// - <b>"Whenever Kari Zev attacks, create Ragavan … tapped and attacking"
///   (CR 508.3g)</b> — an <see cref="Triggers.OnAttackSelf"/>
///   <see cref="TriggeredAbility"/> that, on resolution, creates one legendary
///   2/1 red Monkey token named "Ragavan" via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111.4) and splices it
///   into the in-progress combat as a token that is already tapped and
///   attacking the same defender as Kari Zev, via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3 — enters
///   tapped; CR 508.4 — attacking the same player / planeswalker). This is the
///   same token-rider shape as <see cref="HanweirGarrisonFactory"/>, with a
///   single legendary Monkey instead of two Humans. Because the token is "put
///   onto the battlefield attacking" rather than "declared", it does NOT
///   re-trigger Kari Zev's own attack trigger (CR 508.3g).
/// - <b>"Exile that token at end of combat"</b> — a delayed effect (CR 603.7e)
///   modelled as a one-shot <see cref="PhaseStateType.EndOfCombat"/>
///   <see cref="StepStartedEvent"/> subscription (same EOT-subscription posture
///   as <see cref="AvatarRokuFactory"/>'s "until end of combat" rider and
///   <see cref="RagavanNimblePilfererFactory"/>'s Cleanup grant-clear). On the
///   controller's end-of-combat step the specific token created by this attack
///   is moved to exile (CR 111.8 — a token that leaves the battlefield ceases
///   to exist as a state-based action shortly after), then the handler
///   unsubscribes itself.
///
/// ## No-combat / no-bus fallback
/// Same posture as <see cref="HanweirGarrisonFactory"/>: when
/// <paramref name="combat"/> is null (shape / dispatcher tests) the token still
/// enters the battlefield, just untapped and not attacking. When
/// <paramref name="eventBus"/> is null the end-of-combat exile is not scheduled
/// (tests that need the exile wire a bus).
/// </summary>
[CardName("Kari Zev, Skyship Raider")]
public static class KariZevSkyshipRaiderFactory
{
    public const string CardName = "Kari Zev, Skyship Raider";
    public const string Slug = "kari-zev-skyship-raider";

    /// <summary>Ragavan token — legendary 2/1 red Monkey.</summary>
    public const string TokenName = "Ragavan";
    public const int TokenPower = 2;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Kari Zev with no live runtime wiring. The first-strike /
    /// menace keyword markers and the attack trigger are attached to the card
    /// shape; the token rider creates a plain battlefield token (no combat
    /// splice, no end-of-combat exile). Suitable for dispatcher / shape tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, combat: null, eventBus: null);

    /// <summary>
    /// Construct Kari Zev with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger is registered
    /// so a <see cref="CreatureAttacksEvent"/> for Kari Zev lands it on the
    /// stack automatically.</param>
    /// <param name="combat">When supplied, the Ragavan token is spliced into
    /// the in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    /// <param name="eventBus">When supplied, the "exile that token at end of
    /// combat" delayed effect is scheduled as a one-shot end-of-combat
    /// subscription.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Legendary supertype, Human + Pirate subtypes, {1}{R}, 1/3). The
        // keyword markers and the attack trigger are layered on below — neither
        // is expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 — First strike. CR 702.111 — Menace. Marker abilities
        // consumed by CombatValidator / CombatAbilities (same posture as
        // Sire of Seven Deaths' combat-keyword markers).
        card.AddAbility(new KeywordAbility("First Strike", card, owner));
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // CR 508.3g — "Whenever Kari Zev attacks, create Ragavan, a legendary
        // 2/1 red Monkey creature token. Ragavan enters tapped and attacking.
        // Exile that token at end of combat."
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create Ragavan (legendary 2/1 red Monkey) tapped & attacking, exile EOC",
            () => ResolveRagavanRider(card, owner, combat, eventBus));

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }

    /// <summary>
    /// CR 508.3g — create the Ragavan token, splice it into the in-progress
    /// combat tapped and attacking the same defender as Kari Zev, and schedule
    /// its end-of-combat exile. When no combat is live the token enters the
    /// battlefield untapped (the "tapped and attacking" fidelity requires a
    /// combat to splice into).
    /// </summary>
    private static void ResolveRagavanRider(
        Creature source, Player owner, CombatManager? combat, IEventBus? eventBus)
    {
        var controller = source.Controller ?? owner;

        // CR 111.4 — one 2/1 red Monkey creature token named "Ragavan".
        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Monkey },
            Keywords: null,
            Colors: new[] { ManaColor.Red });

        var token = TokenFactory.CreateOnBattlefield(spec, controller);

        // "a legendary … token" — stamp the Legendary supertype (CR 205.4 /
        // CR 704.5j legend rule applies to the token). TokenSpec carries no
        // supertype field, so set it directly on the minted token.
        token.AddSupertype(CardSupertype.Legendary);

        // CR 508.3g — splice the token into the in-progress combat tapped and
        // attacking the same defender as Kari Zev. When no combat is live the
        // token stays on the battlefield untapped (no-combat fallback).
        combat?.AddTappedAndAttackingToken(token);

        // "Exile that token at end of combat." CR 603.7e — a delayed effect
        // that fires once on the controller's end-of-combat step, then
        // unsubscribes (same one-shot EOT-subscription posture as Avatar
        // Roku's "until end of combat" mana expiry). When no bus is wired the
        // exile is skipped (tests that need it pass a bus).
        if (eventBus == null) return;

        Action<StepStartedEvent>? handler = null;
        handler = e =>
        {
            if (e.StepType != PhaseStateType.EndOfCombat) return;

            // Exile the specific token created by this attack. Guard against a
            // token that already left the battlefield (CR 111.8 — a token that
            // is no longer on the battlefield ceases to exist via SBAs).
            if (token.Zone == ZoneType.Battlefield)
            {
                var tokenController = token.Controller ?? controller;
                tokenController.Zones.Battlefield.RemoveCard(token);
                tokenController.Zones.Exile.AddCard(token);
                token.SetZone(ZoneType.Exile);
            }

            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
