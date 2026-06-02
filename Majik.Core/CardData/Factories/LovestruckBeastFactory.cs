using Majik.Core.Abilities;
using Majik.Core.CardData.Adventures;
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
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lovestruck Beast // Heart's Desire (Throne of
/// Eldraine, {2}{G}).
///
/// ## Card text (verified against Scryfall 2026-06-02)
/// - Lovestruck Beast — Creature — Beast Noble {2}{G}, 5/5.
///     "This creature can't attack unless you control a 1/1 creature."
/// - Heart's Desire (Adventure) — Sorcery — Adventure {G}.
///     "Create a 1/1 white Human creature token."
///     (Then exile this card. You may cast the creature later from exile.)
///
/// Lovestruck Beast is the green Adventure sibling of
/// <see cref="BrazenBorrowerFactory"/> (creature // Adventure) and
/// <see cref="MosswoodDreadknightFactory"/> (green creature // <i>sorcery</i>
/// Adventure) — same framing: a vanilla-bodied creature half plus a
/// no-target sorcery Adventure half. Heart's Desire mints the same 1/1
/// white Human token <see cref="AdelineResplendentCatharFactory"/> /
/// <see cref="CastleArdenvaleFactory"/> produce.
///
/// ## Implemented (v1)
/// - <b>Creature shape</b> (name / Creature / Beast Noble / {2}{G} / 5/5)
///   materialised from the embedded JSON definition (<c>lovestruck-beast.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BrazenBorrowerFactory"/> / <see cref="HazoretTheFerventFactory"/>
///   (the JSON ability schema does not express predicate-mode combat
///   restrictions or the Adventure half, so those are layered on here).
/// - <b>"This creature can't attack unless you control a 1/1 creature"
///   (CR 508.1c)</b>: a self-scoped predicate-mode
///   <see cref="CombatRestrictionEffect"/>
///   (<see cref="CombatRestriction.CannotAttack"/>). The predicate matches
///   only when the queried creature IS Lovestruck Beast and trips while the
///   controller does NOT control any 1/1 creature ("can't attack unless you
///   control a 1/1" == "is restricted while you control no 1/1"). "1/1" reads
///   effective P/T (<see cref="Creature.Power"/> / <see cref="Creature.Toughness"/>)
///   on the live battlefield, so the lock lifts the instant a 1/1 (e.g. the
///   Heart's Desire token) is under the controller's control. Gated on
///   Lovestruck Beast being on the battlefield (CR 603.6e). Same predicate-mode
///   self-scoped shape as <see cref="HazoretTheFerventFactory"/>'s
///   can't-attack-or-block. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
/// - <b>Heart's Desire Adventure half (CR 715)</b>: attached as an
///   <see cref="AdventureSpec"/>. The cast flow
///   (<see cref="Costs.AdventureAlternativeCost"/> + <see cref="SpellCastFlow"/>)
///   routes Heart's Desire through the standard Rule 601 sequence at the
///   Adventure mana cost ({G}, sorcery-speed — Heart's Desire is a Sorcery),
///   exiles the card on resolve (CR 715.3d), and grants the owner a runtime
///   "may cast the creature later from exile" permission for the printed
///   Lovestruck Beast cost via <see cref="Card.GrantRuntimeExileCast"/> — the
///   same exile-cast probe Bonecrusher Giant / Mosswood Dreadknight reuse.
/// - <b>Heart's Desire resolve</b> (<see cref="BuildAdventureSpell"/>): a
///   no-target <see cref="SpellDefinition"/> whose resolve effect creates one
///   1/1 white Human creature token (CR 111 / 111.4) under the caster's
///   control via <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + the Heart's Desire
///   AdventureSpec. The combat restriction is NOT registered (no
///   continuous-effects service). The overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — additionally
///   registers the can't-attack-unless restriction.
///
/// ## Deferred (v1 gaps)
/// - <b>Bot attack planner</b>: the heuristic bot does not yet read the
///   <see cref="CombatRestriction"/> when proposing attackers; the engine
///   rejects any illegal declaration the predicate catches (same posture as
///   Hazoret the Fervent / Ensnaring Bridge).
/// - <b>Heart's Desire token ETB bus event</b>: the no-target resolve path
///   mints the token via the raw (zones == null) <see cref="TokenFactory"/>
///   route — the Adventure <see cref="AdventureSpec.BuildDefinition"/> closure
///   carries no <see cref="ZoneService"/>, so no <see cref="Majik.Core.Events.CardMovedEvent"/>
///   fires for the token's ETB (same posture as Mosswood Dreadknight's Dread
///   Whispers raw zone manipulation). ETB-matters observers (Soul Warden) are
///   not notified of the token in this path.
/// </summary>
[CardName("Lovestruck Beast")]
public static class LovestruckBeastFactory
{
    public const string CardName = "Lovestruck Beast";
    public const string Slug = "lovestruck-beast";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 5;
    public const int Toughness = 5;

