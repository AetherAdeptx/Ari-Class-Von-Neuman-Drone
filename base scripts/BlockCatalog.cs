using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class BlockCatalog
        {
            readonly Program _program;
            readonly ShipState _state;
            int _crawlIndex;
            Vector3D _minimum;
            Vector3D _maximum;
            readonly IMyTerminalBlock[] _extremeBlocks =
                new IMyTerminalBlock[6];

            public BlockCatalog(Program program, ShipState state)
            {
                _program = program;
                _state = state;
            }

            public void ScanOnce()
            {
                _state.ClearLayout();

                _program.GridTerminalSystem.GetBlocksOfType(
                    _state.Blocks,
                    block => block.IsSameConstructAs(_program.Me));

                for (int i = 0; i < _state.Blocks.Count; i++)
                {
                    IMyTerminalBlock block = _state.Blocks[i];
                    RecordPosition(block);

                    if (block.HasInventory)
                        _state.InventoryBlocks.Add(block);

                    IMyShipController controller = block as IMyShipController;
                    if (controller != null)
                        _state.Controllers.Add(controller);

                    IMyThrust thruster = block as IMyThrust;
                    if (thruster != null)
                        _state.Thrusters.Add(thruster);

                    IMyPowerProducer producer = block as IMyPowerProducer;
                    if (producer != null)
                    {
                        _state.PowerProducers.Add(producer);
                        IMyFunctionalBlock fuelSource =
                            producer as IMyFunctionalBlock;
                        if (fuelSource != null &&
                            (block is IMyReactor ||
                             block.BlockDefinition.SubtypeName.IndexOf(
                                "HydrogenEngine",
                                System.StringComparison.OrdinalIgnoreCase) >= 0))
                            _state.FuelSources.Add(fuelSource);
                    }

                    IMyBatteryBlock battery = block as IMyBatteryBlock;
                    if (battery != null)
                        _state.Batteries.Add(battery);

                    IMyGasTank gasTank = block as IMyGasTank;
                    if (gasTank != null)
                        _state.GasTanks.Add(gasTank);

                    IMyReactor reactor = block as IMyReactor;
                    if (reactor != null)
                        _state.Reactors.Add(reactor);

                    IMyGasGenerator gasGenerator = block as IMyGasGenerator;
                    if (gasGenerator != null)
                        _state.GasGenerators.Add(gasGenerator);

                    IMyCameraBlock camera = block as IMyCameraBlock;
                    if (camera != null)
                        _state.DistanceSensors.Add(camera);

                    IMyRadioAntenna antenna = block as IMyRadioAntenna;
                    if (antenna != null)
                        _state.RadioAntennas.Add(antenna);

                    IMyGyro gyro = block as IMyGyro;
                    if (gyro != null)
                        _state.Gyroscopes.Add(gyro);

                    IMySolarPanel solar = block as IMySolarPanel;
                    if (solar != null)
                        _state.SolarPanels.Add(solar);

                    IMyMotorStator rotor = block as IMyMotorStator;
                    if (rotor != null && rotor.CustomName.IndexOf(
                        "[Solar]", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        _state.SolarRotors.Add(rotor);

                    IMyShipConnector connector = block as IMyShipConnector;
                    if (connector != null)
                        _state.Connectors.Add(connector);
                    IMyBeacon beacon = block as IMyBeacon;
                    if (beacon != null)
                        _state.Beacons.Add(beacon);
                }
                _crawlIndex = 0;
            }

            public void CrawlNextBlock()
            {
                if (_state.Blocks.Count > 0)
                {
                    if (_crawlIndex >= _state.Blocks.Count)
                        _crawlIndex = 0;
                    if (_crawlIndex == 0)
                        BeginCollisionScan();
                    IMyTerminalBlock block = _state.Blocks[_crawlIndex++];
                    RecordPosition(block);
                    ScanCollisionBounds(block);
                    if (_crawlIndex == _state.Blocks.Count)
                        PublishCollisionBounds();
                }
                if (_state.ReferenceController != null)
                    _state.CurrentShipPosition =
                        _state.ReferenceController.GetPosition();
            }

            void RecordPosition(IMyTerminalBlock block)
            {
                _state.GridPositions[block.EntityId] = block.Position;
                _state.WorldPositions[block.EntityId] = block.GetPosition();
            }

            void BeginCollisionScan()
            {
                _minimum = new Vector3D(
                    double.MaxValue, double.MaxValue, double.MaxValue);
                _maximum = new Vector3D(
                    double.MinValue, double.MinValue, double.MinValue);
                for (int i = 0; i < _extremeBlocks.Length; i++)
                    _extremeBlocks[i] = null;
            }

            void ScanCollisionBounds(IMyTerminalBlock block)
            {
                if (_state.ReferenceController == null)
                    return;
                BoundingBoxD box = block.WorldAABB;
                MatrixD inverse = MatrixD.Invert(
                    _state.ReferenceController.WorldMatrix);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3D world = new Vector3D(
                        (corner & 1) == 0 ? box.Min.X : box.Max.X,
                        (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                        (corner & 4) == 0 ? box.Min.Z : box.Max.Z);
                    Vector3D local = Vector3D.Transform(world, inverse);
                    CheckMinimum(local.X, 0, block, ref _minimum.X);
                    CheckMaximum(local.X, 1, block, ref _maximum.X);
                    CheckMinimum(local.Y, 2, block, ref _minimum.Y);
                    CheckMaximum(local.Y, 3, block, ref _maximum.Y);
                    CheckMinimum(local.Z, 4, block, ref _minimum.Z);
                    CheckMaximum(local.Z, 5, block, ref _maximum.Z);
                }
            }

            void CheckMinimum(
                double value,
                int index,
                IMyTerminalBlock block,
                ref double extreme)
            {
                if (value >= extreme)
                    return;
                extreme = value;
                _extremeBlocks[index] = block;
            }

            void CheckMaximum(
                double value,
                int index,
                IMyTerminalBlock block,
                ref double extreme)
            {
                if (value <= extreme)
                    return;
                extreme = value;
                _extremeBlocks[index] = block;
            }

            void PublishCollisionBounds()
            {
                _state.Collision_Min_Local_Meters = _minimum;
                _state.Collision_Max_Local_Meters = _maximum;
                _state.Negative_X_Most_Block = _extremeBlocks[0];
                _state.Positive_X_Most_Block = _extremeBlocks[1];
                _state.Negative_Y_Most_Block = _extremeBlocks[2];
                _state.Positive_Y_Most_Block = _extremeBlocks[3];
                _state.Negative_Z_Most_Block = _extremeBlocks[4];
                _state.Positive_Z_Most_Block = _extremeBlocks[5];
                double x = System.Math.Max(
                    System.Math.Abs(_minimum.X),
                    System.Math.Abs(_maximum.X));
                double y = System.Math.Max(
                    System.Math.Abs(_minimum.Y),
                    System.Math.Abs(_maximum.Y));
                double z = System.Math.Max(
                    System.Math.Abs(_minimum.Z),
                    System.Math.Abs(_maximum.Z));
                _state.Collision_Bounding_Radius_Meters =
                    System.Math.Sqrt(x * x + y * y + z * z);
            }
        }
    }
}
