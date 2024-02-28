using UnityEngine;

[AddComponentMenu("Croquet/DrivableComponent")]
public class CroquetDrivableComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetDrivableSystem.Instance;

    public bool isDrivenByThisView;
}
