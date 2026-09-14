using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

internal static class GacMatchupRiskEvaluator
{
    public static GacMatchupRiskAssessment Evaluate(
        GacPlannerApiClient.CounterHintViewModel? hint,
        bool defeated)
    {
        if (defeated)
        {
            return new GacMatchupRiskAssessment(
                "complete",
                "Completada",
                "Defensa derrotada",
                4,
                Normalize(hint?.WinRate),
                Normalize(hint?.OneShotRate),
                hint?.Uses);
        }

        decimal? winRate = Normalize(hint?.WinRate);
        decimal? oneShotRate = Normalize(hint?.OneShotRate);
        int? uses = hint?.Uses;

        if (winRate is null)
        {
            return new GacMatchupRiskAssessment(
                "unknown",
                "Sin evaluar",
                hint is null ? "Sin counter histórico" : "Sin histórico suficiente",
                3,
                null,
                oneShotRate,
                uses);
        }

        int level = winRate >= 85m && (oneShotRate is null || oneShotRate >= 70m)
            ? 0
            : winRate >= 70m && (oneShotRate is null || oneShotRate >= 50m)
                ? 1
                : 2;

        if (uses is < 10 && level < 2)
        {
            level++;
        }

        if (hint?.RequiresDatacronVerification == true && level < 2)
        {
            level++;
        }

        return level switch
        {
            0 => new GacMatchupRiskAssessment(
                "low",
                "Riesgo bajo",
                "Counter favorable",
                0,
                winRate,
                oneShotRate,
                uses),
            1 => new GacMatchupRiskAssessment(
                "medium",
                "Riesgo medio",
                hint?.RequiresDatacronVerification == true
                    ? "Verifica datacron antes de atacar"
                    : "Revisa mods y velocidades",
                1,
                winRate,
                oneShotRate,
                uses),
            _ => new GacMatchupRiskAssessment(
                "high",
                "Riesgo alto",
                hint?.RequiresDatacronVerification == true
                    ? "Datacron sensible · ataque delicado"
                    : "Ataque delicado",
                2,
                winRate,
                oneShotRate,
                uses)
        };
    }

    private static decimal? Normalize(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        decimal normalized = value <= 1m ? value.Value * 100m : value.Value;
        return Math.Clamp(normalized, 0m, 100m);
    }
}

internal sealed record GacMatchupRiskAssessment(
    string Tone,
    string Label,
    string Caption,
    int SortRank,
    decimal? WinRate,
    decimal? OneShotRate,
    int? Uses);
