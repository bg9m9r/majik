using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brave the Elements (Magic 2010 / Magic Origins, {W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose a color. White creatures you control gain protection from the
///    chosen color until end of turn."
///
/// ## Modelling "Choose a color" (CR 700.2 modal analogue)
///
/// Brave the Elements takes no target — it affects EVERY white creature the
/// caster controls (CR 109.2 / CR 702.16). Its only choice is a colour, made
/// as the spell is cast (CR 601.2b — "If the spell ... requires the player to
/// choose ... colors ... the player announces these choices"). The engine's
/// modal <see cref="SpellDefinition.Modes"/> mechanism is the supported shape
/// for a single up-front choose-one selection, so the colour pick is expressed
/// as a five-mode "Choose one —" (white / blue / black / red / green) with NO
/// target requests. The cast flow prompts the caster for the mode the same way
/// it does for <see cref="AbradeFactory"/>; <see cref="BuildDefinition"/>'s
/// <c>EffectFactory</c> reads <see cref="ChosenSpellParams.ModeIndex"/> to map
/// the picked mode back to a <see cref="ManaColor"/>.
///
/// ## Implemented (v1)
///
/// - Instant card with printed mana cost {W}. Card shape comes from the
///   embedded JSON (<c>brave-the-elements.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same load path as
///   <see cref="BlossomingDefenseFactory"/>).
/// - <b>Resolution (CR 702.16 protection grant)</b>: on resolve, every white
///   creature the caster controls on the battlefield
///   (<see cref="CardColors.GetColors"/> contains
///   <see cref="ManaColor.White"/> — CR 105 / CR 202.2) receives a
///   self-sourced <see cref="GrantAbilityEffect"/> adding a
///   <see cref="ProtectionAbility"/> with the chosen colour's quality, expiring
///   at end of turn (CR 514.2 / CR 613.1f). Self-sourced (source == the granted
///   creature) so the grant survives the spell card leaving the stack — same
///   posture as <see cref="GiverOfRunesFactory.Resolve"/> (an instant's card
///   moves to the graveyard on resolution, so it can't be the effect source).
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/> reads the
///   materialised quality.
///
/// ## Notes
///
/// - The set of affected creatures is fixed at resolution (CR 608.2 — the
///   effect "does as much as it can" with the battlefield as it stands when it
///   resolves). Creatures that enter or turn white after resolution are not
///   retroactively protected, matching the oracle wording.
/// </summary>
[CardName("Brave the Elements")]
public static class BraveTheElementsFactory
{
    public const string CardName = "Brave the Elements";
    public const string Slug = "brave-the-elements";
    public const string PrintedManaCost = "{W}";

    public const int ModeWhite = 0;
    public const int ModeBlue = 1;
    public const int ModeBlack = 2;
    public const int ModeRed = 3;
    public const int ModeGreen = 4;

    /// <summary>CR 700.2 — "Choose one —" pick count (the single colour).</summary>
    public const int PickCount = 1;

    /// <summary>Total number of colour modes (WUBRG).</summary>
    public const int TotalModes = 5;

    public const string QualityWhite = "white";
    public const string QualityBlue = "blue";
    public const string QualityBlack = "black";
    public const string QualityRed = "red";
    public const string QualityGreen = "green";

    /// <summary>
    /// The colour choice surfaced as choose-one modes, in WUBRG order. Each
    /// label reads as "protection from &lt;colour&gt;" so the prompt and the bot
    /// classifier see a meaningful clause.
    /// </summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "White creatures you control gain protection from white until end of turn.",
        "White creatures you control gain protection from blue until end of turn.",
        "White creatures you control gain protection from black until end of turn.",
        "White creatures you control gain protection from red until end of turn.",
        "White creatures you control gain protection from green until end of turn.",
    };

    /// <summary>Construct Brave the Elements from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Map a chosen mode index to its protection quality string. Out-of-range
    /// indices fall back to <see cref="QualityWhite"/> (mode 0).
    /// </summary>
    public static string QualityForMode(int modeIndex) => modeIndex switch
    {
        ModeWhite => QualityWhite,
        ModeBlue => QualityBlue,
        ModeBlack => QualityBlack,
        ModeRed => QualityRed,
        ModeGreen => QualityGreen,
        _ => QualityWhite,
    };

    /// <summary>Map a <see cref="ManaColor"/> to its choose-one mode index.</summary>
    public static int ModeForColor(ManaColor color) => color switch
    {
        ManaColor.White => ModeWhite,
        ManaColor.Blue => ModeBlue,
        ManaColor.Black => ModeBlack,
        ManaColor.Red => ModeRed,
        ManaColor.Green => ModeGreen,
        _ => ModeWhite,
    };

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Five choose-one colour
    /// modes, no targets, no X. On resolution, <see cref="Resolve(Player,string)"/>
    /// grants protection-from-the-chosen-colour to every white creature
    /// <paramref name="caster"/> controls until end of turn (CR 702.16 /
    /// CR 514.2).
    /// </summary>
    /// <param name="caster">The player casting Brave the Elements; only THEIR
    /// white creatures are affected (CR 109.2). The caster is captured in the
    /// resolve closure — the same posture as
    /// <see cref="CrashThroughFactory.BuildDefinition"/> for a non-targeted
    /// "creatures you control" mass grant.</param>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Protection,
                BotIntent.Protection,
                BotIntent.Protection,
                BotIntent.Protection,
                BotIntent.Protection,
            },
            EffectFactory: p =>
            {
                // CR 601.2b — the colour is chosen as the spell is cast. Honour
                // either the multi-pick list (first entry for a choose-one card)
                // or the legacy scalar ModeIndex; default to white (mode 0) if
                // neither is present (defensive — the cast flow always supplies
                // a pick for a modal spell).
                var mode = p.ModeIndexes is { Count: > 0 } list
                    ? list[0]
                    : (p.ModeIndex ?? ModeWhite);
                if (mode < 0 || mode >= TotalModes) mode = ModeWhite;

                var quality = QualityForMode(mode);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — white creatures you control gain protection from {quality} until end of turn",
                        () => Resolve(caster, quality)),
                };
            });
    }

    /// <summary>
    /// Grant protection-from-<paramref name="quality"/> until end of turn to
    /// every white creature <paramref name="caster"/> controls on the
    /// battlefield (CR 109.2 / CR 702.16 / CR 514.2). Exposed for direct
    /// invocation by tests / bots without driving the full cast flow.
    /// </summary>
    public static IReadOnlyList<Creature> Resolve(Player caster, string quality)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (string.IsNullOrWhiteSpace(quality)) quality = QualityWhite;

        var granted = new List<Creature>();
        // CR 608.2 — the set of affected creatures is fixed at resolution.
        foreach (var card in caster.Zones.Battlefield.GetCards().ToList())
        {
            if (card is not Creature creature) continue;
            if (creature.Zone != ZoneType.Battlefield) continue;

            // CR 105 / CR 202.2 — "white creatures you control": colour comes
            // from mana-cost pips and any colour indicator.
            if (!CardColors.GetColors(creature).Contains(ManaColor.White)) continue;

            if (creature.ActiveEffects is not null)
            {
                // CR 613.1f — Layer 6 ability grant. Self-sourced (the granted
                // creature is the effect source) so the grant outlives the spell
                // card leaving the stack; expires at end of turn (CR 514.2).
                var grant = new GrantAbilityEffect(
                    source: creature,
                    target: creature,
                    ability: new ProtectionAbility(quality),
                    expiresAtEndOfTurn: true);
                creature.ActiveEffects.Register(grant);
                // Materialise the grant on the same priority window so target /
                // damage / attach legality reads it immediately (CR 700.2a).
                grant.Sync();
            }
            else
            {
                // No layers service wired (shape-only path): attach the marker
                // directly so it is inspectable. EOT cleanup for this path is
                // unavailable without a service — same posture as
                // GiverOfRunesFactory's no-service branch.
                creature.AddAbility(new ProtectionAbility(quality));
            }

            granted.Add(creature);
        }

        return granted;
    }
}
