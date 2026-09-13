namespace Swgoh.Domain.Squads;

public sealed class SquadDefinition
{
    private readonly List<string> tags;
    private readonly List<SquadVariant> variants;

    private SquadDefinition(
        Guid id,
        string name,
        SquadFormat format,
        SquadUse use,
        IEnumerable<string> tags,
        IEnumerable<SquadVariant> variants,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        Name = name;
        Format = format;
        Use = use;
        this.tags = [.. tags];
        this.variants = [.. variants];
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }
    public string Name { get; private set; }
    public SquadFormat Format { get; private set; }
    public SquadUse Use { get; private set; }
    public IReadOnlyList<string> Tags => tags;
    public IReadOnlyList<SquadVariant> Variants => variants;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static SquadDefinition Create(
        Guid id,
        string name,
        SquadFormat format,
        SquadUse use,
        IEnumerable<string> tags,
        IEnumerable<SquadVariant> variants,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Squad ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string[] normalizedTags = NormalizeTags(tags);
        SquadVariant[] validatedVariants = ValidateVariants(format, variants);
        return new SquadDefinition(
            id,
            name.Trim(),
            format,
            use,
            normalizedTags,
            validatedVariants,
            createdAtUtc,
            createdAtUtc);
    }

    public static SquadDefinition Restore(
        Guid id,
        string name,
        SquadFormat format,
        SquadUse use,
        IEnumerable<string> tags,
        IEnumerable<SquadVariant> variants,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        SquadDefinition definition = Create(id, name, format, use, tags, variants, createdAtUtc);
        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "Updated time cannot be earlier than created time.");
        }

        definition.UpdatedAtUtc = updatedAtUtc;
        return definition;
    }

    public void Update(
        string name,
        SquadFormat format,
        SquadUse use,
        IEnumerable<string> tags,
        IEnumerable<SquadVariant> variants,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (updatedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "Updated time cannot be earlier than created time.");
        }

        string[] normalizedTags = NormalizeTags(tags);
        SquadVariant[] validatedVariants = ValidateVariants(format, variants);

        Name = name.Trim();
        Format = format;
        Use = use;
        this.tags.Clear();
        this.tags.AddRange(normalizedTags);
        this.variants.Clear();
        this.variants.AddRange(validatedVariants);
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string[] NormalizeTags(IEnumerable<string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] normalized =
        [
            .. source
                .Select(tag => string.IsNullOrWhiteSpace(tag)
                    ? throw new ArgumentException("Squad tags cannot be empty.", nameof(source))
                    : tag.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(tag => tag, StringComparer.Ordinal)
        ];

        if (normalized.Length > 20)
        {
            throw new ArgumentException("A squad cannot contain more than 20 tags.", nameof(source));
        }

        if (normalized.Any(tag => tag.Length > 64))
        {
            throw new ArgumentException("Squad tags cannot exceed 64 characters.", nameof(source));
        }

        return normalized;
    }

    private static SquadVariant[] ValidateVariants(SquadFormat format, IEnumerable<SquadVariant> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SquadVariant[] items = [.. source];
        if (items.Length == 0)
        {
            throw new ArgumentException("A squad definition requires at least one variant.", nameof(source));
        }

        if (items.Any(variant => variant.Format != format))
        {
            throw new ArgumentException("All squad variants must use the squad definition format.", nameof(source));
        }

        if (items.Select(variant => variant.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
        {
            throw new ArgumentException("Squad variant keys must be unique.", nameof(source));
        }

        return items;
    }
}
