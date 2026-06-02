using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Overlord of the Hauntwoods (Duskmourn: House of
/// Horror, {3}{G}{G}). Enchantment Creature — Avatar Horror 6/5. Oracle
/// text (verified against Scryfall):
///   "Impending 4—{1}{G}{G} (If you cast this spell for its impending cost,
///    it enters with four time counters and isn't a creature until the last
///    is removed. At the beginning of your end step, remove a time counter
///    from it.)
///    Whenever this permanent enters or attacks, create a tapped colorless
///    land token named Everywhere that is every basic land type."
///
/// Same cycle / identical Impending wiring as
/// <see cref="OverlordOfTheBalemurkFactory"/>; only the enters-or-attacks
/// trigger body differs (the green Overlord mints an "Everywhere" land token
/// instead of milling + reanimating).
///
/// The card's base shape (name, Enchantment + Creature types, Avatar +
/// Horror subtypes, {3}{G}{G}, 6/5) is materialised from the embedded JSON
/// definition (<c>overlord-of-the-hauntwoods.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (the Impending marker keyword + the enters-or-attacks trigger) are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express keyword markers or token-creation effects, so they live in the
/// factory (same posture as <see cref="OverlordOfTheBalemurkFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Enters-or-attacks trigger (CR 603.1 ETB + CR 508.1f attack)</b>:
///   two <see cref="TriggeredAbility"/> instances sharing one effect body —
///   one gated on <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> (same dual-trigger shape as
///   <see cref="OverlordOfTheBalemurkFactory"/> / <see cref="PrimevalTitanFactory"/>).
///   On resolution: create one tapped colorless land token named
///   "Everywhere" that is every basic land type (CR 111 token creation,
///   CR 305.6 basic land types). The token enters the battlefield tapped
///   under the Overlord's controller and is a colorless permanent
///   (CR 111.4 — its colour identity is explicitly empty despite producing
///   five colours of mana).
/// - <b>"Everywhere" land token</b>: a colourless, non-basic Land with all
///   five basic land subtypes — Plains, Island, Swamp, Mountain, Forest
///   (CR 305.6). Per CR 305.6 a land with a basic land type has the
///   corresponding intrinsic mana ability, so the token is wired with five
///   <see cref="ManaAbility"/> instances (one per produced colour, CR 605.1
///   — mana abilities don't use the stack). It is NOT a basic land (no
///   Basic supertype), matching the printed "land token … that is every
///   basic land TYPE".
///
/// ## Impending — modelled as a marker keyword (deferred mechanic)
/// "Impending 4—{1}{G}{G}" is an alternative-cost keyword (Duskmourn). As
/// with <see cref="OverlordOfTheBalemurkFactory"/>, the full Impending
/// mechanic (alt-cost cast with four Time counters, the Layer-4 "isn't a
/// creature" type-strip while counters remain per CR 613, and the end-step
/// "remove a time counter" delayed trigger) is deferred. Following the
/// established marker-keyword precedent (Delve, Suspend), Impending is wired
/// as a <see cref="KeywordAbility"/> marker with <c>Arg = 4</c> so
/// introspection can see the keyword + counter count. When cast for its
/// normal {3}{G}{G} cost the card behaves completely; only the alternate
/// way to pay for it is the deferred part.
/// </summary>
[CardName("Overlord of the Hauntwoods")]
public static class OverlordOfTheHauntwoodsFactory
{
    public const string CardName = "Overlord of the Hauntwoods";
    public const string Slug = "overlord-of-the-hauntwoods";

    /// <summary>Impending counter count — "Impending 4".</summary>
    public const int ImpendingCount = 4;

    /// <summary>Name of the land token the trigger creates.</summary>
    public const string TokenName = "Everywhere";

    /// <summary>
    /// The five basic land subtypes the "Everywhere" token carries
    /// (CR 305.6). Every basic land type → five intrinsic mana abilities.
    /// </summary>
    public static readonly IReadOnlyList<CardSubtype> EverywhereSubtypes = new[]
    {
        CardSubtype.Plains,
        CardSubtype.Island,
        CardSubtype.Swamp,
        CardSubtype.Mountain,
        CardSubtype.Forest,
    };

    /// <summary>
    /// Construct Overlord of the Hauntwoods with no live TriggerManager
    /// wiring (the shape/dispatcher path). The two enters-or-attacks
    /// triggers + the Impending marker are attached for shape inspection.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Overlord of the Hauntwoods with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB + attack triggers are
    /// registered so the matching events land their abilities on the stack
    /// automatically.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Avatar + Horror subtypes, {3}{G}{G}, 6/5). The
        // JSON carries no abilities — the Impending marker + the
        // enters-or-attacks trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Impending 4 — marker keyword (mechanic deferred; see class
        // remarks). Arg carries the printed counter count.
        card.AddAbility(new KeywordAbility("Impending", card, owner, arg: ImpendingCount));

        // ----------------------------------------------------------------
        // Shared effect body: "create a tapped colorless land token named
        // Everywhere that is every basic land type." (CR 111 token
        // creation, CR 305.6 basic land types.) The controller at resolve
        // time is whoever currently controls the Overlord; we capture the
        // card and read card.Controller inside the effect.
        // ----------------------------------------------------------------
        IEffect BuildTriggerEffect(string label) =>
            new Effect(label, _ => CreateEverywhereTokenAsync(card));

        // ETB trigger — CR 603.1.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: enters — create tapped Everywhere land token") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack trigger — CR 508.1f.
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: attacks — create tapped Everywhere land token") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Create one tapped colorless "Everywhere" land token that is every
    /// basic land type, onto the Overlord's controller's battlefield
    /// (CR 111 / CR 305.6). The token enters tapped (CR 110.6b — it's
    /// created already tapped, not via an enters-tapped replacement).
    /// </summary>
    public static ValueTask CreateEverywhereTokenAsync(Creature overlord)
    {
        ArgumentNullException.ThrowIfNull(overlord);
        var controller = overlord.Controller
            ?? throw new InvalidOperationException("Overlord of the Hauntwoods has no controller at resolve time.");
        CreateEverywhereToken(controller);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Mint the "Everywhere" land token for <paramref name="controller"/>
    /// and put it onto the battlefield tapped. Public + returning the token
    /// so tests can assert its shape.
    /// </summary>
    public static Land CreateEverywhereToken(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 111.4 — a colourless, non-basic Land with every basic land
        // type. The token is colourless even though it taps for five
        // colours; its colour identity is explicitly empty.
        var token = new Land(TokenName, supertypes: null, subtypes: EverywhereSubtypes)
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        token.SetTokenColors(Array.Empty<ManaColor>());

        // CR 305.6 — a land with a basic land type has the corresponding
        // intrinsic mana ability. Five ManaAbility instances (one per
        // produced colour); mana abilities don't use the stack (CR 605.1).
        token.AddAbility(new ManaAbility(token, controller, ManaCost.Parse("W")));
        token.AddAbility(new ManaAbility(token, controller, ManaCost.Parse("U")));
        token.AddAbility(new ManaAbility(token, controller, ManaCost.Parse("B")));
        token.AddAbility(new ManaAbility(token, controller, ManaCost.Parse("R")));
        token.AddAbility(new ManaAbility(token, controller, ManaCost.Parse("G")));

        // Put the token onto the battlefield using the sentinel-library
        // pattern shared by TokenFactory so CardMovedEvent fires (ETB
        // subscribers see the token). Route through a live ZoneService when
        // one is registered for the controller.
        token.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(token);

        var zones = ZoneServiceRegistry.Get(controller);
        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            controller.Zones.Battlefield.AddCard(token);
            token.SetZone(ZoneType.Battlefield);
        }

        // CR 110.6b — the token is created tapped. Tap after the move so any
        // ETB hooks have run; double-tap is a no-op.
        if (!token.IsTapped) token.Tap();

        return token;
    }
}