    public const string AdventureName = "Heart's Desire";
    public const string AdventureManaCost = "{G}";

    public const string TokenName = "Human";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Lovestruck Beast with no continuous-effects service. The
    /// Heart's Desire <see cref="AdventureSpec"/> is attached; the
    /// can't-attack-unless restriction is NOT registered. Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Lovestruck Beast.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Game-level continuous-effects service.
    /// When supplied, the self-scoped can't-attack-unless-you-control-a-1/1
    /// restriction is registered, gated on Lovestruck Beast being on the
    /// battlefield. Pass null to skip the restriction.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Beast Noble,
        // {2}{G}, 5/5). The JSON carries no abilities — the printed behaviour
        // is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // "This creature can't attack unless you control a 1/1 creature."
        // CR 508.1c.
        //
        // Predicate-mode CombatRestrictionEffect, self-scoped: the predicate
        // matches only when the queried creature IS Lovestruck Beast, and
        // trips (imposes CannotAttack) while the controller controls NO 1/1
        // creature ("can't attack unless you control a 1/1" == "restricted
        // while you control no 1/1"). "1/1" reads effective P/T live every
        // validation pass, so a 1/1 entering (e.g. the Heart's Desire token)
        // lifts the restriction immediately.
        //
        // "you" — Lovestruck Beast's controller (CR 109.5). Gate: only active
        // while Lovestruck Beast is on the battlefield (CR 603.6e).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            bool CannotAttackNow(Creature queried)
            {
                if (!ReferenceEquals(queried, card)) return false; // self-scoped
                var ctrl = card.Controller;
                if (ctrl == null) return true; // no controller → no 1/1 control → locked
                return !ControlsAOneOne(ctrl);
            }

            bool OnBattlefield() => card.Zone == ZoneType.Battlefield;

            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotAttack,
                predicate: CannotAttackNow,
                isActiveGate: OnBattlefield,
                expiresAtEndOfTurn: false));
        }

        // ----------------------------------------------------------------
        // CR 715 — attach the Heart's Desire Adventure half for the cast
        // pipeline. The AdventureSpec carries the alternative characteristics
        // ({G}, Sorcery) + an effects-factory closure; the cast path is driven
        // by AdventureAlternativeCost + SpellCastFlow.
        // ----------------------------------------------------------------
        card.AdventureSpec = new AdventureSpec(
            Name: AdventureName,
            ManaCost: ManaCost.Parse(AdventureManaCost),
            AdventureType: CardType.Sorcery,
            BuildDefinition: BuildAdventureSpell);

        return card;
    }

    /// <summary>
    /// CR 109.5 / CR 208.2 — does <paramref name="controller"/> control a 1/1
    /// creature? Reads effective P/T (<see cref="Creature.Power"/> /
    /// <see cref="Creature.Toughness"/>) on the controller's battlefield.
    /// </summary>
    private static bool ControlsAOneOne(Player controller)
        => controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.Power == 1 && c.Toughness == 1);

    /// <summary>
    /// Build the standalone Heart's Desire <see cref="SpellDefinition"/> — no
    /// target requests; on resolve, create one 1/1 white Human creature token
    /// (CR 111 / 111.4) under the caster's control.
    /// </summary>
    /// <param name="caster">The controller of Heart's Desire — the token's
    /// controller (CR 111.6).</param>
    /// <param name="targetResolver">Unused (no targets), kept for API symmetry
    /// with other Adventure factories (Petty Theft / Stomp / Dread
    /// Whispers).</param>
    public static SpellDefinition BuildAdventureSpell(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    "Heart's Desire: create a 1/1 white Human creature token",
                    () => CreateHumanToken(caster)),
            });
    }

    /// <summary>
    /// CR 111 / CR 111.4 — mint one 1/1 white Human creature token for the
    /// caster. Raw (zones == null) <see cref="TokenFactory"/> route — the
    /// Adventure closure carries no <see cref="ZoneService"/>, so no
    /// CardMovedEvent fires (documented gap; same posture as Mosswood
    /// Dreadknight's Dread Whispers).
    /// </summary>
    private static void CreateHumanToken(Player controller)
    {
        TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec(
                Name: TokenName,
                Power: TokenPower,
                Toughness: TokenToughness,
                Subtypes: new[] { CardSubtype.Human },
                Keywords: null,
                Colors: new[] { ManaColor.White }),
            controller,
            zones: null);
    }
}
