using Platformer.Core;
using Platformer.Mechanics;
using Platformer.Model;
using static Platformer.Core.Simulation;

namespace Platformer.Gameplay
{

    /// <summary>
    /// Fired when a Player collides with an Enemy.
    /// </summary>
    /// <typeparam name="EnemyCollision"></typeparam>
    public class PlayerRevokeInvincibility : Simulation.Event<PlayerRevokeInvincibility>
    {

        PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        public override void Execute()
        {
            PlayerController player = model.player;
            if(player)
            {

            }
        }
    }
}