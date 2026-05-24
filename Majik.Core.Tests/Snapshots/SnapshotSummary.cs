using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;

namespace Majik.Core.Tests.Snapshots;

/// <summary>
/// Deterministic JSON serializer for the output of
/// <see cref="Majik.Core.CardData.ScryfallCardFactory"/>. The whole point of
/// snapshot tests is byte-for-byte stability across runs: every list is sorted
/// alphabetically, no GUIDs or timestamps leak through, and counts always
/// precede free-form description lists.
///
/// The companion <see cref="ScryfallCardFactorySnapshotTests"/> diffs the
/// JSON this class produces against the bytes committed in
/// <c>Majik.Core.Tests/Snapshots/snapshots/</c>.
/// </summary>
internal static class SnapshotSummary
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Build a deterministic JSON tree describing the post-binder state of
    /// <paramref name="card"/>. The optional <paramref name="bus"/> exposes
    /// the replacement effects the factory registered for this card (see
    /// <see cref="SummarizeReplacements"/>).
    /// </summary>
    public static JsonObject Build(ICard card, ReplacementBus? bus = null)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));

        var obj = new JsonObject
        {
            ["name"] = card.Name,
            ["manaCost"] = card.ManaCost,
            ["primaryType"] = PrimaryTypeName(card),
            ["cardTypes"] = SortedStrings(card.CardTypes.Select(t => t.ToString())),
            ["supertypes"] = SortedStrings(card.Supertypes.Select(t => t.ToString())),
            ["subtypes"] = SortedStrings(card.Subtypes.Select(t => t.ToString())),
        };

        // P/T live on Creature; planeswalker has Loyalty. Both nullable in
        // the JSON so non-creature/non-planeswalker rows simply omit them.
        if (card is Creature c)
        {
            obj["power"] = c.BasePower;
            obj["toughness"] = c.BaseToughness;
        }
        else
        {
            obj["power"] = null;
            obj["toughness"] = null;
        }

        if (card is Planeswalker pw)
        {
            obj["loyalty"] = pw.StartingLoyalty;
        }
        else
        {
            obj["loyalty"] = null;
        }

        obj["abilities"] = SummarizeAbilities(card);
        obj["replacements"] = SummarizeReplacements(card, bus);

        if (card is Card concrete && concrete.MdfcState is { } mdfc)
        {
            obj["mdfc"] = new JsonObject
            {
                ["frontFace"] = mdfc.FrontFaceName,
                ["backFace"] = mdfc.BackFaceName,
                ["isBackFace"] = mdfc.IsBackFace,
            };
        }
        else
        {
            obj["mdfc"] = null;
        }

        return obj;
    }

    /// <summary>
    /// Stable serialization: sorted lists, indented JSON, UTF-8 newlines.
    /// Returns the exact bytes that go to disk / get compared against the
    /// committed snapshot.
    /// </summary>
    public static string Serialize(JsonObject obj) =>
        // Append a trailing newline for POSIX-friendly diffs.
        obj.ToJsonString(Options) + "\n";

    private static string PrimaryTypeName(ICard card)
    {
        // Mirror ScryfallCardFactory.PickPrimaryType so the snapshot reflects
        // the same routing decision the factory made.
        foreach (var preferred in new[]
        {
            CardType.Land, CardType.Creature, CardType.Planeswalker,
            CardType.Instant, CardType.Sorcery,
            CardType.Enchantment, CardType.Artifact,
        })
        {
            if (card.CardTypes.Contains(preferred)) return preferred.ToString();
        }
        return card.CardTypes.Count > 0 ? card.CardTypes[0].ToString() : "Unknown";
    }

    private static JsonArray SortedStrings(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values.Where(v => !string.IsNullOrEmpty(v))
                                .Distinct(StringComparer.Ordinal)
                                .OrderBy(v => v, StringComparer.Ordinal))
        {
            arr.Add(v);
        }
        return arr;
    }

    private static JsonObject SummarizeAbilities(ICard card)
    {
        var keyword = new List<string>();
        var mana = new List<string>();
        var activated = new List<string>();
        var triggered = new List<string>();
        var staticAbils = new List<string>();
        var other = new List<string>();

        foreach (var ab in card.Abilities)
        {
            switch (ab)
            {
                case KeywordAbility k:
                    keyword.Add(k.Keyword);
                    break;
                case IManaAbility m:
                    mana.Add($"mana:{m.ManaGenerated}");
                    break;
                case TriggeredAbility t:
                    triggered.Add(DescribeTriggered(t));
                    break;
                case ActivatedAbility a:
                    activated.Add(DescribeActivated(a));
                    break;
                case IStaticAbility s:
                    staticAbils.Add($"static:{s.Description}");
                    break;
                default:
                    other.Add(ab.GetType().Name);
                    break;
            }
        }

        return new JsonObject
        {
            ["keyword"] = new JsonObject
            {
                ["count"] = keyword.Count,
                ["list"] = SortedStrings(keyword),
            },
            ["mana"] = new JsonObject
            {
                ["count"] = mana.Count,
                ["list"] = SortedStrings(mana),
            },
            ["activated"] = new JsonObject
            {
                ["count"] = activated.Count,
                ["list"] = SortedStrings(activated),
            },
            ["triggered"] = new JsonObject
            {
                ["count"] = triggered.Count,
                ["list"] = SortedStrings(triggered),
            },
            ["static"] = new JsonObject
            {
                ["count"] = staticAbils.Count,
                ["list"] = SortedStrings(staticAbils),
            },
            ["other"] = new JsonObject
            {
                ["count"] = other.Count,
                ["list"] = SortedStrings(other),
            },
        };
    }

    private static string DescribeTriggered(TriggeredAbility t)
    {
        var condType = t.Condition?.EventType?.Name ?? "Unknown";
        var effects = string.Join("+", t.Effects.Select(e => e.GetType().Name)
                                                .OrderBy(s => s, StringComparer.Ordinal));
        if (string.IsNullOrEmpty(effects)) effects = "no-effects";
        return $"trig:{condType}=>{effects}";
    }

    private static string DescribeActivated(ActivatedAbility a)
    {
        var costs = string.Join("+", a.Costs.Select(c => c.GetType().Name)
                                            .OrderBy(s => s, StringComparer.Ordinal));
        var effects = string.Join("+", a.Effects.Select(e => e.GetType().Name)
                                                .OrderBy(s => s, StringComparer.Ordinal));
        if (string.IsNullOrEmpty(costs)) costs = "no-costs";
        if (string.IsNullOrEmpty(effects)) effects = "no-effects";
        return $"act:{costs}=>{effects}";
    }

    /// <summary>
    /// Count + describe the replacement effects the binders pushed onto the
    /// supplied <see cref="ReplacementBus"/> for this specific card. The bus
    /// keeps its registrations in a private field; reflection is the
    /// least-invasive way to introspect them for snapshotting. Production
    /// code is not affected — this lives in the test assembly only.
    /// </summary>
    private static JsonObject SummarizeReplacements(ICard card, ReplacementBus? bus)
    {
        var summary = new JsonObject { ["count"] = 0, ["list"] = new JsonArray() };
        if (bus == null) return summary;

        var field = typeof(ReplacementBus).GetField(
            "_effects", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(bus) is not IEnumerable<object> raw) return summary;

        var names = new List<string>();
        foreach (var effect in raw)
        {
            if (!BelongsToCard(effect, card)) continue;
            names.Add(effect.GetType().Name);
        }

        summary["count"] = names.Count;
        summary["list"] = SortedStrings(names);
        return summary;
    }

    /// <summary>
    /// Replacement effects don't share a common Source accessor, but the
    /// production binders all give their effects a private field holding the
    /// owning card. Look for a field whose value is <paramref name="card"/>
    /// — false positives are acceptable here since the snapshot is built
    /// during the same factory call that registered the effect.
    /// </summary>
    private static bool BelongsToCard(object effect, ICard card)
    {
        if (effect == null) return false;
        var type = effect.GetType();
        foreach (var f in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance |
                                         BindingFlags.Public))
        {
            var v = f.GetValue(effect);
            if (ReferenceEquals(v, card)) return true;
        }
        return false;
    }
}
