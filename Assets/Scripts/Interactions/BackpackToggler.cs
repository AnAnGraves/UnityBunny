using Platformer.Core;
using Platformer.Mechanics;
using Platformer.Model;
using UnityEngine;

public class BackpackToggler : InteractableObject
{
    public override void OnInteract()
    {
        Simulation.GetModel<PlatformerModel>().player.ToggleBackpack();
    }
}
