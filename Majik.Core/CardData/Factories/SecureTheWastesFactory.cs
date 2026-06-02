using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Secure the Wastes (Dragons of Tarkir, {X}{W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Create X 1/1 white Warrior creature tokens."
///
/// The base shape (name, Instant type, {X}{W}) is materialised from the
/// embedded JSON definition (<c>secure-the-wastes.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="BattleScreechFactory"/>. The token-creation resolve body is
/// layered on here because the JSON schema doesn't express token creation.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {X}{W}.
/// - <b>X-keyed token count (CR 107.3)</b>: built via
///   <see cref="BuildSpellDefinition"/>. <see cref="SpellDefinition.HasVariableX"/>
///   = true so the cast flow prompts for X at cast time; resolution reads
///   <c>ChosenSpellParams.X</c> as the number of tokens to create — the same
///   X-read posture as <see cref="BonfireOfTheDamnedFactory"/>, except the
///   count drives token creation rather than damage.
/// - No target requests — the effect resolves entirely on the caster
///   (CR 115.1).
/// - Resolve effect (<see cref="BuildResolveEffect"/>): create X 1/1 white
///   Warrior creature tokens via <see cref="TokenFactory.CreateOnBattlefield"/>
///   (CR 111 / 111.4). White colour is stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/> (CR 105 / 111.4); same
///   looped-token pattern as <see cref="KrenkosCommandFactory"/> /
///   <see cref="BattleScreechFactory"/>, only the count (X) and the
///   colour/subtype (white Warrior) differ.
/// - <b>X = 0 (CR 107.3 / 107.3b)</b>: zero tokens created — the loop runs
///   zero times.
/// </summary>
[CardName("Secure the Wastes")]
public static class SecureTheWastesFactory
{
    public const string CardName = "Secure the Wastes";
    public const string Slug = "secure-the-wastes";

    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct the Secure the Wastes instant shape (name, Instant, {X}{W})
    /// from the embedded JSON definition. No resolve effect is bound — callers
    /// build the create-X-Warriors body via <see cref="BuildSpellDefinition"/>
    /// or <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Instant)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Secure the Wastes. No
    /// modes, no target requests (CR 115.1); <see cref="SpellDefinition.HasVariableX"/>
    /// is true so the cast flow prompts for X. Resolution reads
    /// <c>ChosenSpellParams.X</c> and creates that many 1/1 white Warrior
    /// tokens on the caster's battlefield.
    /// </summary>
    /// <param name="caster">The player casting Secure the Wastes.</param>
    /// <param name="zones">Optional zone service so spawned tokens publish
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> on ETB.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: chosen => BuildResolveEffect(caster, chosen.X ?? 0, zones));
    }

    /// <summary>
    /// Build the resolve effect: create <paramref name="count"/> 1/1 white
    /// Warrior creature tokens on the caster's battlefield (CR 111 / 111.4).
    /// CR 107.3 — X is locked at cast time; a count of 0 creates no tokens.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        int count,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create {count} 1/1 white Warrior creature tokens.",
                () =>
                {
                    // CR 107.3 — negative/zero X produces no tokens.
                    for (var i = 0; i < count; i++)
                    {
                        CreateWarriorToken(caster, zones);
                    }
                }),
        };
    }

    /// <summary>
    /// CR 111 / 111.4 — create one 1/1 white Warrior creature token under
    /// <paramref name="controller"/>. White colour is stamped via
    /// <see cref="TokenFactory.TokenSpec.Colors"/> (CR 105 / 111.4).
    /// </summary>
    public static Creature CreateWarriorToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Warrior },
            Keywords: null,
            // CR 111.4 — printed "1/1 white Warrior creature token".
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
