using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Forbidden Orchard (Guildpact). Oracle text
/// (verified against Scryfall):
///   "{T}: Add one mana of any color.
///    Whenever you tap this land for mana, target opponent creates a 1/1
///    colorless Spirit creature token."
///
/// <para>
/// The Land shell (identity / owner / controller) is declared declaratively
/// in <c>Majik.Core/CardData/Cards/forbidden-orchard.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/> — the same posture
/// as <see cref="ManaConfluenceFactory"/>. The "any color" mana abilities
/// and the tap-for-mana trigger are attached on top in C# because the
/// data-only <see cref="ManaAbilityDefinition"/> schema only carries a
/// <c>Produces</c> string (it cannot express the five-colour fan-out) and
/// there is no declarative shape for an event-driven targeted trigger. The
/// JSON therefore declares no abilities; this factory adds them.
/// </para>
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype, colourless) via JSON.
/// - <b>{T}: Add one mana of any color.</b> — modelled as five
///   <see cref="ManaAbility"/> instances, one per WUBRG (same any-colour
///   fan-out as <see cref="ManaConfluenceFactory"/> / Aether Hub's coloured
///   modes). There is NO <c>{C}</c> mode and — unlike Mana Confluence /
///   City of Brass — NO pay-life / pain additional cost. The mana picker
///   chooses whichever colour is needed when paying spell costs.
/// - <b>"Whenever you tap this land for mana, target opponent creates a 1/1
///   colorless Spirit creature token."</b> — a single
///   <see cref="TriggeredAbility"/> subscribing to
///   <see cref="ManaAbilityActivatedEvent"/> (CR 605 — mana abilities don't
///   use the stack, so this event is the only observable tap-for-mana
///   signal; same hook as <see cref="ManabarbsFactory"/>). The condition
///   gates on the source being THIS land specifically ("you tap this land",
///   not Manabarbs's "a player taps a land"). The trigger carries a single
///   "target opponent" <see cref="TargetRequest"/> (CR 603.3d — a triggered
///   ability that requires a target). On resolve the targeted
///   <see cref="Player"/> — not Forbidden Orchard's controller — creates one
///   1/1 colourless Spirit creature token (CR 111 / 111.4) via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>.
///   The opponent is the token's owner and controller (CR 111.2).
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches both the mana abilities and the
///   trigger to the card shape without a live <see cref="TriggerManager"/> —
///   suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> additionally
///   registers the trigger so a real tap-for-mana
///   <see cref="ManaAbilityActivatedEvent"/> places it on the stack, and
///   threads the optional <see cref="ZoneService"/> into token ETB so
///   <see cref="Majik.Core.Events.CardMovedEvent"/> fires (mirrors Doomed
///   Traveler / Manabarbs wiring posture).
///
/// ## Deferred (v1 gaps)
/// - <b>Target-opponent selection in multiplayer</b>: "target opponent"
///   limits the trigger to exactly one chosen opponent. In a 2-player game
///   that opponent is unambiguous; the controller's agent picks in
///   multiplayer (same posture as <see cref="ArchonOfCrueltyFactory"/>). The
///   target is set via <see cref="TriggeredAbility.SetChosenTargets"/> before
///   resolution. No target chosen → the resolution body is a no-op.
/// </summary>
[CardName("Forbidden Orchard")]
public static class ForbiddenOrchardFactory
{
    public const string CardName = "Forbidden Orchard";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("forbidden-orchard");

    private const int TokenPower = 1;
    private const int TokenToughness = 1;

    /// <summary>
    /// Construct Forbidden Orchard with the mana abilities + tap-for-mana
    /// trigger attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Forbidden Orchard with optional <see cref="TriggerManager"/>
    /// and <see cref="ZoneService"/> wiring. When <paramref name="triggers"/>
    /// is supplied the tap-for-mana trigger is registered so a real
    /// <see cref="ManaAbilityActivatedEvent"/> for this land surfaces it as
    /// pending; when <paramref name="zoneService"/> is supplied the Spirit
    /// token's ETB publishes a <see cref="Majik.Core.Events.CardMovedEvent"/>.
    /// </summary>
    public static Land Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add one mana of any color.
        //   Five ManaAbility instances (one per WUBRG) — same any-colour
        //   fan-out as Mana Confluence / Aether Hub. No {C} mode, no
        //   pay-life cost: {T} alone (CR 605.1a — a mana ability).
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color)));
        }

        // "Whenever you tap this land for mana, target opponent creates a
        //  1/1 colorless Spirit creature token." (CR 603.2 — triggered
        //  ability over an event; CR 605 — mana abilities surface the
        //  ManaAbilityActivatedEvent.) The condition gates on the source
        //  being THIS land specifically (not "a land" — that's Manabarbs).
        //
        // Closure-captured null trigger ref → resolved via getTrigger()
        // inside the effect so SetChosenTargets is read at resolution time
        // (same pattern as Archon of Cruelty).
        TriggeredAbility? trigger = null;

        var condition = new EventTriggerCondition<ManaAbilityActivatedEvent>(
            (e, _) => ReferenceEquals(e.Source, land));

        var tokenEffect = new Effect(
            $"{CardName}: target opponent creates a 1/1 colorless Spirit creature token",
            () =>
            {
                var opponent = ResolveTargetOpponent(trigger);
                if (opponent is null) return; // no target chosen → no-op

                // CR 111 / 111.4 — the targeted OPPONENT creates the token;
                // it enters under that opponent's control (CR 111.2). An
                // empty colour list stamps the token colourless.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Spirit",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Spirit },
                    Keywords: Array.Empty<string>(),
                    Colors: Array.Empty<ManaColor>());

                TokenFactory.CreateOnBattlefield(spec, opponent, zoneService);
            });

        var targetRequest = new TargetRequest(
            Description: "target opponent",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());

        trigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { tokenEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return land;
    }

    /// <summary>Resolve the chosen "target opponent" from the trigger's
    /// <see cref="TriggeredAbility.ChosenTargets"/>. Returns <c>null</c> when
    /// no target was chosen (resolution is then a no-op). Mirrors
    /// <see cref="ArchonOfCrueltyFactory"/>.</summary>
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
