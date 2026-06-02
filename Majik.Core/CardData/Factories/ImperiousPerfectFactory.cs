using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Imperious Perfect (Lorwyn — Creature — Elf Warrior
/// {2}{G} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Other Elves you control get +1/+1.
///    {G}, {T}: Create a 1/1 green Elf Warrior creature token."
///
/// The marquee Lorwyn Elf lord — a tribal anthem plus a repeatable Elf-token
/// engine on one body. The base shape (name, Creature — Elf Warrior, {2}{G},
/// 2/2) is materialised from the embedded JSON definition
/// (<c>imperious-perfect.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two abilities are layered on
/// top here — the JSON <c>AbilityDefinition</c> schema doesn't express a tribal
/// anthem nor a token-minting activated ability (same posture as
/// <see cref="AdaptiveAutomatonFactory"/>).
///
/// ## Implemented (v1)
///
/// ### "Other Elves you control get +1/+1." (CR 613.7c — Layer 7c P/T)
/// Wired via <see cref="LordStaticEffect"/> with
/// <c>matchingSubtype: Elf, power: 1, toughness: 1, includeSelf: false,
/// opponentsOnly: false, allPlayers: false</c> — controller-scoped (opponents'
/// Elves are unaffected per CR 109.5) and <c>includeSelf: false</c> honours the
/// printed "Other". Identical shape to <see cref="ElvishArchdruidFactory"/> /
/// <see cref="AdaptiveAutomatonFactory"/>. The effect's
/// <see cref="ContinuousEffect.IsActive"/> gates on the source being on the
/// battlefield, so the buff lifts on LTB / flicker.
///
/// ### "{G}, {T}: Create a 1/1 green Elf Warrior creature token." (CR 602)
/// Wired via an <see cref="ActivatedAbility"/> with two costs:
/// <see cref="ManaCostCost"/>("{G}") + <see cref="AdditionalCost.Tap"/> (same
/// cost shape as <see cref="AgnaQelaFactory"/>). On resolution it mints one 1/1
/// green Elf Warrior token under Imperious Perfect's controller via
/// <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111 / 111.4 — the printed
/// token is green, not colourless). Routes through the supplied
/// <see cref="ZoneService"/> when one is wired so token-ETB triggers (Soul
/// Warden / Impact Tremors) fire. The minted Elf Warrior is itself an Elf, so
/// the anthem above pumps it to 2/2 once it's on the battlefield.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path
///   (<see cref="NamedCardFactory"/>). The activated ability is attached for
///   shape observability but the anthem is NOT registered (no layers service)
///   and tokens land via raw zone moves (no <see cref="ZoneService"/>).
/// - <see cref="Create(Player, ContinuousEffectsService?, ZoneService?)"/> —
///   fully-wired overload registering the +1/+1 anthem and funnelling token
///   creation through the zone service so ETB triggers fire.
///
/// ## Deferred (v1 gaps)
/// - <b>Summoning-sickness gate</b>: the {G}{T} ability's tap cost is gated by
///   <see cref="Majik.Core.Rules.ActionValidator"/>'s tap-cost check against
///   creatures with summoning sickness (CR 302.1). Enforcement is upstream at
///   activation-validation time — same posture as
///   <see cref="KrenkoMobBossFactory"/>.
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/> stays
///   on the <see cref="ContinuousEffectsService"/> across zone changes; its
///   <see cref="ContinuousEffect.IsActive"/> check short-circuits when Imperious
///   Perfect isn't on the battlefield so the anthem lifts correctly (same shape
///   as <see cref="ElvishArchdruidFactory"/> / <see cref="AdaptiveAutomatonFactory"/>).
/// </summary>
[CardName("Imperious Perfect")]
public static class ImperiousPerfectFactory
{
    public const string CardName = "Imperious Perfect";
    public const string Slug = "imperious-perfect";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Imperious Perfect with no live runtime services. Suitable for
    /// card-shape / dispatcher tests — the +1/+1 anthem is NOT registered (no
    /// layers service) and the token-minting ability lands tokens via raw zone
    /// moves (no <see cref="ZoneService"/>, so token-ETB triggers won't
    /// auto-fire from the bus). The activated ability is still attached to the
    /// card shape. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Imperious Perfect.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// "Other Elves you control get +1/+1" <see cref="LordStaticEffect"/>
    /// against. May be null — no live anthem.</param>
    /// <param name="zoneService">Optional zone service so each minted Elf
    /// Warrior token publishes <see cref="Events.CardMovedEvent"/> on ETB
    /// (Soul Warden / Impact Tremors chain correctly). When null, tokens are
    /// placed via raw zone moves.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Elf Warrior,
        // {2}{G}, 2/2).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Other Elves you control get +1/+1." — CR 613.7c (Layer 7c P/T) +
        // CR 109.5 (controller scope). allPlayers: false → opponents' Elves
        // aren't pumped; includeSelf: false honours the printed "Other".
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Elf,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        // ----------------------------------------------------------------
        // {G}, {T}: Create a 1/1 green Elf Warrior creature token (CR 602 —
        // activated ability; CR 111 / 111.4 — token creation). Cost shape =
        // ManaCostCost("{G}") + AdditionalCost.Tap (mirrors Agna, Qela).
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 green Elf Warrior creature token",
            () => CreateElfWarriorToken(card.Controller ?? owner, zoneService));

        var tokenAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{G}"),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { tokenEffect });

        card.AddAbility(tokenAbility);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 GREEN Elf Warrior creature token
    /// under <paramref name="controller"/>'s control. The printed token is
    /// green (not colourless), so the spec carries
    /// <see cref="ManaColor.Green"/>. Mirrors
    /// <see cref="DwynensEliteFactory.CreateElfWarriorToken"/> so "1/1 green Elf
    /// Warrior token" minting stays uniform across Elf sources.
    /// </summary>
    public static Creature CreateElfWarriorToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Elf Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 green Elf Warrior creature token".
            Colors: new[] { ManaColor.Green });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
