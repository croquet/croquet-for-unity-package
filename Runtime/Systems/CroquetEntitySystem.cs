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

    private Dictionary<int, int> CroquetHandleToInstanceID = new Dictionary<int, int>();

    private void AssociateCroquetHandleToInstanceID(int croquetHandle, int id)
    {
        CroquetHandleToInstanceID.Add(croquetHandle, id);
    }

    private void DisassociateCroquetHandleToInstanceID(int croquetHandle)
    {
        CroquetHandleToInstanceID.Remove(croquetHandle);
    }

    /// <summary>
    /// Get GameObject with a specific Croquet Handle
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public GameObject GetGameObjectByCroquetHandle(int croquetHandle)
    {
        CroquetComponent croquetComponent;

        if (CroquetHandleToInstanceID.ContainsKey(croquetHandle))
        {
            int InstanceID = CroquetHandleToInstanceID[croquetHandle];
            if (components.TryGetValue(InstanceID, out croquetComponent))
            {
                return croquetComponent.gameObject;
            }
        }

        // Debug.Log($"Failed to find object {croquetHandle}");
        return null;
    }

    public static int GetInstanceIDByCroquetHandle(int croquetHandle)
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
        // If there is an Instance, and it's not me, delete myself.
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
                // DestroyObject(entityComponent.croquetHandle);
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
            if (ec.croquetHandle.Equals(-1))
            {
                needingInit.Add(ec.gameObject);
            }
        }

        return needingInit;
    }

    IEnumerator LoadAddressableAssetsWithLabel(string sceneName)
    {
        // LoadAssetsAsync throws an error - asynchronously - if there are
        // no assets that match the key.  One way to avoid that error is to run
        // the following code to get a list of locations matching the key.
        // If the list is empty, don't run the LoadAssetsAsync.

        int key = assetLoadKey; // if a new load is started while we're processing, we'll abandon this one

        List<string> labels = new List<string>() { "default", sceneName };

        //Returns IResourceLocations that are mapped to any of the supplied labels
        AsyncOperationHandle<IList<IResourceLocation>> handle = Addressables.LoadResourceLocationsAsync(
            labels,
            Addressables.MergeMode.Union);
        yield return handle;

        if (key != assetLoadKey) yield break; // scene has changed while assets were being found

        IList<IResourceLocation> result = handle.Result;
        int prefabs = 0;
        foreach (var loc in result)
        {
            if (loc.ToString().EndsWith(".prefab")) prefabs++;
        }

        Addressables.Release(handle);

        if (prefabs != 0)
        {
            // Load any assets labelled with this appName from the Addressable Assets
            Addressables.LoadAssetsAsync<GameObject>(
            labels,
            o => { },
            Addressables.MergeMode.Union).Completed += objects =>
            {
                // check again that the scene hasn't been changed during the async operation
                if (key == assetLoadKey)
                {
                    addressableAssets.Clear(); // now that we're ready to fill it
                    foreach (var gameObj in objects.Result)
                    {
                        CroquetActorManifest manifest = gameObj.GetComponent<CroquetActorManifest>();
                        if (manifest != null)
                        {
                            string assetName = manifest.pawnType;
                            Debug.Log($"Loaded asset for {assetName} pawnType");
                            addressableAssets.Add(assetName, gameObj);
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
            makeObject(args);
        }
        else if (command.Equals("destroyObject"))
        {
            DestroyObject(int.Parse(args[0]));
        }
    }

void makeObject(string[] args)
{
    ObjectSpec spec = JsonUtility.FromJson<ObjectSpec>(args[0]);

    // Try to find an existing object that matches the type
    GameObject gameObjectToMake = FindExistingObject(spec.type);

    // If no existing object is found, fall back to addressables or create a new one
    if (gameObjectToMake == null)
    {
        gameObjectToMake = LoadOrCreateNewObject(spec.type);
    }

    InitializeGameObject(gameObjectToMake, spec);
}
void InitializeGameObject(GameObject gameObject, ObjectSpec spec)
{
    CroquetEntityComponent entity = gameObject.GetComponent<CroquetEntityComponent>();
    if (entity == null)
    {
        entity = gameObject.AddComponent<CroquetEntityComponent>();
    }

    // Set properties and initialize other components as needed
    entity.croquetHandle = spec.cH;
    entity.croquetActorId = spec.cN;

    // Register the object in all systems
    RegisterInAllSystems(entity);

    // Apply any additional components, properties, and watchers
    ApplyComponentsAndProperties(gameObject, spec);
}
void ApplyComponentsAndProperties(GameObject gameObject, ObjectSpec spec)
{
    // Apply additional components if specified in the ObjectSpec
    if (!string.IsNullOrEmpty(spec.cs))
    {
        string[] components = spec.cs.Split(',');
        foreach (string componentName in components)
        {
            // Attempt to find the type for the component
            Type componentType = Type.GetType(componentName);
            if (componentType == null)
            {
                // If the type can't be found, try using the full name including the assembly
                string assemblyQualifiedName = System.Reflection.Assembly.CreateQualifiedName("Assembly-CSharp", componentName);
                componentType = Type.GetType(assemblyQualifiedName);
            }

            if (componentType != null)
            {
                // Add the component if it doesn't already exist on the GameObject
                if (gameObject.GetComponent(componentType) == null)
                {
                    gameObject.AddComponent(componentType);
                }
            }
            else
            {
                Debug.LogError($"Unable to find component type: {componentName}");
            }
        }
    }

    // Apply properties if specified in the ObjectSpec
    if (spec.ps != null && spec.ps.Length > 0)
    {
        for (int i = 0; i < spec.ps.Length; i += 2)
        {
            string propertyName = spec.ps[i];
            string propertyValue = spec.ps[i + 1];
            SetProperty(gameObject, propertyName, propertyValue);
        }
    }

    // Apply watchers if specified in the ObjectSpec
    if (spec.ws != null && spec.ws.Length > 0)
    {
        foreach (string watcherProperty in spec.ws)
        {
            string eventName = watcherProperty + "Set";
            Croquet.Listen(gameObject, eventName, (string newValue) =>
            {
                SetProperty(gameObject, watcherProperty, newValue);
            });
        }
    }
}

void SetProperty(GameObject gameObject, string propertyName, string propertyValue)
{
    // Retrieve the CroquetEntityComponent
    CroquetEntityComponent entityComponent = gameObject.GetComponent<CroquetEntityComponent>();

    if (entityComponent != null)
    {
        // Set the property value in the entity's actorProperties dictionary
        entityComponent.actorProperties[propertyName] = propertyValue;

        // Notify the Croquet systems of the property change
        foreach (var system in CroquetBridge.Instance.croquetSystems)
        {
            if (system.KnowsObject(gameObject))
            {
                system.ActorPropertySet(gameObject, propertyName);
            }
        }
    }
}

GameObject FindExistingObject(string type)
{
    GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
    foreach (GameObject obj in allObjects)
    {
        // Adjust this condition based on how you identify existing objects
        if (obj.name.Contains(type) && obj.GetComponent<HasBeenConsumed>() == null)
        {
            return obj;
        }
    }
    return null;
}

GameObject LoadOrCreateNewObject(string type)
{
    GameObject gameObjectToMake = null;

    if (type.StartsWith("primitive"))
    {
        // Handle primitive types
        PrimitiveType primType = GetPrimitiveType(type);
        gameObjectToMake = CreatePrimitive(primType, Color.blue);
    }
    else if (addressableAssets.ContainsKey(type))
    {
        // Load from addressables
        gameObjectToMake = Instantiate(addressableAssets[type]);
    }
    else
    {
        Debug.Log($"Specified spec.type ({type}) not found in prefab! Creating cube fallback object");
        gameObjectToMake = CreatePrimitive(PrimitiveType.Cube, Color.magenta);
    }

    return gameObjectToMake;
}
    void RegisterInAllSystems(CroquetComponent component)
    {
        try
        {
            CroquetSystem.Instance.RegisterComponent(component);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to register {component} in {CroquetSystem.Instance}: {e}");
        }
        try
        {
            CroquetSpatialSystem.Instance.RegisterComponent(component);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to register {component} in {CroquetSpatialSystem.Instance}: {e}");
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
    void DestroyObject(int croquetHandle)
    {
        Debug.Log("Destroying object " + croquetHandle.ToString());

        if (CroquetHandleToInstanceID.ContainsKey(croquetHandle))
        {
            int InstanceID = CroquetHandleToInstanceID[croquetHandle];

            GameObject go = GetGameObjectByCroquetHandle(croquetHandle);
            if (go != null)
            {
                // DestroyInAllSystems(go.GetComponent<CroquetComponent>());
            }
        }
        else
        {
            Debug.Log($"Attempt to destroy absent object {croquetHandle}");
        }
    }

    void DestroyInAllSystems(CroquetComponent component)
    {
        if (CroquetEntitySystem.Instance.KnowsObject(component.gameObject))
        {
            CroquetEntitySystem.Instance.UnregisterComponent(component);
        }
        if (CroquetSpatialSystem.Instance.KnowsObject(component.gameObject))
        {
            CroquetSpatialSystem.Instance.UnregisterComponent(component);
        }
        // Unregister from other systems as needed.
        Destroy(component.gameObject);
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

PrimitiveType GetPrimitiveType(string type)
{
    switch (type)
    {
        case "primitiveSphere":
            return PrimitiveType.Sphere;
        case "primitiveCapsule":
            return PrimitiveType.Capsule;
        case "primitiveCylinder":
            return PrimitiveType.Cylinder;
        case "primitivePlane":
            return PrimitiveType.Plane;
        default:
            return PrimitiveType.Cube;
    }
}

GameObject CreatePrimitive(PrimitiveType type, Color color)
{
    GameObject go = GameObject.CreatePrimitive(type);
    go.name = $"primitive{type.ToString()}";
    go.GetComponent<Renderer>().material.color = color;
    go.AddComponent<CroquetEntityComponent>();
    return go;
}
}
[System.Serializable]
public class ObjectSpec
{
    public int cH; // handle used by this client's Croquet bridge to address this object
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
