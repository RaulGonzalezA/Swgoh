namespace Swgoh.Application.Gac;

public interface ICurrentGacOpponentCache
{
    void Invalidate(long allyCode);
}
