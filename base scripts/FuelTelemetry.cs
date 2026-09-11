using Sandbox.ModAPI.Ingame;
using System;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        sealed class FuelTelemetry
        {
            public static readonly string[] FuelNames =
            {
                "Battery", "Hydrogen", "Oxygen", "Uranium", "Ice"
            };

            readonly ShipState _ship;
            readonly TelemetryState _telemetry;

            public FuelTelemetry(ShipState ship, TelemetryState telemetry)
            {
                _ship = ship;
                _telemetry = telemetry;
                for (int i = 0; i < FuelNames.Length; i++)
                    _telemetry.FuelPercent[FuelNames[i]] = 0;
            }

            public void Update(string fuelName)
            {
                if (fuelName == "Battery")
                    UpdateBatteries();
                else if (fuelName == "Hydrogen")
                    UpdateGas("Hydrogen");
                else if (fuelName == "Oxygen")
                    UpdateGas("Oxygen");
                else if (fuelName == "Uranium")
                    UpdateUranium();
                else if (fuelName == "Ice")
                    UpdateIce();
            }

            void UpdateBatteries()
            {
                double stored = 0;
                double capacity = 0;
                for (int i = 0; i < _ship.Batteries.Count; i++)
                {
                    stored += _ship.Batteries[i].CurrentStoredPower;
                    capacity += _ship.Batteries[i].MaxStoredPower;
                }

                SetPercent("Battery", stored, capacity);
            }

            void UpdateGas(string gasName)
            {
                double filledCapacity = 0;
                double totalCapacity = 0;

                for (int i = 0; i < _ship.GasTanks.Count; i++)
                {
                    IMyGasTank tank = _ship.GasTanks[i];
                    string identity = tank.BlockDefinition.SubtypeName + " " +
                        tank.DefinitionDisplayNameText;
                    if (identity.IndexOf(gasName,
                        StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    totalCapacity += tank.Capacity;
                    filledCapacity += tank.Capacity * tank.FilledRatio;
                }

                SetPercent(gasName, filledCapacity, totalCapacity);
            }

            void UpdateUranium()
            {
                double usedVolume = 0;
                double maxVolume = 0;
                MyItemType uranium = MyItemType.MakeIngot("Uranium");

                for (int i = 0; i < _ship.Reactors.Count; i++)
                {
                    IMyInventory inventory = _ship.Reactors[i].GetInventory();
                    if ((double)inventory.GetItemAmount(uranium) <= 0 &&
                        (double)inventory.CurrentVolume <= 0)
                    {
                        maxVolume += (double)inventory.MaxVolume;
                        continue;
                    }

                    usedVolume += (double)inventory.CurrentVolume;
                    maxVolume += (double)inventory.MaxVolume;
                }

                SetPercent("Uranium", usedVolume, maxVolume);
            }

            void UpdateIce()
            {
                double usedVolume = 0;
                double maxVolume = 0;

                for (int i = 0; i < _ship.GasGenerators.Count; i++)
                {
                    IMyInventory inventory =
                        _ship.GasGenerators[i].GetInventory();
                    usedVolume += (double)inventory.CurrentVolume;
                    maxVolume += (double)inventory.MaxVolume;
                }

                SetPercent("Ice", usedVolume, maxVolume);
            }

            void SetPercent(string name, double current, double maximum)
            {
                double value = Percent(current, maximum);
                _telemetry.FuelPercent[name] = value;
                _telemetry.ShipStats[name + "Percent"] = value;
            }

            double Percent(double current, double maximum)
            {
                if (maximum <= 0)
                    return 0;
                return Math.Max(0, Math.Min(100, current / maximum * 100));
            }
        }
    }
}
