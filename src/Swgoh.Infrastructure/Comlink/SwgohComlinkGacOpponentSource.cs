namespace Swgoh.Infrastructure.Comlink;

// Compatibility holder for the named HttpClient used by the fast locator.
// Opponent discovery is implemented by SwgohComlinkFastGacOpponentSource and
// authoritative bracket resolution is handled by GacExactBracketResolver.
internal static class SwgohComlinkGacOpponentSource
{
    internal const string HttpClientName = ComlinkGacClient.HttpClientName;
}
