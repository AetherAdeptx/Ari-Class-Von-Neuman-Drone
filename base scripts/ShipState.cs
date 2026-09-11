using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ShipState
        {
            public enum SupervisorState
            {
                Normal,
                In_Use,
                Evasion,
                Emergency,
                Solar_Charge,
                Return_To_Base,
                Track_Radio,
                Search,
                Docking,
                Docked
            }

            public readonly List<IMyTerminalBlock> Blocks =
                new List<IMyTerminalBlock>();
            public readonly Dictionary<long, Vector3I> GridPositions =
                new Dictionary<long, Vector3I>();
            public readonly Dictionary<long, Vector3D> WorldPositions =
                new Dictionary<long, Vector3D>();
            public readonly List<IMyShipController> Controllers =
                new List<IMyShipController>();
            public readonly List<IMyTerminalBlock> InventoryBlocks =
                new List<IMyTerminalBlock>();
            public readonly List<IMyPowerProducer> PowerProducers =
                new List<IMyPowerProducer>();
            public readonly List<IMyFunctionalBlock> FuelSources =
                new List<IMyFunctionalBlock>();
            public readonly List<IMyBatteryBlock> Batteries =
                new List<IMyBatteryBlock>();
            public readonly List<IMyGasTank> GasTanks =
                new List<IMyGasTank>();
            public readonly List<IMyReactor> Reactors =
                new List<IMyReactor>();
            public readonly List<IMyGasGenerator> GasGenerators =
                new List<IMyGasGenerator>();
            public readonly List<IMyCameraBlock> DistanceSensors =
                new List<IMyCameraBlock>();
            public readonly List<IMyRadioAntenna> RadioAntennas =
                new List<IMyRadioAntenna>();
            public readonly List<IMyGyro> Gyroscopes =
                new List<IMyGyro>();
            public readonly List<IMySolarPanel> SolarPanels =
                new List<IMySolarPanel>();
            public readonly List<IMyMotorStator> SolarRotors =
                new List<IMyMotorStator>();
            public readonly List<IMyShipConnector> Connectors =
                new List<IMyShipConnector>();
            public readonly List<IMyBeacon> Beacons =
                new List<IMyBeacon>();

            public IMyShipController ReferenceController;
            public Vector3D CurrentShipPosition;
            public Vector3D Spatial_Direction_Vector;
            public Vector3D Spatial_Last_Direction_Vector;
            public Vector3D Spatial_Second_Last_Direction_Vector;
            public Vector3D Home_Base_Coordinates;
            public Vector3D Assigned_Dock_Coordinates;
            public IMyTerminalBlock Negative_X_Most_Block;
            public IMyTerminalBlock Positive_X_Most_Block;
            public IMyTerminalBlock Negative_Y_Most_Block;
            public IMyTerminalBlock Positive_Y_Most_Block;
            public IMyTerminalBlock Negative_Z_Most_Block;
            public IMyTerminalBlock Positive_Z_Most_Block;
            public Vector3D Collision_Min_Local_Meters;
            public Vector3D Collision_Max_Local_Meters;
            public double Collision_Bounding_Radius_Meters;

            public readonly List<IMyThrust> Thrusters = new List<IMyThrust>();

            public readonly List<IMyThrust> Forward = new List<IMyThrust>();
            public readonly List<IMyThrust> Backward = new List<IMyThrust>();
            public readonly List<IMyThrust> Up = new List<IMyThrust>();
            public readonly List<IMyThrust> Down = new List<IMyThrust>();
            public readonly List<IMyThrust> Left = new List<IMyThrust>();
            public readonly List<IMyThrust> Right = new List<IMyThrust>();

            public readonly List<IMyThrust> PitchUp = new List<IMyThrust>();
            public readonly List<IMyThrust> PitchDown = new List<IMyThrust>();
            public readonly List<IMyThrust> YawLeft = new List<IMyThrust>();
            public readonly List<IMyThrust> YawRight = new List<IMyThrust>();
            public readonly List<IMyThrust> RollLeft = new List<IMyThrust>();
            public readonly List<IMyThrust> RollRight = new List<IMyThrust>();

            public readonly List<IMyCameraBlock> SensorsForward =
                new List<IMyCameraBlock>();
            public readonly List<IMyCameraBlock> SensorsBackward =
                new List<IMyCameraBlock>();
            public readonly List<IMyCameraBlock> SensorsUp =
                new List<IMyCameraBlock>();
            public readonly List<IMyCameraBlock> SensorsDown =
                new List<IMyCameraBlock>();
            public readonly List<IMyCameraBlock> SensorsLeft =
                new List<IMyCameraBlock>();
            public readonly List<IMyCameraBlock> SensorsRight =
                new List<IMyCameraBlock>();

            // Reserved for the later flight-control module. Rotation lists are
            // sorted most-effective to least-effective before this is applied.
            public double Thruster_Rotation_Factor = 0.50;
            public int Spatial_Cache_Distance = 4;
            public int Hard_Stop;
            public int Velocity_Vector_Encounter_Detected;
            public int Velocity_Vector_Scan_Valid;
            public double Velocity_Vector_Scan_Distance_Meters = 2000;
            public double Velocity_Vector_Scan_Age_Seconds;
            public double Velocity_Vector_Encounter_Distance_Meters;
            public int Needs_Avoidance;
            public SupervisorState Supervisor_State = SupervisorState.Normal;
            public int Tasks_Per_Frame = 2;
            public int Supervisor_Queued_Tasks;
            public double Low_Charge_Percent = 20;
            public double Low_Fuel_Percent = 15;
            public double Solar_Current_Output_MW;
            public double Solar_Best_Output_MW;
            public int Auto_Tracking_Flag = 1;
            public int Auto_Tracking_Receicing_Side = -1;
            public double Auto_Tracking_Enable_Level = 60;
            public long Mother_Ship_Address;
            public string Node_Identity = "Drone16-Green";
            public string Radio_Encryption_Key = "VonNeuman-Change-Me";
            public int Radio_Node_Kind;
            public int Radio_Slot = 16;
            public int Radio_Color = 3;
            public int Discovery_Mode;
            public int Broadcast_Mode;
            public int Monitor_Mode;
            public int Beacon_Enabled;
            public double Enemy_Exposure_Time;
            public bool Free_Move;
            public Vector3D Target_Destination;
            public double Cruise_Speed = 15;
            public double Travel_Speed = 100;
            public double Target_Speed = 25;
            public int Task_Object_Lines_Per_Frame = 2;
            public int Navigation_Nodes_Per_Frame = 16;
            public int Navigation_Waypoint_Count;
            public bool Navigation_Dangerous_Space;
            public int Navigation_History_Count;
            public int Task_Object_Queue_Count;

            public void SetThrusterRotationFactor(double factor)
            {
                Thruster_Rotation_Factor = System.Math.Max(
                    0,
                    System.Math.Min(1, factor));
            }

            public void SetSpatialCacheDistance(int distance)
            {
                Spatial_Cache_Distance = System.Math.Max(
                    1,
                    System.Math.Min(16, distance));
            }

            public void ClearLayout()
            {
                Blocks.Clear();
                GridPositions.Clear();
                WorldPositions.Clear();
                Controllers.Clear();
                InventoryBlocks.Clear();
                PowerProducers.Clear();
                FuelSources.Clear();
                Batteries.Clear();
                GasTanks.Clear();
                Reactors.Clear();
                GasGenerators.Clear();
                DistanceSensors.Clear();
                RadioAntennas.Clear();
                Gyroscopes.Clear();
                SolarPanels.Clear();
                SolarRotors.Clear();
                Connectors.Clear();
                Beacons.Clear();
                Thrusters.Clear();
                ReferenceController = null;
                Negative_X_Most_Block = null;
                Positive_X_Most_Block = null;
                Negative_Y_Most_Block = null;
                Positive_Y_Most_Block = null;
                Negative_Z_Most_Block = null;
                Positive_Z_Most_Block = null;

                Forward.Clear();
                Backward.Clear();
                Up.Clear();
                Down.Clear();
                Left.Clear();
                Right.Clear();

                PitchUp.Clear();
                PitchDown.Clear();
                YawLeft.Clear();
                YawRight.Clear();
                RollLeft.Clear();
                RollRight.Clear();

                SensorsForward.Clear();
                SensorsBackward.Clear();
                SensorsUp.Clear();
                SensorsDown.Clear();
                SensorsLeft.Clear();
                SensorsRight.Clear();
            }
        }
    }
}
