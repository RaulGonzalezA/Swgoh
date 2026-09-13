namespace Swgoh.Domain.Gac;

public sealed record GacPersonalBattleObservation(
    string Id,
    long PlayerAllyCode,
    long OpponentAllyCode,
    string EventInstanceId,
    int RoundNumber,
    GacFormat Format,
    Guid AttackId,
    Guid DefenseId,
    int Attempt,
    bool IsFleet,
    IReadOnlyCollection<string> AttackerDefinitionIds,
    IReadOnlyCollection<string> DefenderDefinitionIds,
    bool Won,
    int? Banners,
    DateTimeOffset RecordedAtUtc)
{
    public static GacPersonalBattleObservation Create(
        long playerAllyCode,
        long opponentAllyCode,
        string eventInstanceId,
        int roundNumber,
        GacFormat format,
        Guid attackId,
        Guid defenseId,
        int attempt,
        bool isFleet,
        IEnumerable<string> attackerDefinitionIds,
        IEnumerable<string> defenderDefinitionIds,
        bool won,
        int? banners,
        DateTimeOffset recordedAtUtc)
    {
        ValidateAllyCode(playerAllyCode, nameof(playerAllyCode));
        ValidateAllyCode(opponentAllyCode, nameof(opponentAllyCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(eventInstanceId);
        if (roundNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roundNumber));
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if (attackId == Guid.Empty)
        {
            throw new ArgumentException("Attack ID cannot be empty.", nameof(attackId));
        }

        if (defenseId == Guid.Empty)
        {
            throw new ArgumentException("Defense ID cannot be empty.", nameof(defenseId));
        }

        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        if (banners is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(banners), banners, "Banners must be between 0 and 100.");
        }

        string[] attackers = NormalizeSquad(attackerDefinitionIds, nameof(attackerDefinitionIds));
        string[] defenders = NormalizeSquad(defenderDefinitionIds, nameof(defenderDefinitionIds));
        ValidateSquadSize(format, isFleet, attackers, nameof(attackerDefinitionIds));
        ValidateSquadSize(format, isFleet, defenders, nameof(defenderDefinitionIds));

        string normalizedEventInstanceId = eventInstanceId.Trim();
        return new GacPersonalBattleObservation(
            BuildId(playerAllyCode, normalizedEventInstanceId, roundNumber, attackId),
            playerAllyCode,
            opponentAllyCode,
            normalizedEventInstanceId,
            roundNumber,
            format,
            attackId,
            defenseId,
            attempt,
            isFleet,
            attackers,
            defenders,
            won,
            banners,
            recordedAtUtc);
    }

    public static string BuildId(long playerAllyCode, string eventInstanceId, int roundNumber, Guid attackId)
    {
        ValidateAllyCode(playerAllyCode, nameof(playerAllyCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(eventInstanceId);
        if (roundNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roundNumber));
        }

        if (attackId == Guid.Empty)
        {
            throw new ArgumentException("Attack ID cannot be empty.", nameof(attackId));
        }

        return $"{playerAllyCode}:{eventInstanceId.Trim()}:{roundNumber}:{attackId:D}";
    }

    public static string BuildMatchupKey(
        GacFormat format,
        bool isFleet,
        IEnumerable<string> attackerDefinitionIds,
        IEnumerable<string> defenderDefinitionIds)
    {
        string[] attackers = NormalizeSquad(attackerDefinitionIds, nameof(attackerDefinitionIds));
        string[] defenders = NormalizeSquad(defenderDefinitionIds, nameof(defenderDefinitionIds));
        return $"{(int)format}:{isFleet}:{string.Join(',', attackers)}>{string.Join(',', defenders)}";
    }

    public string MatchupKey => BuildMatchupKey(Format, IsFleet, AttackerDefinitionIds, DefenderDefinitionIds);

    private static string[] NormalizeSquad(IEnumerable<string> definitionIds, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        string[] values =
        [
            .. definitionIds.Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Unit definition IDs cannot be empty.", parameterName)
                : value.Trim().ToUpperInvariant())
                .OrderBy(value => value, StringComparer.Ordinal)
        ];
        if (values.Length == 0)
        {
            throw new ArgumentException("A squad must contain at least one unit.", parameterName);
        }

        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
        {
            throw new ArgumentException("A squad cannot contain duplicate units.", parameterName);
        }

        return values;
    }

    private static void ValidateSquadSize(GacFormat format, bool isFleet, IReadOnlyCollection<string> units, string parameterName)
    {
        if (isFleet)
        {
            if (units.Count is < 2 or > 8)
            {
                throw new ArgumentException("A fleet observation must contain between 2 and 8 units.", parameterName);
            }

            return;
        }

        if (units.Count != (int)format)
        {
            throw new ArgumentException($"A character observation must contain exactly {(int)format} units.", parameterName);
        }
    }

    private static void ValidateAllyCode(long allyCode, string parameterName)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(parameterName, allyCode, "Ally code must contain exactly nine digits.");
        }
    }
}
