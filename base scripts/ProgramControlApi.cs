namespace IngameScript
{
    public partial class Program
    {
        public int Hard_Stop
        {
            get { return _ship == null ? 0 : _ship.GetHardStop(); }
        }

        public void SetAvoidanceDistancePerSpeed(double metersPerSecond)
        {
            _ship.SetAvoidanceDistancePerSpeed(metersPerSecond);
        }

        public double GetAvoidanceDistancePerSpeed()
        {
            return _ship.GetAvoidanceDistancePerSpeed();
        }

        public string DumpRegistry()
        {
            return _ship.DumpRegistry();
        }

        public string DumpDebug()
        {
            return _ship.DumpDebug();
        }

    }
}
