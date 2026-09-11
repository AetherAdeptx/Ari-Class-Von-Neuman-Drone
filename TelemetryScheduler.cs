using System.Collections.Generic;

namespace IngameScript
{
    public partial class Program
    {
        sealed class TelemetryScheduler
        {
            const int UpdatesPerTick = 2;

            enum JobKind
            {
                Motion,
                Power,
                Fuel,
                Ore,
                Registry
            }

            sealed class Job
            {
                public readonly string Name;
                public readonly JobKind Kind;
                public readonly string Item;

                public Job(string name, JobKind kind, string item = null)
                {
                    Name = name;
                    Kind = kind;
                    Item = item;
                }
            }

            readonly TelemetryState _telemetry;
            readonly ShipStatsTelemetry _shipStats;
            readonly FuelTelemetry _fuels;
            readonly ResourceTelemetry _resources;
            readonly RuntimeRegistry _registry;
            readonly List<Job> _jobs = new List<Job>();
            int _nextJob;

            public TelemetryScheduler(
                TelemetryState telemetry,
                ShipStatsTelemetry shipStats,
                FuelTelemetry fuels,
                ResourceTelemetry resources,
                RuntimeRegistry registry)
            {
                _telemetry = telemetry;
                _shipStats = shipStats;
                _fuels = fuels;
                _resources = resources;
                _registry = registry;
                BuildSchedule();
            }

            public void Tick(double elapsedSeconds)
            {
                for (int i = 0; i < UpdatesPerTick; i++)
                {
                    if (_jobs.Count == 0)
                        return;

                    Run(_jobs[_nextJob], elapsedSeconds);
                    _nextJob++;
                    if (_nextJob >= _jobs.Count)
                        _nextJob = 0;
                }
            }

            public void RestartCycle()
            {
                _nextJob = 0;
            }

            void BuildSchedule()
            {
                _jobs.Add(new Job("ship.power", JobKind.Power));
                _jobs.Add(new Job("ship.motion", JobKind.Motion));

                for (int i = 0;
                    i < ResourceTelemetry.RawResourceNames.Length;
                    i++)
                {
                    string ore = ResourceTelemetry.RawResourceNames[i];
                    _jobs.Add(new Job("ore." + ore, JobKind.Ore, ore));

                    if (i < FuelTelemetry.FuelNames.Length)
                    {
                        string fuel = FuelTelemetry.FuelNames[i];
                        _jobs.Add(new Job(
                            "fuel." + fuel,
                            JobKind.Fuel,
                            fuel));
                    }
                }

                _jobs.Add(new Job("registry.settings", JobKind.Registry));
                _telemetry.UpdateNames.Clear();
                for (int i = 0; i < _jobs.Count; i++)
                    _telemetry.UpdateNames.Add(_jobs[i].Name);
                _telemetry.UpdateNames.Add("sensor.velocity@charged");
                _telemetry.UpdateNames.Add("sensor.other-directions@4s");
                _telemetry.UpdateNames.Add("spatial.buffer@every-tick");
                _telemetry.UpdateNames.Add("ship.block-crawler@every-tick");
                _telemetry.UpdateNames.Add("supervisor.queue@every-tick");
            }

            void Run(Job job, double elapsedSeconds)
            {
                switch (job.Kind)
                {
                    case JobKind.Motion:
                        _shipStats.UpdateMotion(elapsedSeconds);
                        break;
                    case JobKind.Power:
                        _shipStats.UpdatePower();
                        break;
                    case JobKind.Fuel:
                        _fuels.Update(job.Item);
                        break;
                    case JobKind.Ore:
                        _resources.UpdateOre(job.Item, elapsedSeconds);
                        break;
                    case JobKind.Registry:
                        _registry.UpdateSettings();
                        break;
                }
            }
        }
    }
}
