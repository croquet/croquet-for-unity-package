using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetInteractableComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetInteractableSystem.Instance;

    public bool isInteractable = true;

    public string[] interactableLayers = new string[]{};


}
