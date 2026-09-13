using Microsoft.JSInterop;

using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.State;

public sealed class PlayerContextService(
    PlayerApiClient playerClient,
    PlayerPreferenceService preferences,
    PlayerSessionState session)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (session.AllyCode is not null)
        {
            return true;
        }

        try
        {
            long? allyCode = await preferences.GetAllyCodeAsync(cancellationToken);
            return allyCode is long value && await ActivateAsync(value, persistPreference: false, cancellationToken);
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task<bool> ActivateAsync(
        long allyCode,
        bool persistPreference = true,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (session.AllyCode == allyCode && !string.IsNullOrWhiteSpace(session.PlayerName))
            {
                if (persistPreference)
                {
                    await TryPersistAsync(allyCode, cancellationToken);
                }

                return true;
            }

            PlayerApiClient.PlayerViewModel? player = await playerClient.GetAsync(allyCode, cancellationToken);
            if (player is null)
            {
                return false;
            }

            session.SetPlayer(player.AllyCode, player.Name);
            if (persistPreference)
            {
                await TryPersistAsync(player.AllyCode, cancellationToken);
            }

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        session.Clear();
        try
        {
            await preferences.ClearAsync(cancellationToken);
        }
        catch (JSException)
        {
            // Browser persistence is optional; clearing the in-memory context is enough.
        }
    }

    private async Task TryPersistAsync(long allyCode, CancellationToken cancellationToken)
    {
        try
        {
            await preferences.SetAllyCodeAsync(allyCode, cancellationToken);
        }
        catch (JSException)
        {
            // The active player still works for the current session when storage is unavailable.
        }
    }
}
