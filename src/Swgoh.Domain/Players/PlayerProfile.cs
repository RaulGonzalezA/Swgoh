namespace Swgoh.Domain.Players;

public sealed class PlayerProfile
{
    private PlayerProfile(long allyCode, string name, long galacticPower, DateTimeOffset updatedAtUtc)
    {
        AllyCode = allyCode;
        Name = name;
        GalacticPower = galacticPower;
        UpdatedAtUtc = updatedAtUtc;
    }

    public long AllyCode { get; private set; }
    public string Name { get; private set; }
    public long GalacticPower { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static PlayerProfile Create(long allyCode, string name, long galacticPower, DateTimeOffset updatedAtUtc)
    {
        Validate(allyCode, name, galacticPower);
        return new PlayerProfile(allyCode, name.Trim(), galacticPower, updatedAtUtc);
    }

    public void Refresh(string name, long galacticPower, DateTimeOffset updatedAtUtc)
    {
        Validate(AllyCode, name, galacticPower);
        Name = name.Trim();
        GalacticPower = galacticPower;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static void Validate(long allyCode, string name, long galacticPower)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (galacticPower < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(galacticPower), galacticPower, "Galactic power cannot be negative.");
        }
    }
}
