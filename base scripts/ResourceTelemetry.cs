using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ResourceTelemetry
        {
            public static readonly string[] RawResourceNames =
            {
                "Stone", "Iron", "Nickel", "Cobalt", "Magnesium",
                "Silicon", "Silver", "Gold", "Platinum", "Uranium",
                "Ice", "Scrap"
            };

            readonly ShipState _ship;
            readonly TelemetryState _telemetry;
            readonly OreMemory _oreMemory;
            readonly Dictionary<string, double> _lastAmounts =
                new Dictionary<string, double>();
            readonly Dictionary<string, double> _lastSampleSeconds =
                new Dictionary<string, double>();

            public ResourceTelemetry(ShipState ship, TelemetryState telemetry,
                OreMemory oreMemory)
            {
                _ship = ship;
                _telemetry = telemetry;
                _oreMemory = oreMemory;

                for (int i = 0; i < RawResourceNames.Length; i++)
                {
                    string name = RawResourceNames[i];
                    _telemetry.RawResourceAmounts[name] = 0;
                    _telemetry.TargetRawResourceAmounts[name] = 0;
                    _telemetry.ProjectedNeededResources[name] = 0;
                    _telemetry.OreNetRatePerSecond[name] = 0;
                    _telemetry.OreProductionRatePerSecond[name] = 0;
                }
            }

            public void UpdateOre(string oreName, double elapsedSeconds)
            {
                MyItemType oreType = MyItemType.MakeOre(oreName);
                double amount = 0;

                for (int blockIndex = 0;
                    blockIndex < _ship.InventoryBlocks.Count;
                    blockIndex++)
                {
                    IMyTerminalBlock block = _ship.InventoryBlocks[blockIndex];
                    for (int inventoryIndex = 0;
                        inventoryIndex < block.InventoryCount;
                        inventoryIndex++)
                    {
                        IMyInventory inventory = block.GetInventory(inventoryIndex);
                        amount += (double)inventory.GetItemAmount(oreType);
                    }
                }

                double lastAmount;
                double lastSample;
                if (_lastAmounts.TryGetValue(oreName, out lastAmount) &&
                    _lastSampleSeconds.TryGetValue(oreName, out lastSample))
                {
                    double sampleDuration = elapsedSeconds - lastSample;
                    if (sampleDuration > 0.0001)
                    {
                        _telemetry.OreNetRatePerSecond[oreName] =
                            (amount - lastAmount) / sampleDuration;
                        _telemetry.OreProductionRatePerSecond[oreName] =
                            Math.Max(
                                0,
                                _telemetry.OreNetRatePerSecond[oreName]);
                        if (amount > lastAmount &&
                            (_ship.Supervisor_State ==
                                ShipState.SupervisorState.Mining ||
                             _ship.Supervisor_State ==
                                ShipState.SupervisorState.Prospecting))
                            _oreMemory.Mark(oreName,
                                _ship.CurrentShipPosition, elapsedSeconds);
                    }
                }

                _lastAmounts[oreName] = amount;
                _lastSampleSeconds[oreName] = elapsedSeconds;
                _telemetry.RawResourceAmounts[oreName] = amount;

                double target = _telemetry.TargetRawResourceAmounts[oreName];
                _telemetry.ProjectedNeededResources[oreName] =
                    Math.Max(0, target - amount);
            }
        }
    }
}
