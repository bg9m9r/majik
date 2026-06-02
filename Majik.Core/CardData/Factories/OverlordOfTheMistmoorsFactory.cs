using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Overlord of the Mistmoors (Duskmourn: House of
/// Horror, {5}{W}{W}). Enchantment Creature — Avatar Horror 6/6. Oracle
/// text (verified against Scryfall):
///   "Impending 4—{2}{W}{W} (If you cast this spell for its impending cost,
///    it enters with four time counters and isn't a creature until the last
///    is removed. At the beginning of your end step, remove a time counter
///    from it.)
///    Whenever this permanent enters or attacks, create two 2/1 white Insect
///    creature tokens with flying."
///
/// Same cycle / identical Impending wiring as
/// <see cref="OverlordOfTheBalemurkFactory"/> and
/// <see cref="OverlordOfTheHauntwoodsFactory"/>; only the enters-or-attacks
/// trigger body differs (the white Overlord mints two 2/1 white flying
/// Insect tokens).
///
/// The card's base shape (name, Enchantment + Creature types, Avatar +
/// Horror subtypes, {5}{W}{W}, 6/6) is materialised from the embedded JSON
/// definition (<c>overlord-of-the-mistmoors.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (the Impending marker keyword + the enters-or-attacks trigger) are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express keyword markers or token-creation effects, so they live in the
/// factory (same posture as <see cref="OverlordOfTheHauntwoodsFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Enters-or-attacks trigger (CR 603.1 ETB + CR 508.1f attack)</b>:
///   two <see cref="TriggeredAbility"/> instances sharing one effect body —
///   one gated on <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> (same dual-trigger shape as
///   <see cref="OverlordOfTheHauntwoodsFactory"/> / <see cref="PrimevalTitanFactory"/>).
///   On resolution: create two 2/1 white Insect creature tokens with flying
///   (CR 111 token creation, CR 702.9 Flying). The tokens enter the
///   battlefield under whoever currently controls the Overlord (read off
///   <see cref="Permanent.Controller"/> at resolve time).
/// - <b>2/1 white flying Insect token</b>: minted via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>,
///   once per token (the printed count is two). Colour stamped explicitly
///   White via <see cref="TokenFactory.TokenSpec.Colors"/>; Flying granted
///   via the Keywords list (CR 702.9). Routed through a live
///   <see cref="ZoneService"/> when one is registered for the controller so
///   each token's <see cref="Events.CardMovedEvent"/> fires on ETB (Soul
///   Warden / Impact Tremors see it), mirroring
///   <see cref="OverlordOfTheHauntwoodsFactory"/>'s registry-driven path.
///
/// ## Impending — modelled as a marker keyword (deferred mechanic)
/// "Impending 4—{2}{W}{W}" is an alternative-cost keyword (Duskmourn). As
/// with the rest of the cycle, the full Impending mechanic (alt-cost cast
/// with four Time counters, the Layer-4 "isn't a creature" type-strip while
/// counters remain per CR 613, and the end-step "remove a time counter"
/// delayed trigger) is deferred. Following the established marker-keyword
/// precedent (Delve, Suspend), Impending is wired as a
/// <see cref="KeywordAbility"/> marker with <c>Arg = 4</c> so introspection
/// can see the keyword + counter count. When cast for its normal {5}{W}{W}
/// cost the card behaves completely; only the alternate way to pay for it is
/// the deferred part.
/// </summary>
[CardName("Overlord of the Mistmoors")]
public static class OverlordOfTheMistmoorsFactory
{
    public const string CardName = "Overlord of the Mistmoors";
    public const string Slug = "overlord-of-the-mistmoors";

    /// <summary>Impending counter count — "Impending 4".</summary>
    public const int ImpendingCount = 4;

    /// <summary>Number of Insect tokens the enters-or-attacks trigger mints.</summary>
    public const int TokenCount = 2;

    /// <summary>Name of the creature token the trigger creates.</summary>
    public const string TokenName = "Insect";

    /// <summary>Power of each Insect token.</summary>
    public const int TokenPower = 2;

    /// <summary>Toughness of each Insect token.</summary>
    public const int TokenToughness = 1;

    /// <summary>
    /// The TokenSpec for the 2/1 white Insect token with flying (CR 111.4 /
    /// CR 702.9). Colour stamped explicitly White; Flying via the Keywords
    /// list.
    /// </summary>
    public static readonly TokenFactory.TokenSpec InsectSpec = new(
        Name: TokenName,
        Power: TokenPower,
        Toughness: TokenToughness,
        Subtypes: new[] { CardSubtype.Insect },
        Keywords: new[] { "Flying" },
        Colors: new[] { ManaColor.White });

    /// <summary>
    /// Construct Overlord of the Mistmoors with no live TriggerManager
    /// wiring (the shape/dispatcher path). The two enters-or-attacks
    /// triggers + the Impending marker are attached for shape inspection.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Overlord of the Mistmoors with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB + attack triggers are
    /// registered so the matching events land their abilities on the stack
    /// automatically.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Avatar + Horror subtypes, {5}{W}{W}, 6/6). The
        // JSON carries no abilities — the Impending marker + the
        // enters-or-attacks trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Impending 4 — marker keyword (mechanic deferred; see class
        // remarks). Arg carries the printed counter count.
        card.AddAbility(new KeywordAbility("Impending", card, owner, arg: ImpendingCount));

        // ----------------------------------------------------------------
        // Shared effect body: "create two 2/1 white Insect creature tokens
        // with flying." (CR 111 token creation, CR 702.9 Flying.) The
        // controller at resolve time is whoever currently controls the
        // Overlord; we capture the card and read card.Controller inside the
        // effect.
        // ----------------------------------------------------------------
        IEffect BuildTriggerEffect(string label) =>
            new Effect(label, _ => CreateInsectTokensAsync(card));

        // ETB trigger — CR 603.1.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: enters — create two 2/1 white flying Insects") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack trigger — CR 508.1f.
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: attacks — create two 2/1 white flying Insects") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Create two 2/1 white flying Insect tokens onto the Overlord's
    /// controller's battlefield (CR 111). The controller is read off the
    /// Overlord at resolve time.
    /// </summary>
    public static ValueTask CreateInsectTokensAsync(Creature overlord)
    {
        ArgumentNullException.ThrowIfNull(overlord);
        var controller = overlord.Controller
            ?? throw new InvalidOperationException("Overlord of the Mistmoors has no controller at resolve time.");
        CreateInsectTokens(controller);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Mint <see cref="TokenCount"/> 2/1 white flying Insect tokens for
    /// <paramref name="controller"/> and put them onto the battlefield
    /// (CR 111). Routes through a live <see cref="ZoneService"/> when one is
    /// registered for the controller so each token's CardMovedEvent fires on
    /// ETB. Returns the minted tokens so tests can assert their shape.
    /// </summary>
    public static IReadOnlyList<Creature> CreateInsectTokens(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var zones = ZoneServiceRegistry.Get(controller);

        var minted = new List<Creature>(TokenCount);
        for (var i = 0; i < TokenCount; i++)
        {
            minted.Add(TokenFactory.CreateOnBattlefield(InsectSpec, controller, zones));
        }
        return minted;
    }
}
