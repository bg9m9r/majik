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
/// Named-card factory for Mirrex (Phyrexia: All Will Be One).
///
/// Land — Sphere. Oracle text (verified against Scryfall 2026-06-01):
///   "{T}: Add {C}.
///    {T}: Add one mana of any color. Activate only if this land entered this
///    turn.
///    {3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact creature token
///    with toxic 1 and "This token can't block." (Players dealt combat damage
///    by it also get a poison counter.)"
///
/// Composition mirrors the suggested analogues:
/// <list type="bullet">
/// <item><see cref="RestlessPrairieFactory"/> — the base shape (plain nonbasic
///   Land, the Sphere subtype, and the <b>{T}: Add {C}</b> mana ability) is
///   materialised from the embedded JSON definition (<c>mirrex.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>; the gated any-colour ability
///   and the token-making activated ability are layered on here because the
///   JSON <c>AbilityDefinition</c> schema expresses neither.</item>
/// <item><see cref="CrumblingVestigeFactory"/> / <see cref="LotusCobraFactory"/>
///   — the "add one mana of any color" pip (CR 106.1b). The colour is chosen
///   by the optional <paramref name="colorPicker"/> callback, defaulting to
///   <see cref="LotusCobraFactory.DefaultColor"/> (Green) when absent — the
///   same v1 deferral as those cards (no agent colour-prompt hook yet).</item>
/// <item><see cref="ServoSchematicFactory"/> — the 1/1 colourless
///   artifact-creature token mint. <see cref="TokenFactory"/> creates a
///   Creature shell; Artifact is stamped additively (CR 111.1) so the token
///   reports Artifact + Creature — Phyrexian Mite.</item>
/// </list>
///
/// ## Implemented (v1)
/// - <b>Land — Sphere identity</b> — nonbasic Land with the Sphere subtype
///   (CR 205.3i), from the JSON definition.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1). {C}
///   (colourless, CR 107.4c) has no dedicated <see cref="ManaCost"/> bucket
///   today; <c>ManaCost.Parse("C")</c> folds it into Generic, exactly as
///   Wasteland / Crumbling Vestige / Urza's Saga do.
/// - <b>{T}: Add one mana of any color. Activate only if this land entered
///   this turn.</b> — a second <see cref="ManaAbility"/> (CR 605.1 — a true
///   mana ability, no stack) gated by a <c>canActivateCheck</c> tied to
///   <see cref="Permanent.HasSummoningSickness"/>. A land that entered this
///   turn under its controller carries summoning sickness until that
///   controller's next turn begins (CR 302.6 — the same "hasn't been
///   continuously controlled since your most recent turn began" condition),
///   so it is the faithful proxy for "entered this turn" here. The pip
///   colour defaults to Green (Lotus Cobra deferral).
/// - <b>{3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact creature
///   token …</b> — an <see cref="ActivatedAbility"/> (CR 602) with cost
///   <see cref="ManaCostCost"/>("{3}") + <see cref="AdditionalCost.Tap"/>
///   (same cost shape as Blinkmoth Nexus's pump). On resolution it mints one
///   1/1 colourless Phyrexian Mite via <see cref="TokenFactory"/>, stamps
///   Artifact additively (CR 111.1), and attaches the <c>toxic</c> 1 +
///   "can't block" markers (see Notes).
///
/// ## Notes / v1 deferrals
/// - <b>toxic 1</b> is attached to the token as a parameterised
///   <see cref="KeywordAbility"/> (<c>"toxic"</c>, arg 1). Toxic's combat
///   semantics (extra poison counters on combat damage) are "handled by the
///   damage system" per <see cref="Parsing.KeywordRegistry"/> — the marker is
///   recorded for inspection, same posture as every other toxic source.
/// - <b>"This token can't block."</b> is recorded as a <see cref="KeywordAbility"/>
///   marker (<c>"CantBlock"</c>). The combat block-legality path
///   (<see cref="Majik.Core.Combat.BlockLegality.CanBlock"/>) does not yet read
///   a blocker-side "can't block" restriction, so this is observable but not
///   yet enforced — a deliberate v1 deferral (the engine has no blocker-side
///   can't-block primitive), not a half-built mechanic.
/// - <b>Agent prompt for the any-colour pip</b>: reuses the Lotus Cobra /
///   Crumbling Vestige deferral — defaults to Green.
/// </summary>
[CardName("Mirrex")]
public static class MirrexFactory
{
    public const string CardName = "Mirrex";
    public const string Slug = "mirrex";

