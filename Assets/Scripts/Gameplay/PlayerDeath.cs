using System.Collections;
using System.Collections.Generic;
using Platformer.Core;
using Platformer.Model;
using UnityEngine;

namespace Platformer.Gameplay
{
    /// <summary>
    /// Fired when the player has died.
    /// </summary>
    /// <typeparam name="PlayerDeath"></typeparam>
    public class PlayerDeath : Simulation.Event<PlayerDeath>
    {
        PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        public override void Execute()
        {
            var player = model.player;
            if (player.m_health.IsAlive)
            {
                player.m_health.Die();
                model.virtualCamera.Follow = null;
                model.virtualCamera.LookAt = null;
                // player.collider.enabled = false;
                player.m_controlEnabled = false;

                if (player.m_audioSource && player.ouchAudio)
                    player.m_audioSource.PlayOneShot(player.ouchAudio);
                player.m_animator.SetTrigger("hurt");
                player.m_animator.SetBool("dead", true);

                player.m_chargeParticles.Stop();
                player.m_chargeParticles.Clear();

                player.velocity = Vector2.zero;
                
                Simulation.Schedule<PlayerSpawn>(2);
            }
        }
    }
}