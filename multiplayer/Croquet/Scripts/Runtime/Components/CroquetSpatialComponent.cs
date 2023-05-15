using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Croquet/SpatialComponent")]
public class CroquetSpatialComponent : MonoBehaviour
{
    [Header("Model Transform (read-only)")]
    [SerializeField] private Vector3 position;
    [SerializeField] private Vector3 rotation;
    [SerializeField] private Vector3 scale;
    
    [Header("Options")]
    public bool initializeWithSceneTransform = false;
    public bool linearInterpolation = false;
}
