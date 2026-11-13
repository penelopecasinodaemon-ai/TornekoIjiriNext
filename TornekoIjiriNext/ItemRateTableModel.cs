using System.Buffers.Binary;

namespace TornekoIjiriNext;

internal sealed record LegacyItemDefinition(int Id, string Name, int CategoryId);

/// <summary>Pure offline model for one or more legacy 768-byte item-rate blocks.</summary>
internal sealed class ItemRateTableModel
{
    public const int BlockSize = 768;
    public const int CategoryCdfBytes = 28;
    public const int IndividualRateOffset = 30;
    public const int MaximumCumulativeRate = 999;
    private readonly byte[] _bytes;

    public ItemRateTableModel(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length % BlockSize != 0)
            throw new InvalidDataException($"Data length must be a non-zero multiple of {BlockSize} bytes.");
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Input size exceeds 16 MiB.");
        _bytes = bytes;
    }

    public int BlockCount => _bytes.Length / BlockSize;
    public byte[] ExportBytes() => (byte[])_bytes.Clone();

    public ushort ReadCategoryCumulative(int block, int categoryIndex)
    {
        ValidateBlock(block);
        if (categoryIndex is < 0 or >= 14) throw new ArgumentOutOfRangeException(nameof(categoryIndex));
        return BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(block * BlockSize + categoryIndex * 2, 2));
    }

    public int[] ReadCategoryWeights(int block)
    {
        var weights = new int[14]; var previous = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            var cumulative = ReadCategoryCumulative(block, i);
            weights[i] = cumulative - previous;
            previous = cumulative;
        }
        return weights;
    }

    public void WriteCategoryWeights(int block, IReadOnlyList<int> weights)
    {
        ValidateBlock(block);
        if (weights.Count != 14 || weights.Any(value => value is < 0 or > MaximumCumulativeRate) || weights.Sum() > MaximumCumulativeRate)
            throw new ArgumentException("Category rates must contain 14 non-negative weights whose sum is at most 999.", nameof(weights));
        var offset = block * BlockSize; var cumulative = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            cumulative += weights[i];
            BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(offset + i * 2, 2), (ushort)cumulative);
        }
    }

    public ushort ReadItemCumulative(int block, int itemId)
    {
        ValidateBlock(block); ValidateItemId(itemId);
        var offset = block * BlockSize + IndividualRateOffset + itemId * 2;
        return BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(offset, 2));
    }

    public int[] ReadItemWeights(int block, int categoryId, IReadOnlyList<LegacyItemDefinition> items)
    {
        ValidateBlock(block);
        var group = ValidateAndGetCategoryItems(categoryId, items);
        var weights = new int[group.Length]; var previous = 0;
        for (var i = 0; i < group.Length; i++)
        {
            var cumulative = ReadItemCumulative(block, group[i].Id);
            // HSP treats a zero cumulative entry as disabled and keeps the previous non-zero threshold.
            if (cumulative > 0)
            {
                weights[i] = cumulative - previous;
                previous = cumulative;
            }
        }
        return weights;
    }

    public void WriteItemWeights(int block, int categoryId, IReadOnlyList<LegacyItemDefinition> items, IReadOnlyList<int> weights)
    {
        ValidateBlock(block);
        var group = ValidateAndGetCategoryItems(categoryId, items);
        if (weights.Count != group.Length || weights.Any(value => value is < 0 or > MaximumCumulativeRate) || weights.Sum() > MaximumCumulativeRate)
            throw new ArgumentException("Item weights must match the selected category and sum to at most 999.", nameof(weights));
        var cumulative = 0;
        for (var i = 0; i < group.Length; i++)
        {
            cumulative += weights[i];
            var offset = block * BlockSize + IndividualRateOffset + group[i].Id * 2;
            BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(offset, 2), (ushort)cumulative);
        }
    }

    public static int[] NormalizeTo999(IReadOnlyList<int> weights)
    {
        if (weights.Count == 0 || weights.Any(value => value < 0)) throw new ArgumentException("At least one non-negative weight is required.", nameof(weights));
        var active = Enumerable.Range(0, weights.Count).Where(i => weights[i] > 0).ToArray();
        if (active.Length == 0) throw new ArgumentException("There are no active weights to normalize.", nameof(weights));
        var result = weights.ToArray();
        var total = result.Sum();
        if (total == 0) throw new ArgumentException("There are no active weights to normalize.", nameof(weights));
        var assigned = 0;
        foreach (var i in active)
        {
            result[i] = (int)((long)weights[i] * MaximumCumulativeRate / total);
            assigned += result[i];
        }
        var remaining = MaximumCumulativeRate - assigned;
        for (var index = 0; remaining > 0; index++, remaining--) result[active[index % active.Length]]++;
        return result;
    }

    private void ValidateBlock(int block)
    {
        if (block < 0 || block >= BlockCount) throw new ArgumentOutOfRangeException(nameof(block));
    }

    private static void ValidateItemId(int id)
    {
        if (id is < 0 or > 353) throw new ArgumentOutOfRangeException(nameof(id), "Legacy item ID must be 0..353.");
    }

    private static LegacyItemDefinition[] ValidateAndGetCategoryItems(int categoryId, IReadOnlyList<LegacyItemDefinition> items)
    {
        if (items.Count != 354) throw new ArgumentException("The legacy item catalog must contain 354 items.", nameof(items));
        for (var i = 0; i < items.Count; i++) if (items[i].Id != i) throw new ArgumentException("Legacy item IDs must be contiguous 0..353 and source ordered.", nameof(items));
        var group = items.Where(item => item.CategoryId == categoryId).ToArray();
        if (group.Length == 0) throw new ArgumentOutOfRangeException(nameof(categoryId), "No items exist for this category.");
        return group;
    }
}
