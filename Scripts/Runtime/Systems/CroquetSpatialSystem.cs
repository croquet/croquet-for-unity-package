using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetSpatialSystem : CroquetSystem
{
    public override List<String> KnownCommands { get;  } = new()
    {
        "updateSpatial",
        "setParent",
        "unparent",
    };

    protected override Dictionary<int, CroquetComponent> components { get; set; } = new Dictionary<int, CroquetComponent>();

    public Dictionary<int, CroquetComponent> GetComponents()
    {
        return components;
    }

    // Create Singleton Reference
    public static CroquetSpatialSystem Instance { get; private set; }

    private void Awake()
    {
        // Create Singleton Accessor
        // If there is an instance, and it's not me, delete myself.
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    private void Update()
    {
        // Update the transform (position, rotation, scale) in the scene
        UpdateTransforms();
    }

    void Unparent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(args[0]);
        if (child)
        {
            child.transform.SetParent(null);
        }
    }

    void SetParent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(args[0]);
        GameObject parent = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(args[1]);
        if (parent && child)
        {
            child.transform.SetParent(parent.transform, false); // false => ignore child's existing world position
        }
    }

    public override void PawnInitializationComplete(GameObject go)
    {
        CroquetActorManifest manifest = go.GetComponent<CroquetActorManifest>();
        if (manifest != null && Array.IndexOf(manifest.mixins, "Ballistic2D") >= 0)
        {
            float[] p = Croquet.ReadActorFloatArray(go, "position");
            Vector3 initialPos = new Vector3(p[0], p[1], p[2]);
            float[] r = Croquet.ReadActorFloatArray(go, "rotation");
            Quaternion initialRot = new Quaternion(r[0], r[1], r[2], r[3]);
            float[] s = Croquet.ReadActorFloatArray(go, "scale");
            Vector3 initialScale = new Vector3(s[0], s[1], s[2]);

            string croquetHandle = go.GetComponent<CroquetEntityComponent>().croquetHandle;
            SnapObjectTo(croquetHandle, initialPos, initialRot, initialScale);
            // CroquetBridge.Instance.Log("info", $"spatial setup for {go}");
        }
    }

    public override void ActorPropertySet(GameObject go, string propName)
    {
        // we're being notified that a watched property on an object that we are
        // known to have an interest in has changed (or been set for the first time).
        if (propName == "ballisticVelocity")
        {
            float[] v = Croquet.ReadActorFloatArray(go, "ballisticVelocity");
            Vector3 velocity = new Vector3(v[0], v[1], v[2]);
            CroquetSpatialComponent spatial = components[go.GetInstanceID()] as CroquetSpatialComponent;
            spatial.ballisticVelocity = velocity;
            spatial.currentLag = 0; // reset
            // CroquetBridge.Instance.Log("info", $"set ballisticVelocity for {go} (magnitude {velocity.magnitude})");
        }
    }

    // private int telemetryDumpTrigger = -1;

    /// <summary>
    /// Update the Unity Transform Components in the Scene to reflect the latest desired state.
    /// </summary>
    void UpdateTransforms()
    {
        foreach (KeyValuePair<int, CroquetComponent> kvp in components)
        {
            CroquetSpatialComponent spatial = kvp.Value as CroquetSpatialComponent;
            Transform t = spatial.transform; // where the object is right now

            if (Vector3.Distance(spatial.scale,t.localScale) > spatial.scaleDeltaEpsilon)
            {
                t.localScale = Vector3.Lerp(t.localScale, spatial.scale, spatial.scaleLerpFactor);
            }
            if (Quaternion.Angle(spatial.rotation,t.localRotation) > spatial.rotationDeltaEpsilon)
            {
                t.localRotation = Quaternion.Slerp(t.localRotation, spatial.rotation, spatial.rotationLerpFactor);
            }
            if (spatial.ballisticVelocity != null)
            {
                Vector3 modelPos = spatial.position;
                float deltaTime = Time.deltaTime; // expected to be around 17ms
                if (spatial.currentLag < spatial.desiredLag)
                {
                    // whenever ballisticVelocity is set, we immediately start calculating where
                    // the object will have got to.  but because of the latency in the bridge,
                    // we need to introduce a lag in our computation so that if the model
                    // announces a change in velocity (say, a bounce), the object doesn't
                    // overshoot during the time it takes to receive that announcement. here
                    // we gradually lengthen the lag, up to the desired level (set as
                    // defaultLag on the Spatial component, which defaults to 50ms).
                    float lagDelta = deltaTime / 5;
                    spatial.currentLag += lagDelta;
                    // if (spatial.currentLag >= spatial.desiredLag) CroquetBridge.Instance.Log("info","hit desired lag");
                    deltaTime -= lagDelta; // compute that much less of a move
                }
                Vector3 movement = deltaTime * spatial.ballisticVelocity.Value;
                t.localPosition += movement;
                // check in 2D for drift away from the desired path (typically arising from
                // a bounce happening when the object was some distance from the bounce point)
                Vector2 offsetFromModel =
                    new Vector2(t.localPosition.x - modelPos.x, t.localPosition.z - modelPos.z);
                Vector2 velocityUnit =
                    new Vector2(spatial.ballisticVelocity.Value.x, spatial.ballisticVelocity.Value.z).normalized;
                Vector2 perpendicularUnit = new Vector2(velocityUnit.y, -velocityUnit.x);
                float aheadOnPath = Vector2.Dot(offsetFromModel, velocityUnit);
                float offToSide = Vector2.Dot(offsetFromModel, perpendicularUnit);

                if (Mathf.Abs(offToSide) > 0.01f)
                {
                    float nudgeDist = -offToSide * spatial.positionLerpFactor;
                    Vector3 nudge = new Vector3(perpendicularUnit.x * nudgeDist, 0,
                        perpendicularUnit.y * nudgeDist);
                    t.localPosition += nudge;
                    // CroquetBridge.Instance.Log("info", $"nudge by {nudge.ToString()}");
                }

                // string telemetryString =
                //     $"{DateTimeOffset.Now.ToUnixTimeMilliseconds() % 100000}: bv={TmpFormatVector3(spatial.ballisticVelocity.Value)} model={TmpFormatVector3(modelPos)} localP={TmpFormatVector3(t.localPosition)} ahead={aheadOnPath.ToString("0.00")} side={offToSide.ToString("0.00")}";
                // spatial.telemetry.Add(telemetryString);
                // if (spatial.telemetry.Count > 20) spatial.telemetry.RemoveAt(0);
                // if (Mathf.Abs(aheadOnPath) > 7f)
                // {
                //     if (telemetryDumpTrigger == -1) telemetryDumpTrigger = 6;
                // }
                //
                // if (telemetryDumpTrigger > 0)
                // {
                //     telemetryDumpTrigger--;
                //     if (telemetryDumpTrigger == 0)
                //     {
                //         Debug.Log("VVVVVVVVVVVVVVVVVVVV");
                //         foreach (string s in spatial.telemetry)
                //         {
                //             Debug.Log(s);
                //         }
                //
                //         Debug.Log("^^^^^^^^^^^^^^^^^^^^");
                //     }
                // }

                spatial.hasBeenMoved = true;
            } else if (Vector3.Distance(spatial.position,t.localPosition) > spatial.positionDeltaEpsilon)
            {
                t.localPosition = Vector3.Lerp(t.localPosition, spatial.position, spatial.positionLerpFactor);
            }
        }
    }

    // string TmpFormatVector3(Vector3 vec)
    // {
    //     return $"[{vec.x.ToString("0.00")}, {vec.z.ToString("0.00")}]";
    // }

    /// <summary>
    /// Processing messages from Croquet to update the spatial component
    /// </summary>
    /// <param name="rawData">TODO:Aran</param>
    /// <param name="startPos">TODO:Aran</param>
    /// <returns></returns>
    void UpdateSpatial(byte[] rawData, int startPos)
    {
        const uint SCALE =      0b100000;
        const uint SCALE_SNAP = 0b010000;
        const uint ROT =        0b001000;
        const uint ROT_SNAP =   0b000100;
        const uint POS =        0b000010;
        const uint POS_SNAP =   0b000001;

        int bufferPos = startPos; // byte index through the buffer
        while (bufferPos < rawData.Length)
        {
            // first number encodes object id and (in bits 0-5) whether there is an update (with/without
            // a snap) for each of scale, rotation, translation.  this leaves room for 2**26
            // possible ids - i.e., around 67 million.  that seems more than enough for any given
            // instant, but if some app creates and destroys thousands of entities per second, we
            // would need some kind of id recycling so we don't run out.
            UInt32 encodedId = BitConverter.ToUInt32(rawData, bufferPos);
            bufferPos += 4;
            string croquetHandle = (encodedId >> 6).ToString();

            int instanceID = CroquetEntitySystem.GetInstanceIDByCroquetHandle(croquetHandle);

            CroquetSpatialComponent spatialComponent;
            Transform trans;
            try
            {
                spatialComponent = components[instanceID] as CroquetSpatialComponent;
                trans = spatialComponent.transform;
            }
            catch (Exception e)
            {
                // object not found.  skip through the buffer to the next object's record.
                if ((encodedId & SCALE) != 0)
                {
                    bufferPos += 12;
                }
                if ((encodedId & ROT) != 0)
                {
                    bufferPos += 16;
                }
                if ((encodedId & POS) != 0)
                {
                    bufferPos += 12;
                }
                Debug.Log($"attempt to update absent object {croquetHandle} : {e}");
                continue;
            }

            // first time through, set hasBeenPlaced
            // second time, set hasBeenMoved
            if (!spatialComponent.hasBeenMoved)
            {
                if (spatialComponent.hasBeenPlaced) spatialComponent.hasBeenMoved = true;
                spatialComponent.hasBeenPlaced = true;
            }

            if ((encodedId & SCALE) != 0)
            {
                Vector3 updatedScale = Vector3FromBuffer(rawData, bufferPos);
                bufferPos += 12;
                if ((encodedId & SCALE_SNAP) != 0)
                {
                    // immediately snap scale
                    trans.localScale = updatedScale;
                }
                spatialComponent.scale = updatedScale;
                // Log("verbose", "scale: " + updatedScale.ToString());
            }
            if ((encodedId & ROT) != 0)
            {
                Quaternion updatedQuatRot = QuaternionFromBuffer(rawData, bufferPos);
                bufferPos += 16;
                if ((encodedId & ROT_SNAP) != 0)
                {
                    trans.localRotation = updatedQuatRot;
                }
                spatialComponent.rotation = updatedQuatRot;
                // Log("verbose", "rot: " + updatedQuatRot.ToString());
            }
            if ((encodedId & POS) != 0)
            {
                Vector3 updatedPosition = Vector3FromBuffer(rawData, bufferPos);
                bufferPos += 12;
                if ((encodedId & POS_SNAP) != 0)
                {
                    trans.localPosition = updatedPosition;
                }
                spatialComponent.position = updatedPosition;
                // Log("verbose", "pos: " + updatedPosition.ToString());
            }
        }
    }

    public bool hasObjectMoved(int instanceID)
    {
        return (components[instanceID] as CroquetSpatialComponent).hasBeenMoved;
    }

    public bool hasObjectMoved(string croquetHandle)
    {
        int instanceID = CroquetEntitySystem.GetInstanceIDByCroquetHandle(croquetHandle);
        return (components[instanceID] as CroquetSpatialComponent).hasBeenMoved;
    }

    public void SnapObjectTo(string croquetHandle, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
    {
        int instanceID = CroquetEntitySystem.GetInstanceIDByCroquetHandle(croquetHandle);
        if (instanceID == 0) return;

        CroquetSpatialComponent spatial = components[instanceID] as CroquetSpatialComponent;
        Transform trans = spatial.transform;

        if (position != null)
        {
            trans.localPosition = position.Value;
            spatial.position = position.Value;
        }

        if (rotation != null)
        {
            trans.localRotation = rotation.Value;
            spatial.rotation = rotation.Value;
        }

        if (scale != null)
        {
            trans.localScale = scale.Value;
            spatial.scale = scale.Value;
        }
    }

    public void SnapObjectInCroquet(string croquetHandle, Vector3? position = null, Quaternion? rotation = null,
        Vector3? scale = null)
    {
        List<string> argList = new List<string>();
        argList.Add("objectMoved");
        argList.Add(croquetHandle);

        if (position != null)
        {
            argList.Add("p");
            argList.Add(string.Join<float>(",", new[] { position.Value.x, position.Value.y, position.Value.z }));
        }

        if (rotation != null)
        {
            argList.Add("r");
            argList.Add(string.Join<float>(",", new[] { rotation.Value.x, rotation.Value.y, rotation.Value.z, rotation.Value.w }));
        }

        if (scale != null)
        {
            argList.Add("p");
            argList.Add(string.Join<float>(",", new[] { scale.Value.x, scale.Value.y, scale.Value.z }));
        }

        CroquetBridge.Instance.SendThrottledToCroquet("objectMoved_" + croquetHandle, argList.ToArray());
    }

    public override void ProcessCommand(string command, string[] args)
    {
        if (command.Equals("setParent"))
        {
            // associate parent
            SetParent(args);
        }
        else if (command.Equals("unparent"))
        {
            // unassociate parent
            Unparent(args);
        }
    }

    public override void ProcessCommand(string command, byte[] data, int startIndex)
    {
        if (command.Equals("updateSpatial"))
        {
            UpdateSpatial(data, startIndex);// TODO ARAN: together fix the data format coming in
        }
    }

    Quaternion QuaternionFromBuffer(byte[] rawData, int startPos)
    {
        return new Quaternion(
            BitConverter.ToSingle(rawData, startPos),
            BitConverter.ToSingle(rawData, startPos + 4),
            BitConverter.ToSingle(rawData, startPos + 8),
            BitConverter.ToSingle(rawData, startPos + 12)
        );
    }

    Vector3 Vector3FromBuffer(byte[] rawData, int startPos)
    {
        return new Vector3(
            BitConverter.ToSingle(rawData, startPos),
            BitConverter.ToSingle(rawData, startPos + 4),
            BitConverter.ToSingle(rawData, startPos + 8)
        );
    }
}

