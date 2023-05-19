using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Croquet/MaterialComponent")]
public class CroquetMaterialComponent : CroquetComponent
{
    public Color color;
    public override CroquetSystem croquetSystem { get; set; } = CroquetMaterialSystem.Instance;
}
