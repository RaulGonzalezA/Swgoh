namespace Swgoh.Domain.Conquest;

public sealed partial class ConquestPlan
{
    public const int DefaultEnergyCostPerBattle = 20;

    public int? AvailableEnergy { get; private set; }
    public int EnergyCostPerBattle { get; private set; } = DefaultEnergyCostPerBattle;
    public int CurrentRewardPoints { get; private set; }
    public int? TargetRewardPoints { get; private set; }
    public string? RewardTargetName { get; private set; }

    public bool HasRewardTarget => TargetRewardPoints is not null;
    public bool RewardTargetReached => TargetRewardPoints is int target && CurrentRewardPoints >= target;

    public void ReplaceDailyGoal(
        int? availableEnergy,
        int energyCostPerBattle,
        int currentRewardPoints,
        int? targetRewardPoints,
        string? rewardTargetName,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(updatedAtUtc, CreatedAtUtc);

        ApplyDailyGoal(
            availableEnergy,
            energyCostPerBattle,
            currentRewardPoints,
            targetRewardPoints,
            rewardTargetName);
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RestoreDailyGoal(
        int? availableEnergy,
        int energyCostPerBattle,
        int currentRewardPoints,
        int? targetRewardPoints,
        string? rewardTargetName) => ApplyDailyGoal(
            availableEnergy,
            energyCostPerBattle,
            currentRewardPoints,
            targetRewardPoints,
            rewardTargetName);

    private void ApplyDailyGoal(
        int? availableEnergy,
        int energyCostPerBattle,
        int currentRewardPoints,
        int? targetRewardPoints,
        string? rewardTargetName)
    {
        if (availableEnergy is int energy)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(energy, nameof(availableEnergy));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(energyCostPerBattle, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(energyCostPerBattle, 1_000);
        ArgumentOutOfRangeException.ThrowIfNegative(currentRewardPoints);

        if (targetRewardPoints is int target)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(targetRewardPoints));
        }

        AvailableEnergy = availableEnergy;
        EnergyCostPerBattle = energyCostPerBattle;
        CurrentRewardPoints = currentRewardPoints;
        TargetRewardPoints = targetRewardPoints;
        RewardTargetName = string.IsNullOrWhiteSpace(rewardTargetName) ? null : rewardTargetName.Trim();
    }
}
