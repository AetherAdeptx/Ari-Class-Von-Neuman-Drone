using System.Collections.Generic;

namespace IngameScript
{
    public partial class Program
    {
        sealed class TelemetryState
        {
            // Ship statistics use the units included in each key.
            public readonly Dictionary<string, double> ShipStats =
                new Dictionary<string, double>();

            // Fuel values are always normalized to 0-100 percent capacity.
            public readonly Dictionary<string, double> FuelPercent =
                new Dictionary<string, double>();

            // Ore amounts and reserve targets use Space Engineers inventory
            // amount units (kg for ores).
            public readonly Dictionary<string, double> RawResourceAmounts =
                new Dictionary<string, double>();
            public readonly Dictionary<string, double> TargetRawResourceAmounts =
                new Dictionary<string, double>();
            public readonly Dictionary<string, double> ProjectedNeededResources =
                new Dictionary<string, double>();
            public readonly Dictionary<string, double> OreNetRatePerSecond =
                new Dictionary<string, double>();
            public readonly Dictionary<string, double> OreProductionRatePerSecond =
                new Dictionary<string, double>();

            // Public schedule description in its actual round-robin order.
            public readonly List<string> UpdateNames = new List<string>();
        }
    }
}
