using Platformer.Core;
using Platformer.Mechanics;
using Platformer.Model;

namespace Platformer.Gameplay
{
    /// <summary>
    /// Fired when the health component on an enemy has a hitpoint value of  0.
    /// </summary>
    /// <typeparam name="EnemyDeath"></typeparam>
    public class ToggleBackpackEvent : Simulation.Event<ToggleBackpackEvent>
    {
        public override void Execute()
        {
            PlatformerModel model = Simulation.GetModel<PlatformerModel>();
        }
    }
}