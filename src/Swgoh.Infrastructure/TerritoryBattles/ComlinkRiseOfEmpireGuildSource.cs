using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using Swgoh.Application.TerritoryBattles;

namespace Swgoh.Infrastructure.TerritoryBattles;

internal sealed class ComlinkRiseOfEmpireGuildSource(HttpClient httpClient) : IRiseOfEmpireGuildSource
{
    internal const int MaxResolveParallelism = 6;

    public async Task<RiseOfEmpireGuildSnapshot> GetAsync(
        string guildId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "guild",
            new
            {
                payload = new
                {
                    guildId = guildId.Trim(),
                    includeRecentGuildActivityInfo = false
                },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        JsonElement profile = root.TryGetProperty("profile", out JsonElement profileElement)
            ? profileElement
            : root;
        string resolvedGuildId = ReadString(profile, "id") ?? guildId.Trim();
        string guildName = ReadString(profile, "name") ?? "Guild";
        MemberDraft[] drafts = ParseMembers(root);
        var warnings = new ConcurrentBag<string>();
        var members = new ConcurrentBag<RiseOfEmpireGuildMemberReference>();
        using var semaphore = new SemaphoreSlim(MaxResolveParallelism, MaxResolveParallelism);

        Task[] tasks =
        [
            .. drafts.Select(async draft =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ResolvedMember? resolved = draft.AllyCode is long allyCode
                        ? new ResolvedMember(allyCode, draft.PlayerName, draft.GalacticPower)
                        : await ResolveMemberAsync(draft, cancellationToken).ConfigureAwait(false);
                    if (resolved is null || resolved.AllyCode is < 100_000_000 or > 999_999_999)
                    {
                        warnings.Add($"No se pudo resolver el ally code de {draft.PlayerName}.");
                        return;
                    }

                    members.Add(new RiseOfEmpireGuildMemberReference(
                        draft.PlayerId,
                        resolved.AllyCode,
                        string.IsNullOrWhiteSpace(draft.PlayerName) ? resolved.PlayerName : draft.PlayerName,
                        draft.GalacticPower > 0 ? draft.GalacticPower : resolved.GalacticPower));
                }
                catch (HttpRequestException)
                {
                    warnings.Add($"Comlink no pudo resolver el miembro {draft.PlayerName}.");
                }
                catch (JsonException)
                {
                    warnings.Add($"Comlink devolvió datos inválidos para {draft.PlayerName}.");
                }
                finally
                {
                    semaphore.Release();
                }
            })
        ];
        await Task.WhenAll(tasks).ConfigureAwait(false);

        RiseOfEmpireGuildMemberReference[] ordered =
        [
            .. members
                .GroupBy(member => member.AllyCode)
                .Select(group => group.First())
                .OrderByDescending(member => member.GalacticPower)
                .ThenBy(member => member.PlayerName, StringComparer.OrdinalIgnoreCase)
        ];
        long guildGalacticPower = ReadLong(profile, "galacticPower")
            ?? ReadLong(profile, "guildGalacticPower")
            ?? drafts.Sum(member => member.GalacticPower);

        return new RiseOfEmpireGuildSnapshot(
            resolvedGuildId,
            guildName,
            guildGalacticPower,
            ordered,
            [.. warnings.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)]);
    }

    private async Task<ResolvedMember?> ResolveMemberAsync(
        MemberDraft member,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(member.PlayerId))
        {
            return null;
        }

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "player",
            new { payload = new { playerId = member.PlayerId }, enums = false },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement player = document.RootElement;
        long? allyCode = ReadLong(player, "allyCode");
        return allyCode is null
            ? null
            : new ResolvedMember(
                allyCode.Value,
                ReadString(player, "name") ?? member.PlayerName,
                ReadLong(player, "galacticPower") ?? member.GalacticPower);
    }

    private static MemberDraft[] ParseMembers(JsonElement root)
    {
        if (!root.TryGetProperty("member", out JsonElement members)
            && !root.TryGetProperty("members", out members))
        {
            return [];
        }

        if (members.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. members.EnumerateArray()
                .Select(member => new MemberDraft(
                    ReadString(member, "playerId") ?? string.Empty,
                    ReadString(member, "playerName") ?? ReadString(member, "name") ?? "Miembro",
                    ReadLong(member, "galacticPower") ?? 0,
                    ReadLong(member, "allyCode")))
                .Where(member => !string.IsNullOrWhiteSpace(member.PlayerId) || member.AllyCode is not null)
        ];
    }

    private static string? ReadString(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static long? ReadLong(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long numeric))
        {
            return numeric;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out long parsed)
            ? parsed
            : null;
    }

    private sealed record MemberDraft(
        string PlayerId,
        string PlayerName,
        long GalacticPower,
        long? AllyCode);

    private sealed record ResolvedMember(long AllyCode, string PlayerName, long GalacticPower);
}
