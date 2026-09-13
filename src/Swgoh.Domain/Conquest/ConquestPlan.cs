namespace Swgoh.Domain.Conquest;

public enum ConquestDifficulty
{
    Easy = 1,
    Normal = 2,
    Hard = 3
}

public enum ConquestFeatScope
{
    Global = 1,
    Sector = 2,
    Boss = 3
}

public enum ConquestFeatRuleType
{
    AnyCharacter = 1,
    Faction = 2,
    SpecificUnits = 3
}

public sealed record ConquestUnitStamina(string DefinitionId, int CurrentPercent)
{
    public static ConquestUnitStamina Create(string definitionId, int currentPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        if (currentPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentPercent),
                currentPercent,
                "Stamina must be between 0 and 100 percent.");
        }

        return new ConquestUnitStamina(definitionId.Trim(), currentPercent);
    }
}

public sealed record ConquestFeatRule(
    ConquestFeatRuleType Type,
    string? Faction,
    IReadOnlyCollection<string> UnitDefinitionIds,
    int MinimumMatchingUnits)
{
    public static ConquestFeatRule Create(
        ConquestFeatRuleType type,
        string? faction,
        IEnumerable<string>? unitDefinitionIds,
        int minimumMatchingUnits)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        string? normalizedFaction = string.IsNullOrWhiteSpace(faction) ? null : faction.Trim();
        string[] units =
        [
            .. (unitDefinitionIds ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        if (minimumMatchingUnits is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumMatchingUnits),
                minimumMatchingUnits,
                "Minimum matching units must be between 1 and 5.");
        }

        if (type == ConquestFeatRuleType.Faction && normalizedFaction is null)
        {
            throw new ArgumentException("Faction feats require a faction.", nameof(faction));
        }

        if (type == ConquestFeatRuleType.SpecificUnits && units.Length == 0)
        {
            throw new ArgumentException("Specific-unit feats require at least one unit.", nameof(unitDefinitionIds));
        }

        if (type == ConquestFeatRuleType.SpecificUnits && minimumMatchingUnits > units.Length)
        {
            throw new ArgumentException(
                "Minimum matching units cannot exceed the number of configured units.",
                nameof(minimumMatchingUnits));
        }

        return new ConquestFeatRule(type, normalizedFaction, units, minimumMatchingUnits);
    }
}

public sealed record ConquestFeat(
    Guid Id,
    string Name,
    ConquestFeatScope Scope,
    int? Sector,
    int Points,
    int Target,
    int Progress,
    int ExpectedProgressPerBattle,
    ConquestFeatRule Rule)
{
    public int Remaining => Math.Max(0, Target - Progress);
    public bool IsComplete => Remaining == 0;

    public static ConquestFeat Create(
        Guid id,
        string name,
        ConquestFeatScope scope,
        int? sector,
        int points,
        int target,
        int progress,
        int expectedProgressPerBattle,
        ConquestFeatRule rule)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Feat ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (scope == ConquestFeatScope.Global && sector is not null)
        {
            throw new ArgumentException("Global feats cannot belong to a sector.", nameof(sector));
        }

        if (scope != ConquestFeatScope.Global && sector is not (>= 1 and <= 5))
        {
            throw new ArgumentOutOfRangeException(nameof(sector), sector, "Sector must be between 1 and 5.");
        }

        if (points < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(points));
        }

        if (target < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        if (progress < 0 || progress > target)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }

        if (expectedProgressPerBattle < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedProgressPerBattle));
        }

        ArgumentNullException.ThrowIfNull(rule);
        return new ConquestFeat(
            id,
            name.Trim(),
            scope,
            sector,
            points,
            target,
            progress,
            expectedProgressPerBattle,
            rule);
    }
}

public sealed class ConquestPlan
{
    public const int DefaultStaminaCostPerBattle = 10;
    public const int DefaultReserveFloorPercent = 40;

    private readonly List<ConquestFeat> feats;
    private readonly List<ConquestUnitStamina> stamina;

