using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Genesis Chamber (Fifth Dawn, {2}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "Whenever a nontoken creature enters, if this artifact is untapped,
///    that creature's controller creates a 1/1 colorless Myr artifact
///    creature token."
///
/// The base shape (name, Artifact, {2}) is materialised from the embedded
/// JSON definition (<c>genesis-chamber.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed behaviour
/// (the symmetric nontoken-creature-ETB trigger with an intervening-if on
/// the artifact's tapped state) is layered on here, since the JSON
/// <c>AbilityDefinition</c> schema doesn't express either piece.
///
/// ## Implemented (v1)
/// - 0-mana-body Artifact at {2}, owner/controller wired.
/// - <b>Symmetric nontoken-creature ETB trigger (CR 603.6a)</b> — a
///   <see cref="CardMovedEvent"/> trigger firing whenever ANY creature
///   enters the battlefield (<c>ToZone == Battlefield</c> + the moved card
///   <see cref="Card.HasType"/> Creature) that is NOT a token
///   (<see cref="Permanent.IsToken"/> false — CR 111.1 distinguishes token
///   from nontoken permanents; the printed "nontoken" rider excludes the
///   Myr tokens this very artifact mints, so it does not chain). The
///   trigger is symmetric — it watches every player's creatures, not just
///   the controller's — so the entering creature's OWN controller
///   (<see cref="ICard.Controller"/>), read off the event, is the player
///   who creates the Myr (CR 109.4 — "that creature's controller").
/// - <b>Intervening-if "if this artifact is untapped" (CR 603.4)</b> —
///   threaded through <see cref="TriggeredAbility.InterveningIf"/>: the
///   condition is checked both at trigger time AND on resolution, so an
///   artifact that becomes tapped after the creature entered but before the
///   trigger resolves still does nothing. Gates on
///   <c>!card.IsTapped</c>.
/// - <b>1/1 colorless Myr artifact creature token (CR 111.4)</b> — minted
///   via <see cref="TokenFactory.CreateOnBattlefield"/> with the
///   <see cref="CardSubtype.Myr"/> subtype + an empty colour set
///   (colourless), then additively stamped with
///   <see cref="CardType.Artifact"/> (CR 111.1 — Myr tokens are artifact
///   creatures; the Token shell is Creature-only, mirroring Whirler Rogue's
///   Thopter / Plague Myr's Artifact-Creature shape).
///
/// ## Notes
/// - <b>Symmetric, every-player trigger</b>: Genesis Chamber benefits all
///   players whose nontoken creatures enter. The token goes to the entering
///   creature's controller, NOT necessarily Genesis Chamber's controller.
/// - <b>No chaining</b>: the minted Myr is a token, so the "nontoken"
///   filter stops it from re-triggering the chamber (CR 111.1).
/// </summary>
[CardName("Genesis Chamber")]
public static class GenesisChamberFactory
{
    public const string CardName = "Genesis Chamber";
    public const string Slug = "genesis-chamber";
    public const string PrintedManaCost = "{2}";
    public const string MyrTokenName = "Myr";
    public const int MyrPower = 1;
    public const int MyrToughness = 1;

    /// <summary>
    /// Construct Genesis Chamber with no live runtime services. The
    /// nontoken-creature ETB trigger is attached to the card shape but not
    /// registered with a <see cref="TriggerManager"/>, and the minted Myr
    /// token's ETB does not publish <see cref="CardMovedEvent"/>. Suitable
    /// for dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Genesis Chamber with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the nontoken-creature ETB
    /// trigger is registered with so it surfaces as pending. May be null.</param>
    /// <param name="zoneService">When supplied the minted Myr token's ETB
    /// publishes <see cref="CardMovedEvent"/> so downstream subscribers see
    /// it enter (CR 603.6a / CR 111.6).</param>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact, {2}).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Symmetric nontoken-creature ETB trigger — CR 603.6a.
        //   "Whenever a nontoken creature enters, if this artifact is
        //    untapped, that creature's controller creates a 1/1 colorless
        //    Myr artifact creature token."
        // The "nontoken" rider (CR 111.1) excludes the Myr tokens this
        // artifact mints — so it never chains off its own output. The
        // trigger is symmetric (every player's creatures), and the entering
        // creature's controller is the token's controller (CR 109.4).
        // ----------------------------------------------------------------
        // Capture the entering creature's controller (CR 109.4) so it is the
        // Myr's controller on resolution. The controller is resolved at
        // trigger time (when the predicate matches) and stashed for the
        // resolve effect to read.
        Player? enteringController = null;

        var entersCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            // CR 111.1 — exclude token permanents ("nontoken creature").
            if (e.Card is Permanent { IsToken: true }) return false;
            enteringController = e.Card.Controller;
            return true;
        });

        var mintEffect = new Effect(
            $"{CardName}: that creature's controller creates a 1/1 colourless Myr artifact creature token",
            () =>
            {
                // CR 109.4 — "that creature's controller". Fall back to this
                // artifact's controller only if the entering controller was
                // not captured (defensive; should always be set when fired
                // through the bus).
                var beneficiary = enteringController ?? card.Controller ?? owner;
                CreateMyrToken(beneficiary, zoneService);
            });

        var entersTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: entersCondition,
            effects: new IEffect[] { mintEffect },
            // CR 603.4 — intervening-if "if this artifact is untapped",
            // re-checked at trigger time AND on resolution.
            interveningIf: () => !card.IsTapped,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(entersTrigger);
        triggers?.RegisterTriggeredAbility(entersTrigger);

        return card;
    }

    /// <summary>
    /// CR 111.4 — create one 1/1 colourless Myr artifact creature token
    /// under <paramref name="controller"/>'s control. The Token shell only
    /// stamps Creature; the Artifact type is layered additively (CR 111.1 —
    /// Myr tokens are artifact creatures), mirroring Whirler Rogue's Thopter
    /// and Plague Myr's Artifact-Creature shape.
    /// </summary>
    public static Creature CreateMyrToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: MyrTokenName,
            Power: MyrPower,
            Toughness: MyrToughness,
            Subtypes: new[] { CardSubtype.Myr },
            // CR 105.2 / CR 111.4 — "1/1 colorless Myr artifact creature token".
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 111.1 — Myr tokens are artifact creatures. The TokenFactory
        // shell only stamps Creature; layer Artifact on additively.
        token.AddCardType(CardType.Artifact);

        return token;
    }
}
