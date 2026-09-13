namespace Swgoh.Application.Gac;

internal sealed record GacPersonalLearningSignal(
    decimal Adjustment,
    int Samples,
    int Wins,
    int Failures,
    decimal? WinRate,
    decimal? OneShotRate,
    decimal? AverageBanners,
    string Scope,
    string Summary)
{
    public static GacPersonalLearningSignal None { get; } = new(
        0m,
        0,
        0,
        0,
        null,
        null,
        null,
        "None",
        "Sin histórico personal suficiente para ajustar este counter.");
}
