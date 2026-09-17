namespace Swgoh.Application.Investments;

public interface IInvestmentTargetRepository
{
    Task<InvestmentTarget?> GetAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<InvestmentTarget>> GetAllAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        InvestmentTarget target,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default);
}