    private ConquestPlan(
        string id,
        long allyCode,
        string eventId,
        string name,
        ConquestDifficulty difficulty,
        IEnumerable<ConquestFeat> feats,
        int staminaCostPerBattle,
        int reserveFloorPercent,
        IEnumerable<ConquestUnitStamina> stamina,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        AllyCode = allyCode;
        EventId = eventId;
        Name = name;
        Difficulty = difficulty;
        this.feats = [.. feats];
        StaminaCostPerBattle = staminaCostPerBattle;
        ReserveFloorPercent = reserveFloorPercent;
        this.stamina = [.. stamina];
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Id { get; }
    public long AllyCode { get; }
    public string EventId { get; }
    public string Name { get; private set; }
    public ConquestDifficulty Difficulty { get; private set; }
    public IReadOnlyList<ConquestFeat> Feats => feats;
    public int StaminaCostPerBattle { get; private set; }
    public int ReserveFloorPercent { get; private set; }
    public IReadOnlyList<ConquestUnitStamina> Stamina => stamina;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public int GetCurrentStamina(string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        return stamina.FirstOrDefault(value => string.Equals(
            value.DefinitionId,
            definitionId.Trim(),
            StringComparison.OrdinalIgnoreCase))?.CurrentPercent ?? 100;
    }

    public static ConquestPlan Create(
        long allyCode,
        string eventId,
        string name,
        ConquestDifficulty difficulty,
        IEnumerable<ConquestFeat> feats,
        DateTimeOffset createdAtUtc,
        int staminaCostPerBattle = DefaultStaminaCostPerBattle,
        int reserveFloorPercent = DefaultReserveFloorPercent,
        IEnumerable<ConquestUnitStamina>? stamina = null)
    {
        ValidateIdentity(allyCode, eventId, name, difficulty);
        ConquestFeat[] normalizedFeats = ValidateFeats(feats);
        ValidateStaminaSettings(staminaCostPerBattle, reserveFloorPercent);
        ConquestUnitStamina[] normalizedStamina = ValidateStamina(stamina ?? []);
        return new ConquestPlan(
            BuildId(allyCode, eventId),
            allyCode,
            eventId.Trim(),
            name.Trim(),
            difficulty,
            normalizedFeats,
            staminaCostPerBattle,
            reserveFloorPercent,
            normalizedStamina,
            createdAtUtc,
            createdAtUtc);
    }

    public static ConquestPlan Restore(
        long allyCode,
        string eventId,
        string name,
        ConquestDifficulty difficulty,
        IEnumerable<ConquestFeat> feats,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        int staminaCostPerBattle = DefaultStaminaCostPerBattle,
        int reserveFloorPercent = DefaultReserveFloorPercent,
        IEnumerable<ConquestUnitStamina>? stamina = null)
    {
        ConquestPlan plan = Create(
            allyCode,
            eventId,
            name,
            difficulty,
            feats,
            createdAtUtc,
            staminaCostPerBattle,
            reserveFloorPercent,
            stamina);
        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

        plan.UpdatedAtUtc = updatedAtUtc;
        return plan;
    }

    public void Replace(
        string name,
        ConquestDifficulty difficulty,
        IEnumerable<ConquestFeat> newFeats,
        DateTimeOffset updatedAtUtc) => Replace(
            name,
            difficulty,
            newFeats,
            StaminaCostPerBattle,
            ReserveFloorPercent,
            stamina,
            updatedAtUtc);

    public void Replace(
        string name,
        ConquestDifficulty difficulty,
        IEnumerable<ConquestFeat> newFeats,
        int staminaCostPerBattle,
        int reserveFloorPercent,
        IEnumerable<ConquestUnitStamina> newStamina,
        DateTimeOffset updatedAtUtc)
    {
        ValidateIdentity(AllyCode, EventId, name, difficulty);
        if (updatedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

        ConquestFeat[] normalizedFeats = ValidateFeats(newFeats);
        ValidateStaminaSettings(staminaCostPerBattle, reserveFloorPercent);
        ConquestUnitStamina[] normalizedStamina = ValidateStamina(newStamina);
        Name = name.Trim();
        Difficulty = difficulty;
        feats.Clear();
        feats.AddRange(normalizedFeats);
        StaminaCostPerBattle = staminaCostPerBattle;
        ReserveFloorPercent = reserveFloorPercent;
        stamina.Clear();
        stamina.AddRange(normalizedStamina);
        UpdatedAtUtc = updatedAtUtc;
    }

    public static string BuildId(long allyCode, string eventId)
    {
        ValidateAllyCode(allyCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        return $"{allyCode}:{eventId.Trim()}";
    }

    private static void ValidateIdentity(
        long allyCode,
        string eventId,
        string name,
        ConquestDifficulty difficulty)
    {
        ValidateAllyCode(allyCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(difficulty))
        {
            throw new ArgumentOutOfRangeException(nameof(difficulty));
        }
    }

    private static ConquestFeat[] ValidateFeats(IEnumerable<ConquestFeat> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ConquestFeat[] values = [.. source];
        if (values.Select(feat => feat.Id).Distinct().Count() != values.Length)
        {
            throw new ArgumentException("Feat IDs must be unique.", nameof(source));
        }

        return values;
    }

    private static ConquestUnitStamina[] ValidateStamina(IEnumerable<ConquestUnitStamina> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ConquestUnitStamina[] values =
        [
            .. source.Select(value => ConquestUnitStamina.Create(value.DefinitionId, value.CurrentPercent))
        ];
        if (values.Select(value => value.DefinitionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
        {
            throw new ArgumentException("Stamina unit IDs must be unique.", nameof(source));
        }

        return values;
    }

    private static void ValidateStaminaSettings(int staminaCostPerBattle, int reserveFloorPercent)
    {
        if (staminaCostPerBattle is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staminaCostPerBattle),
                staminaCostPerBattle,
                "Stamina cost per battle must be between 1 and 100 percent.");
        }

        if (reserveFloorPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reserveFloorPercent),
                reserveFloorPercent,
                "Reserve floor must be between 0 and 100 percent.");
        }
    }

    private static void ValidateAllyCode(long allyCode)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }
    }
}