    public const string TokenName = "Phyrexian Mite";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const int TokenToxic = 1;

    /// <summary>Cost of the token-making ability: {3}, {T}.</summary>
    public const string TokenAbilityManaCost = "{3}";

    /// <summary>
    /// Construct Mirrex with no live runtime services. The any-colour ability
    /// adds the default Green pip and the token ability mints the Mite when
    /// their effects are executed directly (shape-observability path).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, zones: null, colorPicker: null);

    /// <summary>
    /// Construct Mirrex with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">Zone service so the minted token's ETB
    /// CardMovedEvent fires (Soul Warden etc.). May be null.</param>
    /// <param name="colorPicker">Optional callback returning the colour the
    /// any-colour ability adds. When null (or a non-coloured pip) Green is
    /// used — same posture as Lotus Cobra / Crumbling Vestige.</param>
    public static Land Create(
        Player owner,
        ZoneService? zones,
        Func<ManaColor>? colorPicker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land — Sphere,
        // {T}: Add {C} mana ability). The gated any-colour ability and the
        // token-making activated ability are layered on below — neither is
        // expressible in the current JSON AbilityDefinition schema (same
        // posture as RestlessPrairieFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color. Activate only if this land entered
        // this turn.
        //
        // CR 605.1 — a true mana ability (no stack). The "entered this turn"
        // gate (CR 602.5c "Activate only if …") is satisfied while the land
        // still has summoning sickness — i.e. it hasn't been continuously
        // controlled since its controller's most recent turn began
        // (CR 302.6), which is exactly "entered this turn" for a land that
        // entered under its controller. The pip colour defaults to Green
        // (Lotus Cobra deferral).
        // ----------------------------------------------------------------
        var anyColor = new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () =>
            {
                var chosen = colorPicker?.Invoke() ?? LotusCobraFactory.DefaultColor;
                return LotusCobraFactory.BuildOneManaOfColor(chosen);
            },
            canActivateCheck: () =>
                !land.IsTapped && land.HasSummoningSickness);

        land.AddAbility(anyColor);

        // ----------------------------------------------------------------
        // {3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact creature
        // token with toxic 1 and "This token can't block."
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {3} + tap (same cost shape as Blinkmoth Nexus's pump). On
        // resolution it mints one Mite token under the source's live
        // controller.
        // ----------------------------------------------------------------
        var makeMite = new Effect(
            $"{CardName}: create a 1/1 colorless Phyrexian Mite artifact creature token (toxic 1, can't block)",
            () => CreateMiteToken(land, owner, zones));

        var tokenAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(TokenAbilityManaCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { makeMite });

        land.AddAbility(tokenAbility);

        return land;
    }

    /// <summary>
    /// CR 111.1 / CR 111.4 — create one 1/1 colourless Phyrexian Mite artifact
    /// creature token under the source land's live controller. The token is a
    /// Phyrexian Mite creature with an explicit colourless colour set, stamped
    /// Artifact additively (multi-type artifact creature), and carries the
    /// <c>toxic</c> 1 + "can't block" markers (see factory Notes for the
    /// can't-block deferral).
    /// </summary>
    public static Creature CreateMiteToken(Land source, Player owner, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(owner);

        var controller = source.Controller ?? owner;

        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Mite },
            Keywords: null,
            Colors: System.Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        // CR 111.1 — the Mite is an artifact creature. TokenFactory mints a
        // Creature shell; stamp Artifact additively so it reports both types
        // (same multi-type pattern as Servo Schematic's Servo).
        token.AddCardType(CardType.Artifact);

        // toxic 1 (CR 702.180) — parameterised keyword marker; combat poison
        // semantics are handled by the damage system per KeywordRegistry.
        token.AddAbility(new KeywordAbility("toxic", token, controller, arg: TokenToxic));

        // "This token can't block." — recorded as a marker (CR 509.1a). The
        // block-legality path does not yet read a blocker-side can't-block
        // restriction; see the factory Notes (v1 deferral).
        token.AddAbility(new KeywordAbility("CantBlock", token, controller));

        return token;
    }
}
