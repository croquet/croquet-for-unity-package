using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

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
    private Stopwatch stopWatch;

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

    private void Start()
    {
        stopWatch = new Stopwatch();
        stopWatch.Start();

        AddWrapRenderHandler(HandleRenderForSpatials);
    }

    private void OnDestroy()
    {
        RemoveWrapRenderHandler(HandleRenderForSpatials);
    }

    public override List<string> InitializationStringsForObject(GameObject go)
    {
        // placement doesn't depend on having a SpatialComponent
        CroquetSpatialComponent sc = go.GetComponent<CroquetSpatialComponent>(); // if any

        Transform t = go.transform;
        List<string> strings = new List<string>();
        Vector3 position = t.position;
        if (position.magnitude > (sc ? sc.positionEpsilon : 0.01f))
        {
            int precision = sc ? sc.positionMaxDecimals : 4;
            strings.Add($"pos:{FormatFloats(new []{position.x,position.y,position.z}, precision)}");
        }
        Quaternion rotation = t.rotation;
        if (Quaternion.Angle(rotation,Quaternion.identity) > (sc ? sc.rotationEpsilon : 0.01f))
        {
            int precision = sc ? sc.rotationMaxDecimals : 6;
            strings.Add($"rot:{FormatFloats(new []{rotation.x,rotation.y,rotation.z,rotation.w}, precision)}");
        }
        Vector3 scale = t.lossyScale;
        if (Vector3.Distance(scale,new Vector3(1f, 1f, 1f)) > (sc ? sc.scaleEpsilon : 0.01f))
        {
            int precision = sc ? sc.scaleMaxDecimals : 4;
            strings.Add($"scale:{FormatFloats(new []{scale.x,scale.y,scale.z}, precision)}");
        }

        if (sc && sc.includeOnSceneInit)
        {
            strings.Add($"spatialOptions:{PackedOptionValues(sc)}");
        }

        return strings;
    }

    string FormatFloats(float[] floats, int precision)
    {
        string formatter = $"0.{new string('0', precision)}";
        string[] strings = Array.ConvertAll(floats, f =>
        {
            // suggestion adapted from https://stackoverflow.com/questions/4525854/remove-trailing-zeros
            string strdec = f.ToString(formatter);
            return strdec.Contains(".") ? strdec.TrimEnd('0').TrimEnd('.') : strdec;
        });
        return string.Join(',', strings);
    }

    private string PackedOptionValues(CroquetSpatialComponent spatial)
    {
        float[] props = new float[]
        {
            spatial.positionSmoothTime,
            spatial.positionEpsilon,
            spatial.rotationLerpPerFrame,
            spatial.rotationEpsilon,
            spatial.scaleLerpPerFrame,
            spatial.scaleEpsilon,
            spatial.desiredLag,
            spatial.ballisticNudgeLerp
        };

        return string.Join<float>(',', props);
    }

    private void UnpackOptionValues(string packedValues, CroquetSpatialComponent spatial)
    {
        float[] props = Array.ConvertAll(packedValues.Split(','), float.Parse);
        spatial.positionSmoothTime = props[0];
        spatial.positionEpsilon = props[1];
        spatial.rotationLerpPerFrame = props[2];
        spatial.rotationEpsilon = props[3];
        spatial.scaleLerpPerFrame = props[4];
        spatial.scaleEpsilon = props[5];
        spatial.desiredLag = props[6];
        spatial.ballisticNudgeLerp = props[7];
    }

    private void Update()
    {
        // Update the transform (position, rotation, scale) in the scene
        // - but only if the scene is running
        if (CroquetBridge.Instance.unitySceneState == "running")
        {
            UpdateTransforms();
        }
    }

    void Unparent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(int.Parse(args[0]));
        if (child)
        {
            child.transform.SetParent(null);
        }
    }

    void SetParent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(int.Parse(args[0]));
        GameObject parent = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(int.Parse(args[1]));
        if (parent && child)
        {
            child.transform.SetParent(parent.transform, false); // false => ignore child's existing world position
        }
    }

    public override void PawnInitializationComplete(GameObject go)
    {
        if (Croquet.HasActorSentProperty(go, "spatialOptions"))
        {
            string options = Croquet.ReadActorString(go, "spatialOptions");
            CroquetSpatialComponent spatial = go.GetComponent<CroquetSpatialComponent>();
            UnpackOptionValues(options, spatial);
        }

        CroquetActorManifest manifest = go.GetComponent<CroquetActorManifest>();
        if (manifest != null && Array.IndexOf(manifest.mixins, "Ballistic2D") >= 0)
        {
            float[] p = Croquet.ReadActorFloatArray(go, "position");
            Vector3 initialPos = new Vector3(p[0], p[1], p[2]);
            float[] r = Croquet.ReadActorFloatArray(go, "rotation");
            Quaternion initialRot = new Quaternion(r[0], r[1], r[2], r[3]);
            initialRot.Normalize(); // probably ok, but doesn't hurt to confirm
            float[] s = Croquet.ReadActorFloatArray(go, "scale");
            Vector3 initialScale = new Vector3(s[0], s[1], s[2]);

            int croquetHandle = go.GetComponent<CroquetEntityComponent>().croquetHandle;
            DrivePawn(croquetHandle, initialPos, initialRot, initialScale);
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
            if (spatial.viewOverride) continue; // view has control right now

            Transform t = spatial.transform; // where the object is right now

            if (Vector3.Distance(spatial.scale,t.localScale) > spatial.scaleEpsilon)
            {
                t.localScale = Vector3.Lerp(t.localScale, spatial.scale, spatial.scaleLerpPerFrame);
            }
            if (Quaternion.Angle(spatial.rotation,t.localRotation) > spatial.rotationEpsilon)
            {
                t.localRotation = Quaternion.Slerp(t.localRotation, spatial.rotation, spatial.rotationLerpPerFrame);
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
                    float nudgeDist = -offToSide * spatial.ballisticNudgeLerp;
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
            } else if (Vector3.Distance(spatial.position,t.localPosition) > spatial.positionEpsilon)
            {
                // SmoothDamp seems better suited to our needs than a constant lerp
                t.localPosition = Vector3.SmoothDamp(t.localPosition, spatial.position,
                        ref spatial.dampedVelocity, spatial.positionSmoothTime);
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
        const uint POS_CONTINUOUS = 0b000010;
        const uint POS_SNAP =   0b000001;
        const uint POS_ANY = POS_CONTINUOUS | POS_SNAP;

        int bufferPos = startPos; // byte index through the buffer
        while (bufferPos < rawData.Length)
        {
            // first number encodes object id and (in bits 0-5) whether there is an update (with/without
            // a snap) for each of scale, rotation, translation.  this leaves room for 2**26
            // possible ids - i.e., around 67 million.
            // jul 2023: we now implement id recycling: the next id after 999,999 is 1 (or the first
            // handle after 1 that's not still being used by a pawn).  if a million handles available
            // at once isn't enough, we can increase it.
            UInt32 encodedId = BitConverter.ToUInt32(rawData, bufferPos);
            bufferPos += 4;
            int croquetHandle = (int)(encodedId >> 6);

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
                if ((encodedId & POS_ANY) != 0)
                {
                    bufferPos += 12;
                }
                Debug.Log($"attempt to update absent object {croquetHandle} : {e}");
                continue;
            }

            long nowMS = stopWatch.ElapsedMilliseconds;

            // first time through, set hasBeenPlaced
            // second time, set hasBeenMoved
            if (!spatialComponent.hasBeenMoved)
            {
                if (spatialComponent.hasBeenPlaced) spatialComponent.hasBeenMoved = true;
                else
                {
                    spatialComponent.hasBeenPlaced = true;
                    spatialComponent.lastGeometryUpdate = nowMS;
                }
            }

            long msSinceLastUpdate = nowMS - spatialComponent.lastGeometryUpdate;
            spatialComponent.lastGeometryUpdate = nowMS;

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
            if ((encodedId & POS_ANY) != 0)
            {
                Vector3 updatedPosition = Vector3FromBuffer(rawData, bufferPos);
                bufferPos += 12;
                if ((encodedId & POS_SNAP) != 0)
                {
                    // oct 2023: if both SNAP and CONTINUOUS are set, it means we're being snapped
                    // while on the move (e.g., wrapping over a world boundary).  for smooth movement,
                    // we calculate our offset not just from the spatialComponent's current recorded
                    // position but from where we estimate that position would have been moved to if this
                    // update were continuous rather than a snap.  then we apply that offset to the
                    // supplied snap position.
                    // an object can be poised for seconds or minutes on a world edge before getting the
                    // nudge that moves it across.  a communication glitch can introduce false delays.
                    // only attempt velocity adjustment within 150ms since the last known update.
                    if ((encodedId & POS_CONTINUOUS) != 0)
                    {
                        if (msSinceLastUpdate <= 150)
                        {
                            Vector3 velocity = spatialComponent.ballisticVelocity.HasValue
                                ? spatialComponent.ballisticVelocity.Value
                                : spatialComponent.dampedVelocity;
                            Vector3 expectedMove = velocity * (float)msSinceLastUpdate / 1000f;
                            Vector3 trackingLag = spatialComponent.position + expectedMove - trans.position;
                            trans.localPosition = updatedPosition - trackingLag;
                        }
                        else trans.localPosition = updatedPosition;
                    }
                    else
                    {
                        trans.localPosition = updatedPosition;
                        spatialComponent.dampedVelocity = Vector3.zero; // a snap that stops you
                    }
                }

                // available diagnostics for object movement.  at any time a single object can be identified
                // for tracking (see SetDiagnosticTrackedObject).  if trackedObject is assigned - and until
                // reassigned, or the object is destroyed - each position update is logged to the console.
                if (trackedObject != null && spatialComponent.gameObject == trackedObject)
                {
                    long deltaTMS = msSinceLastUpdate;
                    // float dist = Vector3.Distance(trans.localPosition, updatedPosition); // distance of object now from the new position
                    float dist = Vector3.Distance(spatialComponent.position, updatedPosition); // distance of (pre-adjusted) spatial from new position
                    float v = dist / ((float)(deltaTMS) / 1000f);
                    CroquetBridge.Instance.Log("session",$"after {deltaTMS}ms: d={dist:F3} implying v={v:F3}");
                }

                spatialComponent.position = updatedPosition;
                // Log("verbose", "pos: " + updatedPosition.ToString());
            }
        }
    }

    public void DrivePawn(int croquetHandle, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
    {
        // to impose a view-side update on a pawn gameObject's placement, invoke this to force
        // the updated values into the gameObject's SpatialComponent, as used by the SpatialSystem
        // for the object's reference location.
        // this does *not* communicate the update to Croquet.  if the corresponding actor moves,
        // the sending of that movement over the bridge will override what has been set here.
        // in the case of a "drivable" (i.e., view-controlled) gameObject, the standard practice
        // is to invoke DriveActor in addition to this.  that will update the position in the
        // Croquet model, but only views other than the driving one will be told about the change.

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

    public void DriveActor(int croquetHandle, bool useSnap, Vector3? position = null, Quaternion? rotation = null,
        Vector3? scale = null)
    {
        // as explained above, this is for imposing a view-driven update on a Croquet actor.  the information
        // supplied here instantly overwrites the actor's state, and is communicated to the pawn either with
        // or without a "snap", depending on the useSnap argument.

        List<string> argList = new List<string>();
        argList.Add("objectMoved");
        argList.Add(croquetHandle.ToString());
        argList.Add(useSnap.ToString());

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
            argList.Add("s");
            argList.Add(string.Join<float>(",", new[] { scale.Value.x, scale.Value.y, scale.Value.z }));
        }

        CroquetBridge.Instance.SendThrottledToCroquet($"objectMoved_{croquetHandle}", argList.ToArray());
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
        Quaternion q = new Quaternion(
            BitConverter.ToSingle(rawData, startPos),
            BitConverter.ToSingle(rawData, startPos + 4),
            BitConverter.ToSingle(rawData, startPos + 8),
            BitConverter.ToSingle(rawData, startPos + 12)
        );
        q.Normalize(); // probably ok, but doesn't hurt to confirm
        return q;
    }

    Vector3 Vector3FromBuffer(byte[] rawData, int startPos)
    {
        return new Vector3(
            BitConverter.ToSingle(rawData, startPos),
            BitConverter.ToSingle(rawData, startPos + 4),
            BitConverter.ToSingle(rawData, startPos + 8)
        );
    }

    private GameObject trackedObject = null;
    public void SetDiagnosticTrackedObject(GameObject go)
    {
        Debug.Log($"tracking new object: {go}");
        trackedObject = go;
    }

    // --------------------------------------------------------------------------------------
    // WORLD WRAPPING
    // --------------------------------------------------------------------------------------

    // we number the quadrants clockwise from top left, with zero in the centre.
    // 1   2   3
    // 8   0   4
    // 7   6   5
    float[,] allQuadrants =
    {
        { 0, 0 },       // centre
        { -1f, 1f },    // -x+z "1"
        { 0, 1f },      // +z   "2"
        { 1f, 1f },     // +x+z "3"
        { 1f, 0 },      // +x   "4"
        { 1f, -1f },    // +x-z "5"
        { 0, -1f },     // -z   "6"
        { -1f, -1f },   // -x-z "7"
        { -1f, 0 },     // -x   "8"
    };

    private Vector3 cameraPos, changeX, changeXInViewSpace, changeZ, changeZInViewSpace;
    private Vector3 outOfSight;

    private float viewX, viewZ, quadX, quadZ;
    private Quaternion intoViewSpace, intoWorldSpace;

    bool needToSearch;
    List<(Vector3, Vector3)> possibleAdjustments;

    List<Action<bool>> wrapRenderHandlers = new List<Action<bool>>();

    Vector3 WorldToView(Vector3 worldPos)
    {
        return intoViewSpace * (worldPos - cameraPos);
    }

    Vector3 ViewToWorld(Vector3 viewPos)
    {
        return intoWorldSpace * viewPos + cameraPos;
    }

    bool AppearsInView(Vector3 viewPos)
    {
        return viewPos.x >= -viewX && viewPos.x <= viewX && viewPos.z >= -viewZ && viewPos.z <= viewZ;
    }

    public void AddWrapRenderHandler(Action<bool> action)
    {
        wrapRenderHandlers.Add(action);
    }
    public void RemoveWrapRenderHandler(Action<bool> action)
    {
        wrapRenderHandlers.Remove(action);
    }

    public void PositionObjectsForRender(Camera cam, float viewWidth, float viewHeight, float worldWidth, float worldHeight)
    {
        if (wrapRenderHandlers.Count == 0) return; // no-one has any objects they want to map

        float viewDiagonal = Mathf.Sqrt(viewWidth * viewWidth + viewHeight * viewHeight);
        if (viewDiagonal >= worldWidth || viewDiagonal >= worldHeight)
        {
            Debug.LogWarning(
                $"Diagonal in view size {viewWidth}x{viewHeight} exceeds world size {worldWidth}x{worldHeight}.");
        }

        cameraPos = cam.transform.position;
        Quaternion q = cam.transform.rotation;
        float yaw = Mathf.Rad2Deg * Mathf.Atan2(2 * q.y * q.w - 2 * q.x * q.z, 1 - 2 * q.y * q.y - 2 * q.z * q.z);
        // Debug.Log($"yaw: {yaw}");
        intoViewSpace = Quaternion.AngleAxis(-yaw, Vector3.up);
        intoWorldSpace = Quaternion.AngleAxis(yaw, Vector3.up);

        // size of the view
        viewX = viewWidth / 2;
        viewZ = viewHeight / 2;

        // size of the wrapping world
        quadX = worldWidth / 2;
        quadZ = worldHeight / 2;

        // offsets for moving into neighbouring quadrants, in world and view spaces
        changeX = new Vector3(worldWidth, 0, 0);
        changeZ = new Vector3(0, 0, worldHeight);
        changeXInViewSpace = intoViewSpace * changeX;
        changeZInViewSpace = intoViewSpace * changeZ;

        // figure out which outer quadrants are currently intersected by the view.

        List<int> quadsToSearch = new List<int>();
        quadsToSearch.Add(0); // if there are multiple quads to search, we'll always start with the central one

        float xSign = Mathf.Sign(cameraPos.x);
        float zSign = Mathf.Sign(cameraPos.z);
        if (xSign != 0 || zSign != 0) // no possible out-of-bounds if both are zero (unlikely!)
        {
            HashSet<int> quadsSet = new HashSet<int>();

            // first, if we find that the nearest corner of the central quad appears inside the view (which
            // can only be true for one, if their relative sizes have been chosen appropriately), then all three
            // quadrants outside that corner are of interest.  look no further.
            if (xSign != 0 && zSign != 0) // no possible overlap of a quad corner if either is zero
            {
                // corners in the order (-x, z), (x, z), (-x, -z), (x, -z)
                // i.e., +x adds 1; -z adds 2
                int[,] cornerQuads = { { 8, 1, 2 }, { 2, 3, 4 }, { 6, 7, 8 }, { 4, 5, 6 } };
                int cornerIndex = (xSign >= 0 ? 1 : 0) + (zSign < 0 ? 2 : 0);
                Vector3 quadCorner = new Vector3(xSign * quadX, 0, zSign * quadZ);
                if (AppearsInView(WorldToView(quadCorner)))
                {
                    quadsSet.Add(cornerQuads[cornerIndex, 0]);
                    quadsSet.Add(cornerQuads[cornerIndex, 1]);
                    quadsSet.Add(cornerQuads[cornerIndex, 2]);
                }
            }

            if (quadsSet.Count == 0)
            {
                // the quad corner isn't overlapped.  next, look at the corners of the view.  for each
                // corner that is outside the central quad, add the side quadrant on that overflow edge.
                // two view corners can be out on the same side, which is why we're using a HashSet.
                for (int xSide = 0; xSide < 2; xSide++)
                {
                    float cornerX = xSide == 0 ? -viewX : viewX;
                    for (int zSide = 0; zSide < 2; zSide++)
                    {
                        float cornerZ = zSide == 0 ? -viewZ : viewZ;
                        Vector3 cornerInWorld = ViewToWorld(new Vector3(cornerX, 0, cornerZ));
                        if (cornerInWorld.x < -quadX) quadsSet.Add(8);
                        else if (cornerInWorld.x > quadX) quadsSet.Add(4);
                        if (cornerInWorld.z < -quadZ) quadsSet.Add(6);
                        else if (cornerInWorld.z > quadZ) quadsSet.Add(2);
                    }
                }
            }

            foreach (int quad in quadsSet) quadsToSearch.Add(quad);
        }

        needToSearch = quadsToSearch.Count > 1;
        possibleAdjustments = null;
        if (needToSearch)
        {
            possibleAdjustments = new List<(Vector3, Vector3)>();
            foreach (int quad in quadsToSearch)
            {
                float xFactor = allQuadrants[quad, 0];
                float zFactor = allQuadrants[quad, 1];
                possibleAdjustments.Add((xFactor * changeXInViewSpace + zFactor * changeZInViewSpace,
                    xFactor * changeX + zFactor * changeZ));
            }
        }

        // now that the mapping is all set up, tell the action handlers that will apply it
        // to the objects under their control.
        foreach (Action<bool> action in wrapRenderHandlers) action(true); // true => is pre-render
    }

    public Vector3? RenderPositionForPoint(Vector3 point)
    {
        // allow for a base position that's outside the central quadrant
        // (e.g., from a previous render adjustment)
        float baseX = point.x, baseZ = point.z;
        while (baseX > quadX) baseX -= quadX * 2;
        while (baseX < -quadX) baseX += quadX * 2;
        while (baseZ > quadZ) baseZ -= quadZ * 2;
        while (baseZ < -quadZ) baseZ += quadZ * 2;

        Vector3 basePosition = new Vector3(baseX, 0, baseZ);
        Vector3 inViewSpace = WorldToView(basePosition);

        if (!needToSearch) return AppearsInView(inViewSpace) ? basePosition : null;

        foreach ((Vector3 viewAdj, Vector3 worldAdj) in possibleAdjustments)
        {
            Vector3 adjusted = inViewSpace + viewAdj;
            if (AppearsInView(adjusted)) return basePosition + worldAdj;
        }

        return null;
    }

    private void HandleRenderForSpatials(bool isPreRender)
    {
        Vector3 outOfSight = new Vector3(-1000f, 0, 0);

        foreach (CroquetComponent c in CroquetSpatialSystem.Instance.GetComponents().Values)
        {
            CroquetSpatialComponent spatial = c as CroquetSpatialComponent;
            if (isPreRender)
            {
                Vector3 basePosition = spatial.transform.position;
                spatial.stashedPosition = basePosition;
                Vector3? mappedPosition = RenderPositionForPoint(basePosition);
                spatial.transform.position = mappedPosition.HasValue ? mappedPosition.Value : outOfSight;
            }
            else
            {
                spatial.transform.position = spatial.stashedPosition;
            }
        }
    }

    public void RestoreObjectPositions()
    {
        foreach (Action<bool> action in wrapRenderHandlers) action(false); // false => is post-render
    }
}

