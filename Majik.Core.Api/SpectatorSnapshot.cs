using System.Text.Json;
using Majik.Core.Api.Dtos;

namespace Majik.Core.Api;

/// <summary>
/// Read-only snapshot save/load. Serializes a <see cref="GameStateDto"/> to
/// JSON bytes so a frontend can persist a game (or pass it around for
/// inspection / replay). Loaded snapshots are read-only — they cannot resume
/// the live game loop (that requires the full domain model, which is
/// reconstructed in a later phase if/when needed).
/// </summary>
public static class SpectatorSnapshot
{
    public static GameStateDto Load(byte[] blob)
    {
        if (blob == null) throw new ArgumentNullException(nameof(blob));

        return JsonSerializer.Deserialize<GameStateDto>(blob)
            ?? throw new InvalidOperationException("Snapshot deserialized as null.");
    }
}
