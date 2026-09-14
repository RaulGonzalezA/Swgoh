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
        if (updatedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

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
        if (availableEnergy is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableEnergy),
                availableEnergy,
                "Available Conquest energy cannot be negative.");
        }

        if (energyCostPerBattle is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(energyCostPerBattle),
                energyCostPerBattle,
                "Energy cost per battle must be between 1 and 1000.");
        }

        if (currentRewardPoints < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRewardPoints),
                currentRewardPoints,
                "Current reward points cannot be negative.");
        }

        if (targetRewardPoints is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetRewardPoints),
                targetRewardPoints,
                "Target reward points cannot be negative.");
        }

        AvailableEnergy = availableEnergy;
        EnergyCostPerBattle = energyCostPerBattle;
        CurrentRewardPoints = currentRewardPoints;
        TargetRewardPoints = targetRewardPoints;
        RewardTargetName = string.IsNullOrWhiteSpace(rewardTargetName) ? null : rewardTargetName.Trim();
    }
}
