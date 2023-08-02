using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;

using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// Handles Creation and Destruction of Objects.
/// Maintains the mapping between the model and the view objects.
/// </summary>
public class CroquetEntitySystem : CroquetSystem
{
    // manages preloading the addressableAssets
    private Dictionary<string, GameObject> addressableAssets;
    private string assetScene = ""; // the scene for which we've loaded the assets
    private int assetLoadKey = 0; // to distinguish the asynchronous loads
    public string assetManifestString;

    public bool addressablesReady = false; // make public read or emit event to inform other systems that the assets are loaded

    // Create Singleton Reference
    public static CroquetEntitySystem Instance { get; private set; }

    public override List<string> KnownCommands { get; } = new List<string>()
    {
        "makeObject",
        "destroyObject"
    };

    protected override Dictionary<int, CroquetComponent> components { get; set; } =
        new Dictionary<int, CroquetComponent>();

    private Dictionary<string, int> CroquetHandleToInstanceID = new Dictionary<string, int>();

    private void AssociateCroquetHandleToInstanceID(string croquetHandle, int id)
    {
        CroquetHandleToInstanceID.Add(croquetHandle, id);
    }

    private void DisassociateCroquetHandleToInstanceID(string croquetHandle)
    {
        CroquetHandleToInstanceID.Remove(croquetHandle);
    }

    /// <summary>
    /// Get GameObject with a specific Croquet Handle
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public GameObject GetGameObjectByCroquetHandle(string croquetHandle)
    {
        CroquetComponent croquetComponent;

        if (CroquetHandleToInstanceID.ContainsKey(croquetHandle))
        {
            int instanceID = CroquetHandleToInstanceID[croquetHandle];
            if (components.TryGetValue(instanceID, out croquetComponent))
            {
                return croquetComponent.gameObject;
            }
        }

        Debug.Log($"Failed to find object {croquetHandle}");
        return null;
    }

