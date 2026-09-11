using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    public partial class Program
    {
        sealed class RuntimeRegistry
        {
            readonly ShipState _ship;
            readonly RadioNetwork _radio;
            readonly GlobalTemporalBsp _globalMap;

            public readonly Dictionary<string, string> Values =
                new Dictionary<string, string>();

            public RuntimeRegistry(
                ShipState ship,
                RadioNetwork radio,
                GlobalTemporalBsp globalMap)
            {
                _ship = ship;
                _radio = radio;
                _globalMap = globalMap;
            }

            public void UpdateSettings()
            {
                double batteryCapacity = 0;
                double batteryCharge = 0;
                for (int i = 0; i < _ship.Batteries.Count; i++)
                {
                    batteryCapacity += _ship.Batteries[i].MaxStoredPower;
                    batteryCharge += _ship.Batteries[i].CurrentStoredPower;
                }
                Values["Thruster_Rotation_Factor"] =
                    _ship.Thruster_Rotation_Factor.ToString("0.000");
                Values["Spatial_Cache_Distance"] =
                    _ship.Spatial_Cache_Distance.ToString();
                Values["Spatial_Buffer_Budget_MiB"] = "64";
                Values["Global_BSP_Budget_MiB"] = "16";
                Values["Global_BSP_Reserved_Bytes"] =
                    _globalMap.ReservedBytes.ToString();
                Values["Global_BSP_Node_Count"] =
                    _globalMap.NodeCount.ToString();
                Values["Global_BSP_Scanned_Cell_Count"] =
                    _globalMap.ScannedCellCount.ToString();
                Values["Global_BSP_Full"] =
                    _globalMap.IsFull ? "1" : "0";
                Values["Hard_Stop"] = _ship.Hard_Stop.ToString();
                Values["Velocity_Vector_Encounter_Detected"] =
                    _ship.Velocity_Vector_Encounter_Detected.ToString();
                Values["Velocity_Vector_Scan_Valid"] =
                    _ship.Velocity_Vector_Scan_Valid.ToString();
                Values["Velocity_Vector_Scan_Distance_Meters"] =
                    _ship.Velocity_Vector_Scan_Distance_Meters.ToString("0");
                Values["Velocity_Vector_Scan_Age_Seconds"] =
                    _ship.Velocity_Vector_Scan_Age_Seconds.ToString("0.00");
                Values["Velocity_Vector_Encounter_Distance_Meters"] =
                    _ship.Velocity_Vector_Encounter_Distance_Meters.ToString(
                        "0.0");
                Values["Home_Base_Coordinates"] =
                    Coordinates(_ship.Home_Base_Coordinates);
                Values["Assigned_Dock_Coordinates"] =
                    Coordinates(_ship.Assigned_Dock_Coordinates);
                Values["Radio_Antenna_Range_Meters"] =
                    _radio.MaximumAntennaRangeMeters.ToString("0");
                Values["Radio_Assigned_Drones"] =
                    _radio.AssignedDroneCount.ToString();
                Values["Radio_Assigned_Relays"] =
                    _radio.AssignedRelayCount.ToString();
                Values["Radio_Out_Of_Sync_Nodes"] =
                    _radio.OutOfSyncNodeCount.ToString();
                Values["Battery_Total_Capacity_MWh"] =
                    batteryCapacity.ToString("0.000");
                Values["Battery_Total_Charge_MWh"] =
                    batteryCharge.ToString("0.000");
                Values["Battery_Count"] =
                    _ship.Batteries.Count.ToString();
                Values["Reactor_Count"] =
                    _ship.Reactors.Count.ToString();
                Values["Solar_Panel_Count"] =
                    _ship.SolarPanels.Count.ToString();
                Values["Negative_X_Most_Block"] =
                    BlockName(_ship.Negative_X_Most_Block);
                Values["Positive_X_Most_Block"] =
                    BlockName(_ship.Positive_X_Most_Block);
                Values["Negative_Y_Most_Block"] =
                    BlockName(_ship.Negative_Y_Most_Block);
                Values["Positive_Y_Most_Block"] =
                    BlockName(_ship.Positive_Y_Most_Block);
                Values["Negative_Z_Most_Block"] =
                    BlockName(_ship.Negative_Z_Most_Block);
                Values["Positive_Z_Most_Block"] =
                    BlockName(_ship.Positive_Z_Most_Block);
                Values["Collision_Min_Local_Meters"] =
                    Coordinates(_ship.Collision_Min_Local_Meters);
                Values["Collision_Max_Local_Meters"] =
                    Coordinates(_ship.Collision_Max_Local_Meters);
                Values["Collision_Bounding_Radius_Meters"] =
                    _ship.Collision_Bounding_Radius_Meters.ToString("0.0");
                Values["Solar_Rotor_Count"] =
                    _ship.SolarRotors.Count.ToString();
                Values["Solar_Current_Output_MW"] =
                    _ship.Solar_Current_Output_MW.ToString("0.000");
                Values["Solar_Best_Output_MW"] =
                    _ship.Solar_Best_Output_MW.ToString("0.000");
                Values["Auto_Tracking_Flag"] =
                    _ship.Auto_Tracking_Flag.ToString();
                Values["Auto_Tracking_Receicing_Side"] =
                    _ship.Auto_Tracking_Receicing_Side.ToString();
                Values["Auto_Tracking_Enable_Level"] =
                    _ship.Auto_Tracking_Enable_Level.ToString("0.0");
                Values["Supervisor_State"] =
                    _ship.Supervisor_State.ToString();
                Values["Tasks_Per_Frame"] =
                    _ship.Tasks_Per_Frame.ToString();
                Values["Supervisor_Queued_Tasks"] =
                    _ship.Supervisor_Queued_Tasks.ToString();
                Values["Low_Charge_Percent"] =
                    _ship.Low_Charge_Percent.ToString("0.0");
                Values["Low_Fuel_Percent"] =
                    _ship.Low_Fuel_Percent.ToString("0.0");
                Values["Mother_Ship_Address"] =
                    _ship.Mother_Ship_Address.ToString();
                Values["Node_Identity"] = _ship.Node_Identity;
                Values["Radio_Encryption_Key"] =
                    _ship.Radio_Encryption_Key;
                Values["Radio_Node_Kind"] = _ship.Radio_Node_Kind.ToString();
                Values["Radio_Slot"] = _ship.Radio_Slot.ToString();
                Values["Radio_Color"] = _ship.Radio_Color.ToString();
                Values["Discovery_Mode"] = _ship.Discovery_Mode.ToString();
                Values["Broadcast_Mode"] = _ship.Broadcast_Mode.ToString();
                Values["Monitor_Mode"] = _ship.Monitor_Mode.ToString();
                Values["Beacon_Enabled"] = _ship.Beacon_Enabled.ToString();
                Values["Enemy_Exposure_Time"] = _ship.Enemy_Exposure_Time.ToString("0.0");
                Values["Free_Move"] = _ship.Free_Move ? "1" : "0";
                Values["Target_Destination"] =
                    Coordinates(_ship.Target_Destination);
                Values["Cruise_Speed"] = _ship.Cruise_Speed.ToString("0.0");
                Values["Travel_Speed"] = _ship.Travel_Speed.ToString("0.0");
                Values["Task_Object_Lines_Per_Frame"] =
                    _ship.Task_Object_Lines_Per_Frame.ToString();
                Values["Task_Object_Queue_Count"] =
                    _ship.Task_Object_Queue_Count.ToString();
                Values["Navigation_Nodes_Per_Frame"] =
                    _ship.Navigation_Nodes_Per_Frame.ToString();
                Values["Navigation_Waypoint_Count"] =
                    _ship.Navigation_Waypoint_Count.ToString();
                Values["Navigation_Dangerous_Space"] =
                    _ship.Navigation_Dangerous_Space ? "1" : "0";
                Values["Navigation_History_Count"] =
                    _ship.Navigation_History_Count.ToString();
                Values["Navigation_History_Raw_Bytes"] =
                    NavigationMemory.RawBytes.ToString();
            }

            public string EncodePersistent()
            {
                UpdateSettings();
                StringBuilder text = new StringBuilder("REG1|");
                foreach (KeyValuePair<string, string> item in Values)
                    text.Append(item.Key).Append('=').Append(item.Value)
                        .Append(';');
                return text.ToString();
            }

            public bool DecodePersistent(string encoded)
            {
                if (string.IsNullOrEmpty(encoded) ||
                    !encoded.StartsWith("REG1|"))
                    return false;
                string[] records = encoded.Substring(5).Split(';');
                for (int i = 0; i < records.Length; i++)
                {
                    int split = records[i].IndexOf('=');
                    if (split > 0)
                        Values[records[i].Substring(0, split)] =
                            records[i].Substring(split + 1);
                }
                double number;
                int integer;
                long address;
                string value;
                if (Values.TryGetValue("Thruster_Rotation_Factor", out value) &&
                    double.TryParse(value, out number))
                    _ship.SetThrusterRotationFactor(number);
                if (Values.TryGetValue("Spatial_Cache_Distance", out value) &&
                    int.TryParse(value, out integer))
                    _ship.SetSpatialCacheDistance(integer);
                if (Values.TryGetValue("Auto_Tracking_Flag", out value) &&
                    int.TryParse(value, out integer))
                    _ship.Auto_Tracking_Flag = integer == 0 ? 0 : 1;
                if (Values.TryGetValue("Auto_Tracking_Enable_Level", out value) &&
                    double.TryParse(value, out number))
                    _ship.Auto_Tracking_Enable_Level =
                        System.Math.Max(0, System.Math.Min(100, number));
                if (Values.TryGetValue("Tasks_Per_Frame", out value) &&
                    int.TryParse(value, out integer))
                    _ship.Tasks_Per_Frame = System.Math.Max(1,
                        System.Math.Min(32, integer));
                ShipState.SupervisorState state;
                if (Values.TryGetValue("Supervisor_State", out value) &&
                    System.Enum.TryParse(value, true, out state))
                    _ship.Supervisor_State = state;
                if (Values.TryGetValue("Low_Charge_Percent", out value) &&
                    double.TryParse(value, out number))
                    _ship.Low_Charge_Percent = number;
                if (Values.TryGetValue("Low_Fuel_Percent", out value) &&
                    double.TryParse(value, out number))
                    _ship.Low_Fuel_Percent = number;
                if (Values.TryGetValue("Mother_Ship_Address", out value) &&
                    long.TryParse(value, out address))
                    _ship.Mother_Ship_Address = address;
                if (Values.TryGetValue("Node_Identity", out value) &&
                    value.Length > 0 && value.Length <= 32)
                    _ship.Node_Identity = value;
                if (Values.TryGetValue("Radio_Encryption_Key", out value) &&
                    value.Length >= 8 && value.Length <= 64)
                    _ship.Radio_Encryption_Key = value;
                if (Values.TryGetValue("Radio_Node_Kind", out value) &&
                    int.TryParse(value, out integer))
                    _ship.Radio_Node_Kind = integer == 0 ? 0 : 1;
                if (Values.TryGetValue("Radio_Slot", out value) &&
                    int.TryParse(value, out integer))
                    _ship.Radio_Slot = System.Math.Max(1, System.Math.Min(
                        _ship.Radio_Node_Kind == 0 ? 24 : 64, integer));
                if (Values.TryGetValue("Radio_Color", out value) &&
                    int.TryParse(value, out integer))
                    _ship.Radio_Color = System.Math.Max(0, System.Math.Min(6,
                        integer));
                if (Values.TryGetValue("Discovery_Mode", out value) && int.TryParse(value, out integer))
                    _ship.Discovery_Mode = integer == 0 ? 0 : 1;
                if (Values.TryGetValue("Broadcast_Mode", out value) && int.TryParse(value, out integer))
                    _ship.Broadcast_Mode = integer == 0 ? 0 : 1;
                if (Values.TryGetValue("Monitor_Mode", out value) && int.TryParse(value, out integer))
                    _ship.Monitor_Mode = integer == 0 ? 0 : 1;
                if (Values.TryGetValue("Beacon_Enabled", out value) && int.TryParse(value, out integer))
                    _ship.Beacon_Enabled = integer == 0 ? 0 : 1;
                if (Values.TryGetValue("Enemy_Exposure_Time", out value) && double.TryParse(value, out number))
                    _ship.Enemy_Exposure_Time = System.Math.Max(0, number);
                if (Values.TryGetValue("Free_Move", out value))
                    _ship.Free_Move = value != "0";
                if (Values.TryGetValue("Target_Destination", out value))
                    TryCoordinates(value, out _ship.Target_Destination);
                if (Values.TryGetValue("Cruise_Speed", out value) &&
                    double.TryParse(value, out number))
                    _ship.Cruise_Speed = System.Math.Max(1,
                        System.Math.Min(100, number));
                if (Values.TryGetValue("Travel_Speed", out value) &&
                    double.TryParse(value, out number))
                    _ship.Travel_Speed = System.Math.Max(1,
                        System.Math.Min(100, number));
                if (Values.TryGetValue("Task_Object_Lines_Per_Frame", out value) &&
                    int.TryParse(value, out integer))
                    _ship.Task_Object_Lines_Per_Frame = System.Math.Max(1,
                        System.Math.Min(32, integer));
                if (Values.TryGetValue("Navigation_Nodes_Per_Frame", out value) &&
                    int.TryParse(value, out integer))
                    _ship.Navigation_Nodes_Per_Frame = System.Math.Max(1,
                        System.Math.Min(128, integer));
                return true;
            }

            bool TryCoordinates(string text, out VRageMath.Vector3D value)
            {
                string[] parts = text.Split(',');
                double x, y, z;
                if (parts.Length == 3 && double.TryParse(parts[0], out x) &&
                    double.TryParse(parts[1], out y) &&
                    double.TryParse(parts[2], out z))
                {
                    value = new VRageMath.Vector3D(x, y, z);
                    return true;
                }
                value = VRageMath.Vector3D.Zero;
                return false;
            }

            string Coordinates(VRageMath.Vector3D value)
            {
                return value.X.ToString("0.0") + "," +
                    value.Y.ToString("0.0") + "," +
                    value.Z.ToString("0.0");
            }

            string BlockName(Sandbox.ModAPI.Ingame.IMyTerminalBlock block)
            {
                return block == null
                    ? "none"
                    : block.CustomName + "@" + block.Position.Z;
            }
        }
    }
}
