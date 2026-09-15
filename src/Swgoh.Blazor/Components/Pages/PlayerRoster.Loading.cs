using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class PlayerRoster
{
    private CancellationTokenSource? _playerLoadSource;
    private CancellationTokenSource? _rosterRequestSource;
    private long _playerLoadGeneration;
    private long _rosterRequestGeneration;
    private long? _activeAllyCode;
    private bool _disposed;

    protected override async Task OnParametersSetAsync()
    {
        long allyCode = AllyCode;
        (long generation, CancellationToken token) = BeginPlayerLoad(allyCode);

        _loadingProfile = true;
        _error = null;
        _selectedUnit = null;
        _currentGacPlan = null;
        _analysis = null;
        _player = null;
        _roster = null;
        _page = 1;

        _ = LoadAnalysisAsync(allyCode, generation, token);
        _ = LoadCurrentGacContextAsync(allyCode, generation, token);

        await LoadRosterAsync(allyCode, generation, token);
        if (IsCurrentPlayerLoad(generation, allyCode) && _roster is not null)
        {
            _player = new RosterPlayerSummary(
                _roster.AllyCode,
                _roster.PlayerName ?? $"Jugador #{FormatAllyCode(_roster.AllyCode)}",
                _roster.GalacticPower,
                _roster.RosterCount);
        }

        if (IsCurrentPlayerLoad(generation, allyCode))
        {
            _loadingProfile = false;
        }
    }

    private async Task LoadCurrentGacContextAsync(
        long allyCode,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            GacApiClient.CurrentGacResult result = await GacClient
                .GetCurrentOpponentAsync(allyCode, cancellationToken);
            if (IsCurrentPlayerLoad(generation, allyCode))
            {
                _currentGacPlan = result.Scouting?.BattlePlan;
            }
        }
        catch (HttpRequestException) when (IsCurrentPlayerLoad(generation, allyCode))
        {
            _currentGacPlan = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await RenderIfCurrentAsync(generation, allyCode);
        }
    }

    private async Task LoadAnalysisAsync(
        long allyCode,
        long generation,
        CancellationToken cancellationToken)
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
            await RenderIfCurrentAsync(generation, allyCode);
        }
    }

    private async Task LoadFirstPageAsync()
    {
        _page = 1;
        await LoadRosterAsync();
    }

    private async Task ResetFiltersAsync()
    {
        _search = string.Empty;
        _type = "All";
        _faction = string.Empty;
        _minRelic = string.Empty;
        _abilityFilter = "All";
        _orderBy = "GalacticPower";
        _direction = "Descending";
        _page = 1;
        await LoadRosterAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (_page <= 1)
        {
            return;
        }

        _page--;
        await LoadRosterAsync();
    }

    private async Task NextPageAsync()
    {
        if (_roster is null || _page >= _roster.TotalPages)
        {
            return;
        }

        _page++;
        await LoadRosterAsync();
    }

    private Task LoadRosterAsync()
    {
        CancellationTokenSource? source = _playerLoadSource;
        return _activeAllyCode is long allyCode && source is not null
            ? LoadRosterAsync(allyCode, Volatile.Read(ref _playerLoadGeneration), source.Token)
            : Task.CompletedTask;
    }

    private async Task LoadRosterAsync(
        long allyCode,
        long generation,
        CancellationToken parentToken)
    {
        var requestSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _rosterRequestSource, requestSource);
        previous?.Cancel();
        previous?.Dispose();
        long requestGeneration = Interlocked.Increment(ref _rosterRequestGeneration);
        CancellationToken cancellationToken = requestSource.Token;

        if (IsCurrentRosterRequest(generation, allyCode, requestGeneration, requestSource))
        {
            _loadingRoster = true;
            _error = null;
        }

        int page = _page;
        string search = _search;
        string type = _type;
        string faction = _faction;
        string minRelic = _minRelic;
        string abilityFilter = _abilityFilter;
        string orderBy = _orderBy;
        string direction = _direction;

        try
        {
            bool? hasZeta = abilityFilter == "Zeta" ? true : null;
            bool? hasOmicron = abilityFilter == "Omicron" ? true : null;
            PlayerApiClient.RosterPageViewModel? roster = await PlayerClient.GetRosterAsync(
                allyCode,
                page: page,
                pageSize: 24,
                search: search,
                type: type,
                minRelic: ParseOptionalInt(minRelic),
                hasZeta: hasZeta,
                hasOmicron: hasOmicron,
                orderBy: orderBy,
                direction: direction,
                faction: faction,
                cancellationToken: cancellationToken);

            if (IsCurrentRosterRequest(generation, allyCode, requestGeneration, requestSource))
            {
                _roster = roster;
            }
        }
        catch (HttpRequestException) when (
            IsCurrentRosterRequest(generation, allyCode, requestGeneration, requestSource))
        {
            _error = "No se ha podido consultar el roster.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (IsCurrentRosterRequest(generation, allyCode, requestGeneration, requestSource))
            {
                _loadingRoster = false;
            }
        }
    }

    private (long Generation, CancellationToken Token) BeginPlayerLoad(long allyCode)
    {
        CancelRosterRequest();

        var next = new CancellationTokenSource();
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

    private bool IsCurrentRosterRequest(
        long generation,
        long allyCode,
        long requestGeneration,
        CancellationTokenSource source) =>
        IsCurrentPlayerLoad(generation, allyCode) &&
        Volatile.Read(ref _rosterRequestGeneration) == requestGeneration &&
        ReferenceEquals(_rosterRequestSource, source) &&
        !source.IsCancellationRequested;

    private Task RenderIfCurrentAsync(long generation, long allyCode) =>
        IsCurrentPlayerLoad(generation, allyCode)
            ? InvokeAsync(StateHasChanged)
            : Task.CompletedTask;

    private void CancelRosterRequest()
    {
        Interlocked.Increment(ref _rosterRequestGeneration);
        CancellationTokenSource? request = Interlocked.Exchange(ref _rosterRequestSource, null);
        request?.Cancel();
        request?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelRosterRequest();
        _activeAllyCode = null;
        Interlocked.Increment(ref _playerLoadGeneration);
        CancellationTokenSource? playerLoad = Interlocked.Exchange(ref _playerLoadSource, null);
        playerLoad?.Cancel();
        playerLoad?.Dispose();
    }
}