    public static int GetInstanceIDByCroquetHandle(string croquetHandle)
    {
        GameObject go = Instance.GetGameObjectByCroquetHandle(croquetHandle);
        if (go != null)
        {
            return go.GetInstanceID();
        }

        return 0; // TODO: remove sentinel in favor of unwrapping optional
    }

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
            addressableAssets = new Dictionary<string, GameObject>();
        }
    }

    private void Start()
    {
    }

    public override void LoadedScene(string sceneName)
    {
        base.LoadedScene(sceneName);

        // this is sent *after* switching the scene
        if (sceneName == assetScene) return; // already loaded (or being searched for)

        assetScene = sceneName;
        addressablesReady = false;
        assetLoadKey++;
        StartCoroutine(LoadAddressableAssetsWithLabel(sceneName)); // NB: used to be the appName (despite what our docs said)
    }

    public override bool ReadyToRunScene(string sceneName)
    {
        return assetScene == sceneName && addressablesReady;
    }

    public override void TearDownScene()
    {
        // destroy everything in the scene, in preparation either for rebuilding the same scene after
        // a connection glitch or for loading/reloading due to a requested scene change.

        List<CroquetComponent> componentsToDelete = components.Values.ToList();
        foreach (CroquetComponent component in componentsToDelete)
        {
            CroquetEntityComponent entityComponent = component as CroquetEntityComponent;
            if (entityComponent != null)
            {
                DestroyObject(entityComponent.croquetHandle);
            }
        }

        base.TearDownScene();
    }

    public List<GameObject> UninitializedObjectsInScene()
    {
        List<GameObject> needingInit = new List<GameObject>();
        foreach (CroquetComponent c in components.Values)
        {
            CroquetEntityComponent ec = c as CroquetEntityComponent;
            if (ec.croquetHandle.Equals(""))
            {
                needingInit.Add(ec.gameObject);
            }
        }

        return needingInit;
    }

    IEnumerator LoadAddressableAssetsWithLabel(string sceneName)
    {
        // @@ LoadAssetsAsync throws an error - asynchronously - if there are
        // no assets that match the key.  One way to avoid that error is to run
        // the following code to get a list of locations matching the key.
        // If the list is empty, don't run the LoadAssetsAsync.
        // Presumably there are more efficient ways to do this (in particular, when
        // there *are* matches).  Maybe by using the list?

        int key = assetLoadKey;

        //Returns any IResourceLocations that are mapped to the supplied label
        AsyncOperationHandle<IList<IResourceLocation>> handle = Addressables.LoadResourceLocationsAsync(sceneName);
        yield return handle;

        if (key != assetLoadKey) yield break; // scene has changed while assets were being found

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
            Addressables.LoadAssetsAsync<GameObject>(sceneName, null).Completed += objects =>
            {
                // check again that the scene hasn't been changed during the async operation
                if (key == assetLoadKey)
                {
                    addressableAssets.Clear(); // now that we're ready to fill it
                    foreach (var go in objects.Result)
                    {
                        CroquetActorManifest manifest = go.GetComponent<CroquetActorManifest>();
                        if (manifest != null)
                        {
                            string assetName = manifest.pawnType;
                            Debug.Log($"Loaded asset for {assetName} pawnType");
                            addressableAssets.Add(assetName, go);
                        }
                    }

                    addressablesReady = true;
                    // prepare this now, because trying within the Socket's OnOpen
                    // fails.  presumably a thread issue.
                    assetManifestString = AssetManifestsAsString();
                }
            };
        }
        else
        {
            Debug.Log($"No addressable assets are tagged '{sceneName}'");
            addressablesReady = true;
        }
    }

    public string AssetManifestsAsString()
    {
        // we expect each addressable asset to have an attached CroquetActorManifest, that contains
        //    string[] mixins;
        //    string[] staticProperties;
        //    string[] watchedProperties;

        // here we build a single string that combines all assets' manifest properties.
        // arbitrarily, the string format is
        //   assetName1:mixinsList1:staticsList1:watchedList1:assetName2:mixinsList2:...
        // where ':' is in fact \x03, and the lists are comma-separated

        List<string> allManifests = new List<string>();
        foreach (KeyValuePair<string, GameObject> kv in Instance.addressableAssets)
        {
            GameObject asset = kv.Value;
            CroquetActorManifest manifest = asset.GetComponent<CroquetActorManifest>();
            if (manifest != null)
            {
                List<string> oneAssetStrings = new List<string>();
                oneAssetStrings.Add(kv.Key); // asset name
                oneAssetStrings.Add(string.Join(',', manifest.mixins));
                oneAssetStrings.Add(string.Join(',', manifest.staticProperties));
                oneAssetStrings.Add(string.Join(',', manifest.watchedProperties));
                allManifests.Add(string.Join('\x03', oneAssetStrings.ToArray()));
            }
        }

        string result = allManifests.Count == 0 ? "" : string.Join('\x03', allManifests.ToArray());
        return result;
    }

    public override void ProcessCommand(string command, string[] args)
    {
        if (command.Equals("makeObject"))
        {
            MakeObject(args);
        }
        else if (command.Equals("destroyObject"))
        {
            DestroyObject(args[0]);
        }
    }

    void MakeObject(string[] args)
    {
        ObjectSpec spec = JsonUtility.FromJson<ObjectSpec>(args[0]);
        // Debug.Log($"making object {spec.cH}");

        // try to find a prefab with the given name
        GameObject gameObjectToMake;
        if (spec.type.StartsWith("primitive"))
        {
            PrimitiveType primType = PrimitiveType.Cube;
            if (spec.type == "primitiveSphere") primType = PrimitiveType.Sphere;
            else if (spec.type == "primitiveCapsule") primType = PrimitiveType.Capsule;
            else if (spec.type == "primitiveCylinder") primType = PrimitiveType.Cylinder;
            else if (spec.type == "primitivePlane") primType = PrimitiveType.Plane;

            gameObjectToMake = CreateCroquetPrimitive(primType, Color.blue);
        }
        else
        {
            if (addressableAssets.ContainsKey(spec.type))
            {
                gameObjectToMake = Instantiate(addressableAssets[spec.type]);
            }
            else
            {
                Debug.Log( $"Specified spec.type ({spec.type}) is not found as a prefab! Creating Cube as Fallback Object");
                gameObjectToMake = CreateCroquetPrimitive(PrimitiveType.Cube, Color.magenta);
            }
        }

        if (gameObjectToMake.GetComponent<CroquetEntityComponent>() == null){
            gameObjectToMake.AddComponent<CroquetEntityComponent>();
        }

        CroquetEntityComponent entity = gameObjectToMake.GetComponent<CroquetEntityComponent>();
        entity.croquetHandle = spec.cH;
        int instanceID = gameObjectToMake.GetInstanceID();
        AssociateCroquetHandleToInstanceID(spec.cH, instanceID);

        // croquetName (actor.id)
        if (spec.cN != "")
        {
            entity.croquetActorId = spec.cN;
            CroquetBridge.Instance.FixUpEarlyListens(gameObjectToMake, entity.croquetActorId);
        }

        // allComponents
        if (spec.cs != "")
        {
            string[] comps = spec.cs.Split(',');
            foreach (string compName in comps)
            {
                try
                {
                    Type typeToAdd = Type.GetType(compName);
                    if (typeToAdd == null)
                    {
                        string assemblyQualifiedName =
                            System.Reflection.Assembly.CreateQualifiedName("Assembly-CSharp", compName);
                        typeToAdd = Type.GetType(assemblyQualifiedName);
                    }
                    if (typeToAdd == null)
                    {
                        // blew it
                        Debug.LogError($"Unable to find component {compName} in package or main assembly");
                    }
                    else
                    {
                        if (gameObjectToMake.GetComponent(typeToAdd) == null)
                        {
                            // Debug.Log($"adding component {typeToAdd}");
                            gameObjectToMake.AddComponent(typeToAdd);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in adding component {compName}: {e}");
                }
            }
        }

        // propertyValues
        if (spec.ps.Length != 0)
        {
            // an array with pairs   propName1, propVal1, propName2,...
            string[] props = spec.ps;
            for (int i = 0; i < props.Length; i += 2)
            {
                SetPropertyValueString(entity, props[i], props[i + 1]);
            }
        }

        // watchers
        if (spec.ws.Length != 0)
        {
            foreach (string propName in spec.ws)
            {
                string eventName = propName + "Set";
                Croquet.Listen(gameObjectToMake, eventName, (string stringyVal) =>
                {
                    SetPropertyValueString(entity, propName, stringyVal);
                });
            }
        }

        // waitToPresent
        if (spec.wTP)
        {
            foreach (Renderer renderer in gameObjectToMake.GetComponentsInChildren<Renderer>())
            {
                renderer.enabled = false;
            }
        }

        foreach (ICroquetDriven component in gameObjectToMake.GetComponents<ICroquetDriven>())
        {
            component.PawnInitializationComplete();
        }

        foreach (CroquetSystem system in CroquetBridge.Instance.croquetSystems) {
            if (system.KnowsObject(gameObjectToMake))
            {
                system.PawnInitializationComplete(gameObjectToMake);
            }
        }

        // confirmCreation
        if (spec.cC)
        {
            CroquetBridge.Instance.SendToCroquet("objectCreated", spec.cH, DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());
        }

    }

    private void SetPropertyValueString(CroquetEntityComponent entity, string propertyName, string stringyValue)
    {
        // @@ messy that this takes a component, while GetPropertyValueString takes
        // a game object.  but that is public, and this is private; around here we
        // know all about the components.

        // Debug.Log($"setting {propertyName} to {stringyValue}");
        entity.actorProperties[propertyName] = stringyValue;
        GameObject go = entity.gameObject;
        foreach (CroquetSystem system in CroquetBridge.Instance.croquetSystems)
        {
            if (system.KnowsObject(go))
            {
                system.ActorPropertySet(go, propertyName);
            }
        }
    }

    public bool HasActorSentProperty(GameObject gameObject, string propertyName)
    {
        CroquetEntityComponent entity = components[gameObject.GetInstanceID()] as CroquetEntityComponent;
        if (entity == null)
        {
            Debug.LogWarning($"failed to find Entity component for {gameObject}");
            return false;
        }

        StringStringSerializableDict properties = entity.actorProperties;
        return properties.ContainsKey(propertyName);
    }

    public string GetPropertyValueString(GameObject gameObject, string propertyName)
    {
        CroquetEntityComponent entity = components[gameObject.GetInstanceID()] as CroquetEntityComponent;
        if (entity == null)
        {
            Debug.LogWarning($"failed to find Entity component for {gameObject}");
            return null;
        }

        StringStringSerializableDict properties = entity.actorProperties;
        if (!properties.ContainsKey(propertyName))
        {
            Debug.LogWarning($"failed to find property {propertyName} in {gameObject}");
            return "";
        }
        return properties[propertyName];
    }

    void DestroyObject(string croquetHandle)
    {
        // Debug.Log( "Destroying Object " + croquetHandle.ToString());

        if (CroquetHandleToInstanceID.ContainsKey(croquetHandle))
        {
            int instanceID = CroquetHandleToInstanceID[croquetHandle];
            //components.Remove(instanceID);

            // INFORM OTHER COMPONENT'S SYSTEMS THEY ARE TO BE UNREGISTERED
            GameObject go = GetGameObjectByCroquetHandle(croquetHandle);
            CroquetComponent[] componentsToUnregister  = go.GetComponents<CroquetComponent>();
            foreach (var componentToUnregister in componentsToUnregister)
            {
                CroquetSystem system = componentToUnregister.croquetSystem;
                system.UnregisterComponent(componentToUnregister); //crosses fingers
            }


            CroquetBridge.Instance.RemoveCroquetSubscriptionsFor(go);


            DisassociateCroquetHandleToInstanceID(croquetHandle);

            Destroy(go);
        }
        else
        {
            // asking to destroy a pawn for which there's no view can happen just because of
            // creation/destruction timing in worldcore.  not necessarily a problem.
            Debug.Log($"attempt to destroy absent object {croquetHandle}");
        }
    }

    GameObject CreateCroquetPrimitive(PrimitiveType type, Color color)
    {
        GameObject go = new GameObject();
        go.name = $"primitive{type.ToString()}";
        go.AddComponent<CroquetEntityComponent>();
        GameObject inner = GameObject.CreatePrimitive(type);
        inner.transform.parent = go.transform;
        return go;
    }
}

[System.Serializable]
public class ObjectSpec
{
    public string cH; // croquet handle: currently an integer, but no point converting all the time
    public string cN; // Croquet name (generally, the model id)
    public bool cC; // confirmCreation: whether Croquet is waiting for a confirmCreation message for this
    public bool wTP; // waitToPresent:  whether to make visible immediately
    public string type;
    public string cs; // comma-separated list of extra components
    public string[] ps; // actor properties and their values
    public string[] ws; // actor properties to be watched
}

public interface ICroquetDriven
{
    void PawnInitializationComplete();
}
