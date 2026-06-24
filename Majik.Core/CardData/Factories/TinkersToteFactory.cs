using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tinker's Tote (Bloomburrow, {2}{W}).
///
/// Artifact. Oracle text (verified against the embedded Scryfall seed):
///   "When this artifact enters, create two 1/1 colorless Gnome artifact
///    creature tokens.
///    {W}, Sacrifice this artifact: You gain 3 life."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {2}{W}; mana value 3; colors W (the {W} pip in the
///     printed cost makes the artifact white per CR 202.2 — no colour
///     indicator needed, unlike Carrot Cake whose cost has no coloured pip).</item>
///   <item>Type line: Artifact (CR 301).</item>
/// </list>
///
/// The base shape (name, Artifact, {2}{W}) is materialised from the embedded
/// JSON definition (<c>tinkers-tote.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities —
/// the ETB token trigger and the sac-for-life activated ability are layered on
/// here (same posture as <see cref="CarrotCakeFactory"/>, the closest analogue:
/// a white Artifact with an ETB token-create and a "{mana}, Sacrifice this
/// artifact: You gain 3 life" ability).
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Artifact {2}{W}, owner / controller stamped. White is
///   derived from the {W} pip in the mana cost (CR 202.2) — no explicit colour
///   override.
/// - <b>ETB triggered ability (CR 603.6a)</b>: a single
///   <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution it creates
///   TWO 1/1 colourless Gnome artifact-creature tokens (CR 111 — one token per
///   "create", two creates here; same two-token posture as
///   <see cref="GisasBiddingFactory"/>). Each token is a 1/1 colourless
///   <see cref="CardSubtype.Gnome"/> creature with the
///   <see cref="CardType.Artifact"/> type stamped additively (TokenFactory
///   mints a Creature shell — same artifact-creature multi-type stamp as
///   <see cref="ServoSchematicFactory"/>'s Servos).
/// - <b>"{W}, Sacrifice this artifact: You gain 3 life." (CR 602.1)</b>: an
///   <see cref="ActivatedAbility"/> whose costs are
///   <see cref="Primitives.Costs.Mana"/>("{W}") and a bus-aware
///   <see cref="SacrificeSelfCost"/> (no <c>{T}</c> in the printed cost, unlike
///   Carrot Cake). On resolution the controller gains 3 life
///   (<see cref="Fx.GainLife"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached for
///   shape observability; not registered with any <see cref="TriggerManager"/>,
///   no <see cref="ZoneService"/> wiring (tokens enter via the raw zone path).
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully wired:
///   the ETB trigger registers with the bus, and each Gnome token's ETB routes
///   through the <see cref="ZoneService"/> so its <see cref="Events.CardMovedEvent"/>
///   publishes (downstream ETB subscribers see the tokens' arrival).
/// </summary>
[CardName("Tinker's Tote")]
public static class TinkersToteFactory
{
    public const string CardName = "Tinker's Tote";
    public const string Slug = "tinkers-tote";

    /// <summary>Activation mana cost of the gain-life ability — {W}.</summary>
    public const string LifeAbilityManaCost = "{W}";

    /// <summary>Life gained by the "{W}, Sacrifice" ability.</summary>
    public const int LifeGain = 3;

    /// <summary>Gnome token name.</summary>
    public const string GnomeTokenName = "Gnome";
    public const int GnomePower = 1;
    public const int GnomeToughness = 1;

    /// <summary>
    /// Construct Tinker's Tote with no live wiring. The ETB token trigger is
    /// attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring
    /// (tokens enter via the raw zone path). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Tinker's Tote with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, each Gnome token's ETB routes through
    /// <see cref="ZoneService.MoveCardTo"/> so <see cref="Events.CardMovedEvent"/>
    /// publishes for any zone-change subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with the
    /// bus so the corresponding ETB lands the ability on the stack
    /// automatically (CR 603.2).</param>
    public static Artifact Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact, {2}{W}).
        // The JSON carries no abilities — both are layered on below. White is
        // derived from the {W} mana-cost pip (CR 202.2), so no colour override.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111.
        //   "When this artifact enters, create two 1/1 colorless Gnome
        //    artifact creature tokens."
        // No targets — pure token-creation (two creates → two tokens, CR 111).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create two 1/1 colorless Gnome artifact creature tokens",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateGnomeToken(controller, zones);
                CreateGnomeToken(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // {W}, Sacrifice this artifact: You gain 3 life. (CR 602.1)
        // Cost is a single white mana pip + a bus-aware SacrificeSelfCost —
        // NO {T} in the printed cost (unlike Carrot Cake's {2},{T},Sac).
        // ----------------------------------------------------------------
        var lifeEffect = new Effect(
            $"{CardName}: you gain {LifeGain} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.GainLife(controller, LifeGain);
            });

        var lifeAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                Primitives.Costs.Mana(LifeAbilityManaCost),
                new SacrificeSelfCost(card),
            },
            effects: new IEffect[] { lifeEffect });
        card.AddAbility(lifeAbility);

        return card;
    }

    /// <summary>
    /// CR 111.1 / CR 111.4 — create one 1/1 colourless Gnome artifact creature
    /// token under <paramref name="controller"/>'s control. The token is a
    /// <see cref="CardSubtype.Gnome"/> creature with an explicit colourless
    /// colour set (so colour-matters subscribers see "no colours" rather than
    /// probing the empty mana cost), then additively stamped
    /// <see cref="CardType.Artifact"/> for the artifact-creature multi-type
    /// (TokenFactory mints a Creature shell — same stamp as
    /// <see cref="ServoSchematicFactory"/>'s Servos).
    /// </summary>
    public static Creature CreateGnomeToken(Player controller, ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: GnomeTokenName,
            Power: GnomePower,
            Toughness: GnomeToughness,
            Subtypes: new[] { CardSubtype.Gnome },
            Keywords: null,
            // CR 111.4 — printed colourless token. Explicit empty colour set.
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        // CR 111.1 — Gnome tokens are artifact creatures. Stamp Artifact
        // additively so the token reports Artifact + Creature — Gnome.
        token.AddCardType(CardType.Artifact);

        return token;
    }
}
