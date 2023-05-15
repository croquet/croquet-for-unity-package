using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;

using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// Handles Creation and Destruction of Objects.
/// Maintains the mapping between the model and the view objects.
/// </summary>
public class CroquetEntitySystem : CroquetBridgeExtension
{
    Dictionary<string, GameObject> croquetObjects = new Dictionary<string, GameObject>();

    private Dictionary<string, GameObject> addressableAssets;
    private bool addressablesReady = false;

    // Create Singleton Reference
    public static CroquetEntitySystem Instance { get; private set; }
    
    
    
    /// <summary>
    /// Find GameObject with a specific Croquet ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public GameObject FindObject(string id)
    {
        GameObject obj;
        if (croquetObjects.TryGetValue(id, out obj)) return obj;
        Debug.Log($"Failed to find object {id}");
        return null;
    }

    public List<String> Messages = new List<string>
    {
        "makeObject",
        "destroyObject"
    };
    
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
        
        CroquetBridge.bridge.RegisterBridgeExtension(this);

        addressableAssets = new Dictionary<string, GameObject>();
        StartCoroutine(LoadAddressableAssetsWithLabel(CroquetBridge.bridge.appName));
    }
    
    IEnumerator LoadAddressableAssetsWithLabel(string label)
    {
        // @@ LoadAssetsAsync throws an error - asynchronously - if there are
        // no assets that match the key.  One way to avoid that error is to run
        // the following code to get a list of locations matching the key.
        // If the list is empty, don't run the LoadAssetsAsync.
        // Presumably there are more efficient ways to do this (in particular, when
        // there *are* matches).  Maybe by using the list?

        //Returns any IResourceLocations that are mapped to the supplied label
        AsyncOperationHandle<IList<IResourceLocation>> handle = Addressables.LoadResourceLocationsAsync(label);
        yield return handle;

        IList<IResourceLocation> result = handle.Result;
        int prefabs = 0;
        foreach (var loc in result) {
            if (loc.ToString().EndsWith(".prefab")) prefabs++;
        }
        // int count = result.Count;
        // Debug.Log($"Found {prefabs} addressable prefabs");
        Addressables.Release(handle);

        if (prefabs != 0)
        {
            // Load any assets labelled with this appName from the Addressable Assets
            Addressables.LoadAssetsAsync<GameObject>(label, null).Completed += objects =>
            {
                foreach (var go in objects.Result)
                {
                    Debug.Log($"Addressable Loaded: {go.name}");
                    addressableAssets.Add(go.name.ToLower(), go); // @@ remove case-sensitivity
                }

                addressablesReady = true;
            };
        }
        else
        {
            Debug.Log($"No addressable assets are tagged '{label}'");
            addressablesReady = true;
        }
    }

    public override void ProcessCommand(string command, string[] args)
    {
        if (command.Equals("makeObject"))
        {
            MakeObject(args);
        }
    }
    
    void MakeObject(string[] args)
    {
        ObjectSpec spec = JsonUtility.FromJson<ObjectSpec>(args[0]);
        Debug.Log($"making object {spec.id}");

        // try to find a prefab with the given name
        GameObject obj = null;
        if (!spec.type.StartsWith("primitive"))
        {
            obj = Instantiate(addressableAssets[spec.type.ToLower()]); // @@ remove case-sensitivity
        }
        if (obj == null)
        {
            Debug.Log( $"Specified spec.type ({spec.type}) is not found as a prefab!");
            PrimitiveType type = PrimitiveType.Cube;
            if (spec.type == "primitiveSphere") type = PrimitiveType.Sphere;

            obj = new GameObject(spec.type);
            obj.AddComponent<CroquetGameObject>();
            GameObject inner = GameObject.CreatePrimitive(type);
            inner.transform.parent = obj.transform;
        }

        CroquetGameObject cgo = obj.GetComponent<CroquetGameObject>();
        cgo.croquetGameHandle = spec.id;
        if (spec.type.StartsWith("primitive")) cgo.recolorable = true; // all primitives can take arbitrary colour
        if (spec.cN != "") cgo.croquetActorId = spec.cN;

        if (spec.cs != "")
        {
            string[] comps = spec.cs.Split(',');
            foreach (string compName in comps)
            {
                try
                {
                    Type packageType = Type.GetType(compName);
                    if (packageType != null) obj.AddComponent(packageType);
                    else
                    {
                        string assemblyQualifiedName =
                            System.Reflection.Assembly.CreateQualifiedName("Assembly-CSharp", compName);
                        Type customType = Type.GetType(assemblyQualifiedName);
                        if (customType != null) obj.AddComponent(customType);
                        else Debug.LogError($"Unable to find component {compName} in package or main assembly");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in adding component {compName}: {e}");
                }
            }
        }

        if (cgo.recolorable && spec.c[0] != -1f) // a red value of -1 means "don't recolour"
        {
            Material material = obj.GetComponentInChildren<Renderer>().material;
            if (spec.a != 1f)
            {
                // sorcery from https://forum.unity.com/threads/standard-material-shader-ignoring-setfloat-property-_mode.344557/
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }

            Color color = new Color(spec.c[0], spec.c[1], spec.c[2], spec.a);
            material.SetColor("_Color", color);
        }

        obj.SetActive(!spec.wTA);

        croquetObjects[spec.id] = obj;

        obj.transform.localScale = new Vector3(spec.s[0], spec.s[1], spec.s[2]);
        // normalise the quaternion because it's potentially being sent with reduced precision
        obj.transform.localRotation = Quaternion.Normalize(new Quaternion(spec.r[0], spec.r[1], spec.r[2], spec.r[3]));
        obj.transform.localPosition = new Vector3(spec.t[0], spec.t[1], spec.t[2]);

        if (spec.cC)
        {
            CroquetBridge.bridge.SendToCroquet("objectCreated", spec.id.ToString(), DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());
        }
    }
    
    void DestroyObject(string id)
    {
        Debug.Log( "destroying object " + id.ToString());
        // if (cameraOwnerId == id)
        // {
        //     cameraOwnerId = "";
        //     GameObject camera = GameObject.FindWithTag("MainCamera");
        //     camera.transform.parent = null;
        // }
        
        if (croquetObjects.ContainsKey(id))
        {
            GameObject obj = croquetObjects[id];
            
            //TODO: CALL ALL SYSTEMS ONDESTROY for this ID
            
            Destroy(obj);
            croquetObjects.Remove(id);
        }
        else
        {
            // asking to destroy a pawn for which there's no view can happen just because of
            // creation/destruction timing in worldcore.  not necessarily a problem.
            Debug.Log($"attempt to destroy absent object {id}");
        }
    }
}

[System.Serializable]
public class ObjectSpec
{
    public string id; // currently an integer, but no point converting all the time
    public string cN; // Croquet name (generally, the model id)
    public bool cC; // confirmCreation: whether Croquet is waiting for a confirmCreation message for this 
    public bool wTA; // waitToActivate:  whether to make visible immediately, or only on first posn update
    public string type;
    public string cs; // comma-separated list of extra components
    public float[] c; // color;
    public float a; // alpha;
    public float[] s; // scale;
    public float[] r; // rotation;
    public float[] t; // translation;
}
