using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        ShipRuntime _ship;

        public Program()
        {
            _ship = new ShipRuntime(this);
            _ship.Init();
        }

        public void Save()
        {
            _ship.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            _ship.Main(argument, updateSource);
        }
    }
}
