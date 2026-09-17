namespace Swgoh.Application.Investments;

internal static class RelicMaterialRequirements
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, long>> RequirementsByTargetTier =
        new Dictionary<int, IReadOnlyDictionary<string, long>>
        {
            [1] = Materials(("credits", 10_000), ("carbonite_circuit_board", 40)),
            [2] = Materials(("credits", 25_000), ("carbonite_circuit_board", 30), ("bronzium_wiring", 40), ("signal_data_fragmented", 15)),
            [3] = Materials(("credits", 50_000), ("carbonite_circuit_board", 30), ("bronzium_wiring", 40), ("chromium_transistor", 20), ("signal_data_fragmented", 20), ("signal_data_incomplete", 15)),
            [4] = Materials(("credits", 75_000), ("carbonite_circuit_board", 30), ("bronzium_wiring", 40), ("chromium_transistor", 40), ("signal_data_fragmented", 20), ("signal_data_incomplete", 25)),
            [5] = Materials(("credits", 100_000), ("carbonite_circuit_board", 30), ("bronzium_wiring", 40), ("chromium_transistor", 30), ("aurodium_heatsink", 20), ("signal_data_fragmented", 20), ("signal_data_incomplete", 25), ("signal_data_flawed", 15)),
            [6] = Materials(("credits", 250_000), ("carbonite_circuit_board", 20), ("bronzium_wiring", 30), ("chromium_transistor", 30), ("aurodium_heatsink", 20), ("electrium_conductor", 20), ("signal_data_fragmented", 20), ("signal_data_incomplete", 25), ("signal_data_flawed", 25)),
            [7] = Materials(("credits", 500_000), ("carbonite_circuit_board", 20), ("bronzium_wiring", 30), ("chromium_transistor", 20), ("aurodium_heatsink", 20), ("electrium_conductor", 20), ("zinbiddle_card", 10), ("signal_data_fragmented", 20), ("signal_data_incomplete", 25), ("signal_data_flawed", 35)),
            [8] = Materials(("credits", 1_000_000), ("chromium_transistor", 20), ("aurodium_heatsink", 20), ("electrium_conductor", 20), ("zinbiddle_card", 20), ("impulse_detector", 20), ("aeromagnifier", 20), ("signal_data_fragmented", 20), ("signal_data_incomplete", 25), ("signal_data_flawed", 45)),
            [9] = Materials(("credits", 1_500_000), ("electrium_conductor", 20), ("zinbiddle_card", 20), ("impulse_detector", 20), ("aeromagnifier", 20), ("gyrda_keypad", 20), ("droid_brain", 20), ("signal_data_incomplete", 30), ("signal_data_flawed", 55)),
            [10] = Materials(("credits", 2_000_000), ("impulse_detector", 20), ("aeromagnifier", 20), ("gyrda_keypad", 20), ("droid_brain", 20), ("coaxial_servomotor", 20), ("signal_data_incomplete", 25), ("signal_data_flawed", 45), ("signal_data_corrupted", 15))
        };

    public static IReadOnlyDictionary<string, long> Calculate(int currentRelicTier, int targetRelicTier)
    {
        if (targetRelicTier <= currentRelicTier || targetRelicTier <= 0)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        for (int tier = Math.Max(1, currentRelicTier + 1); tier <= targetRelicTier; tier++)
        {
            if (!RequirementsByTargetTier.TryGetValue(tier, out IReadOnlyDictionary<string, long>? tierRequirements))
            {
                continue;
            }

            foreach ((string resourceId, long quantity) in tierRequirements)
            {
                result[resourceId] = result.GetValueOrDefault(resourceId) + quantity;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, long> Materials(params (string Id, long Quantity)[] values) =>
        values.ToDictionary(value => value.Id, value => value.Quantity, StringComparer.Ordinal);
}
