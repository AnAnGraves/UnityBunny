using Platformer.Core;
using Platformer.Mechanics;
using Platformer.Model;

namespace Platformer.Gameplay
{
    /// <summary>
    /// Fired when the player is spawned after dying.
    /// </summary>
    public class PlayerSpawn : Simulation.Event<PlayerSpawn>
    {
        PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        public override void Execute()
        {
            var player = model.player;
            player.m_collider2d.enabled = true;
            player.m_controlEnabled = false;
            if (player.m_audioSource && player.respawnAudio)
                player.m_audioSource.PlayOneShot(player.respawnAudio);
            player.m_health.Increment();
            player.Teleport(model.spawnPoint.transform.position);
            player.m_state = PlayerController.JumpState.Grounded;
            player.m_animator.SetBool("dead", false);
            model.virtualCamera.Follow = player.transform;
            model.virtualCamera.LookAt = player.transform;
            Simulation.Schedule<EnablePlayerInput>(0.5f);
        }
    }
}