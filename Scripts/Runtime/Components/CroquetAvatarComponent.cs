using UnityEngine;

[AddComponentMenu("Croquet/AvatarComponent")]
public class CroquetAvatarComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetAvatarSystem.Instance;

    public bool isActiveAvatar;

}
