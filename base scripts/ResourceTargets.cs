using Sandbox.ModAPI.Ingame;
using System;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program
    {
        sealed class ResourceTargets
        {
            const string Section = "resource-targets";
            readonly Program _program;
            readonly TelemetryState _telemetry;
            readonly MyIni _ini = new MyIni();

            public ResourceTargets(Program program, TelemetryState telemetry)
            {
                _program = program;
                _telemetry = telemetry;
            }

            public void Reload()
            {
                MyIniParseResult result;
                bool valid = _ini.TryParse(_program.Me.CustomData, out result);

                for (int i = 0;
                    i < ResourceTelemetry.RawResourceNames.Length;
                    i++)
                {
                    string ore = ResourceTelemetry.RawResourceNames[i];
                    double target = valid
                        ? _ini.Get(Section, ore).ToDouble(0)
                        : 0;
                    _telemetry.TargetRawResourceAmounts[ore] =
                        Math.Max(0, target);
                }
            }
        }
    }
}
