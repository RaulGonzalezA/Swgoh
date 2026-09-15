using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class Home
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _playerLoadSource;
    private long _playerLoadGeneration;
    private long? _activeAllyCode;
    private bool _disposed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _player is not null || _searched)
        {
            return;
        }

        if (await PlayerContext.RestoreAsync() && Session.AllyCode is long allyCode)
        {
            _allyCode = allyCode.ToString();
            await LoadPlayerAsync(allyCode, persistPreference: false);
            await RenderProgressAsync();
        }
    }

    private async Task LoadAsync()
    {
        string normalized = _allyCode
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!long.TryParse(normalized, out long allyCode) || normalized.Length != 9)
        {
            _error = "Introduce un Ally Code válido de 9 dígitos.";
            return;
        }

        await LoadPlayerAsync(allyCode, persistPreference: true);
    }

    private async Task LoadPlayerAsync(long allyCode, bool persistPreference)
    {
        (long generation, CancellationToken token) = BeginPlayerLoad(allyCode);
        _loading = true;
        _searched = true;
        _error = null;
        _analysis = null;
        _planner = null;
        _gacOptimization = null;
        _conquest = null;
        _conquestOptimization = null;
        _dailyPlan = null;

        try
        {
            bool activated = persistPreference
                ? await PlayerContext.ConnectAsync(allyCode, token)
                : await PlayerContext.ActivateAsync(allyCode, persistPreference: false, token);

            if (!IsCurrentPlayerLoad(generation, allyCode))
            {
                return;
            }

            if (!activated)
            {
                _player = null;
                _error = "No se ha encontrado el perfil. Comprueba el Ally Code y vuelve a intentarlo.";
                return;
            }

            PlayerApiClient.PlayerViewModel? player = await PlayerClient.GetAsync(allyCode, token);
            if (!IsCurrentPlayerLoad(generation, allyCode))
            {
                return;
            }

            _player = player;
            if (player is null)
            {
                _error = "El perfil activo no está disponible en la API.";
                return;
            }

            _showPlayerPicker = false;
            _gacLoading = true;
            _gacMessage = null;
            await RenderProgressAsync();
            _ = LoadAnalysisAsync(allyCode, generation, token);
            _ = LoadGacAsync(allyCode, generation, token);
            _ = LoadConquestAsync(allyCode, generation, token);
        }
        catch (HttpRequestException exception) when (
            IsCurrentPlayerLoad(generation, allyCode) &&
            exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _error = "Espera 30 segundos antes de volver a importar este perfil.";
        }
        catch (HttpRequestException exception) when (
            IsCurrentPlayerLoad(generation, allyCode) &&
            exception.StatusCode == System.Net.HttpStatusCode.BadGateway)
        {
            _error = "No se ha podido importar el perfil desde SWGOH. Inténtalo de nuevo en unos momentos.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _error = "La conexión ha tardado demasiado. Inténtalo de nuevo en unos momentos.";
        }
        catch (HttpRequestException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _error = "La API no está disponible en este momento. Revisa que AppHost y Swgoh.Api estén arrancados.";
        }
        finally
        {
            if (IsCurrentPlayerLoad(generation, allyCode))
            {
                _loading = false;
            }
        }
    }

    private async Task LoadGacAsync(long allyCode, long generation, CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerApiClient.PlannerResult plannerResult = await PlannerClient
                .GetCurrentAsync(allyCode, cancellationToken);
            if (!IsCurrentPlayerLoad(generation, allyCode))
            {
                return;
            }

            if (plannerResult.Planner is null)
            {
                _planner = null;
                _gacOptimization = null;
                _gacMessage = plannerResult.Message;
                return;
            }

            GacPlannerApiClient.OptimizationResult optimization = await PlannerClient.OptimizeCurrentAsync(
                allyCode,
                "FillGaps",
                apply: false,
                cancellationToken: cancellationToken);
            if (!IsCurrentPlayerLoad(generation, allyCode))
            {
                return;
            }

            _planner = plannerResult.Planner;
            _gacMessage = plannerResult.Message;
            _gacOptimization = optimization.Envelope?.Optimization;
        }
        catch (HttpRequestException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _planner = null;
            _gacOptimization = null;
            _gacMessage = "No se ha podido consultar Gran Arena. El resto de tus datos sigue disponible.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _gacMessage = "La búsqueda de Gran Arena ha agotado su tiempo. Puedes volver a intentarlo más tarde.";
        }
        catch (Polly.Timeout.TimeoutRejectedException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _gacMessage = "Gran Arena está tardando demasiado. El resto de tus datos sigue disponible.";
        }
        finally
        {
            if (IsCurrentPlayerLoad(generation, allyCode))
            {
                _gacLoading = false;
                await RenderProgressAsync();
            }
        }
    }

    private async Task LoadConquestAsync(long allyCode, long generation, CancellationToken cancellationToken)
    {
        try
        {
            ConquestApiClient.PlanViewModel? conquest = await ConquestClient
                .GetCurrentAsync(allyCode, cancellationToken);
            if (conquest is null)
            {
                if (IsCurrentPlayerLoad(generation, allyCode))
                {
                    _conquest = null;
                    _conquestOptimization = null;
                    _dailyPlan = null;
                }

                return;
            }

            Task<ConquestApiClient.OptimizationViewModel?> optimizationTask =
                ConquestClient.OptimizeCurrentAsync(allyCode, cancellationToken);
            Task<ConquestDailyPlanApiClient.DailyPlanViewModel?> dailyPlanTask =
                DailyPlanClient.BuildAsync(allyCode, maxBattles: 6, cancellationToken);
            await Task.WhenAll(optimizationTask, dailyPlanTask);
            if (!IsCurrentPlayerLoad(generation, allyCode))
            {
                return;
            }

            _conquest = conquest;
            _conquestOptimization = await optimizationTask;
            _dailyPlan = await dailyPlanTask;
        }
        catch (HttpRequestException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _conquest = null;
            _conquestOptimization = null;
            _dailyPlan = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (IsCurrentPlayerLoad(generation, allyCode))
            {
                await RenderProgressAsync();
            }
        }
    }

    private async Task LoadAnalysisAsync(long allyCode, long generation, CancellationToken cancellationToken)
    {
        try
        {
            PlayerApiClient.PlayerRosterAnalysisViewModel? analysis = await PlayerClient
                .GetAnalysisAsync(allyCode, cancellationToken);
            if (IsCurrentPlayerLoad(generation, allyCode))
            {
                _analysis = analysis;
            }
        }
        catch (HttpRequestException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _analysis = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (IsCurrentPlayerLoad(generation, allyCode))
            {
                await RenderProgressAsync();
            }
        }
    }

    private (long Generation, CancellationToken Token) BeginPlayerLoad(long allyCode)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _playerLoadSource, next);
        previous?.Cancel();
        previous?.Dispose();

        _activeAllyCode = allyCode;
        long generation = Interlocked.Increment(ref _playerLoadGeneration);
        return (generation, next.Token);
    }

    private bool IsCurrentPlayerLoad(long generation, long allyCode) =>
        !_disposed &&
        Volatile.Read(ref _playerLoadGeneration) == generation &&
        _activeAllyCode == allyCode &&
        _playerLoadSource is { IsCancellationRequested: false };

    private void CancelPlayerLoad()
    {
        _activeAllyCode = null;
        Interlocked.Increment(ref _playerLoadGeneration);
        CancellationTokenSource? current = Interlocked.Exchange(ref _playerLoadSource, null);
        current?.Cancel();
        current?.Dispose();
    }

    private Task RenderProgressAsync() => _disposed ? Task.CompletedTask : InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPlayerLoad();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
