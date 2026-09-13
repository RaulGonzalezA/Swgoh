namespace Swgoh.Blazor.State;

public sealed class PlayerSessionState
{
    public long? AllyCode { get; private set; }
    public string? PlayerName { get; private set; }

    public event Action? Changed;

    public void SetPlayer(long allyCode, string? playerName)
    {
        AllyCode = allyCode;
        PlayerName = string.IsNullOrWhiteSpace(playerName) ? null : playerName.Trim();
        Changed?.Invoke();
    }

    public void Clear()
    {
        AllyCode = null;
        PlayerName = null;
        Changed?.Invoke();
    }
}
