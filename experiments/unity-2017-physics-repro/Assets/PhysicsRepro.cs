using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public sealed class PhysicsRepro : MonoBehaviour
{
    [Serializable]
    private sealed class Sample
    {
        public int ordinal, renderFrame, fixedStep;
        public float bodyX, bodyY, bodyZ, transformX, transformY, transformZ;
        public float localX, localY, localZ;
        public float bodyRotationX, bodyRotationY, bodyRotationZ, bodyRotationW;
        public float transformRotationX, transformRotationY, transformRotationZ, transformRotationW;
        public float localRotationX, localRotationY, localRotationZ, localRotationW;
        public float velocityX, velocityY, velocityZ, angularVelocityX, angularVelocityY, angularVelocityZ;
        public float boundsCenterY, boundsMinY;
        public int bodyXBits, bodyYBits, bodyZBits, transformXBits, transformYBits, transformZBits;
        public int localXBits, localYBits, localZBits;
        public int bodyRotationXBits, bodyRotationYBits, bodyRotationZBits, bodyRotationWBits;
        public int transformRotationXBits, transformRotationYBits, transformRotationZBits, transformRotationWBits;
        public int localRotationXBits, localRotationYBits, localRotationZBits, localRotationWBits;
        public int velocityXBits, velocityYBits, velocityZBits, angularVelocityXBits, angularVelocityYBits, angularVelocityZBits;
        public int boundsCenterYBits, boundsMinYBits;
        public bool kinematic, sleeping;
    }

    [Serializable]
    private sealed class Report
    {
        public string kind = "unity-2017-physics-rewind-reproduction";
        public int version = 5;
        public string unityVersion, mode, geometry, restoreLifecycle, architecture, outputPath, initialPoseMethod;
        public string sceneManifestPath, sceneSourceSha256;
        public float startX, startY, moveSpeed, floorTop, fixedDeltaTime, capsuleRadius, capsuleHeight, contactOffset;
        public int warmupFrames, precheckpointFrames, frozenFrames, continuationFrames, restoreWrites;
        public int sceneBodyCount, serializedBodyCount, sceneObjectCount, sceneComponentCount, primaryBodyFileId;
        public bool initialMoveTargetApplied, precheckpointMoveTargetApplied;
        public bool parented, syncAfterRestore, includeTriggers, includeStaticCapsules, includePlateProxies;
        public string restoreBodyOrder;
        public float initialMoveTargetY, precheckpointMoveTargetY;
        public bool passed, completed;
        public string firstDifference, error;
        public Sample checkpoint, futureBoundary, restoredBoundary;
        public Sample[] warmup, precheckpoint, original, replay;
    }

    private sealed class Frozen
    {
        public Rigidbody body;
        public Vector3 velocity, angularVelocity;
        public bool kinematic, gravity;
    }

    private sealed class BodyCheckpoint
    {
        public Rigidbody body;
        public Vector3 bodyPosition, localPosition;
        public Quaternion bodyRotation, localRotation;
    }

    [Serializable]
    private sealed class SceneManifest
    {
        public string kind, source, sourceSha256;
        public int version;
        public SceneCounts counts;
        public SceneObject[] objects;
        public SceneComponent[] components;
    }

    [Serializable]
    private sealed class SceneCounts
    {
        public int objects, components, rigidbodies, meshColliders, boxColliders, capsuleColliders;
    }

    [Serializable]
    private sealed class SceneObject
    {
        public int fileId, ordinal, layer, transformFileId, parentTransformFileId, rootOrder;
        public string name, tag;
        public bool active;
        public int[] componentFileIds;
        public float[] localPosition, localRotation, localScale;
    }

    [Serializable]
    private sealed class SceneReference
    {
        public int fileId, type;
        public string guid;
    }

    [Serializable]
    private sealed class SceneComponent
    {
        public int fileId, classId, owner, ordinal;
        public string path;
        public float mass, drag, angularDrag;
        public bool useGravity, kinematic;
        public int interpolate, constraints, collisionDetection;
        public SceneReference material, mesh;
        public bool trigger, enabled, convex;
        public int cookingOptions;
        public float skinWidth;
        public float[] center, size;
        public float radius, height;
        public int direction;
    }

    private Rigidbody body;
    private CapsuleCollider capsule;
    private readonly List<Rigidbody> bodies = new List<Rigidbody>();
    private readonly List<Rigidbody> controlledBodies = new List<Rigidbody>();
    private readonly Dictionary<Rigidbody, int> bodyFileIds = new Dictionary<Rigidbody, int>();
    private readonly Dictionary<string, PhysicMaterial> materialCache = new Dictionary<string, PhysicMaterial>();
    private int fixedSteps, phaseFixedStart;
    private bool activeSimulation;
    private string mode, geometry, restoreLifecycle, outputPath, sceneManifestPath, restoreBodyOrder, initialPoseMethod;
    private float startX, startY, moveSpeed, floorTop;
    private int warmupFrames, precheckpointFrames, frozenFrames, continuationFrames, restoreWrites, sceneBodyCount, primaryBodyFileId;
    private bool parented, syncAfterRestore, includeTriggers, includeStaticCapsules, includePlateProxies;
    private bool precheckpointMoveTargetApplied;
    private float precheckpointMoveTargetY;
    private SceneManifest sceneManifest;
    private Report report;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartReproduction()
    {
        GameObject root = new GameObject("PhysicsRepro");
        DontDestroyOnLoad(root);
        root.AddComponent<PhysicsRepro>();
    }

    private void Awake()
    {
        Application.runInBackground = true;
        Time.captureFramerate = 60;
        Time.fixedDeltaTime = 0.02f;
        mode = Argument("--mode", "physics-only");
        geometry = Argument("--geometry", "flat");
        restoreLifecycle = Argument("--restoreLifecycle", "stay-frozen");
        outputPath = Path.GetFullPath(Argument("--out", Path.Combine(Application.persistentDataPath, "physics-repro.json")));
        sceneManifestPath = Argument("--sceneManifest", "");
        if (!String.IsNullOrEmpty(sceneManifestPath)) sceneManifestPath = Path.GetFullPath(sceneManifestPath);
        startX = FloatArgument("--startX", 0f);
        startY = FloatArgument("--startY", 0f);
        moveSpeed = FloatArgument("--moveSpeed", 1f);
        floorTop = FloatArgument("--floorTop", 0f);
        parented = BoolArgument("--parented", false);
        syncAfterRestore = BoolArgument("--syncAfterRestore", false);
        includeTriggers = BoolArgument("--includeTriggers", false);
        includeStaticCapsules = BoolArgument("--includeStaticCapsules", true);
        includePlateProxies = BoolArgument("--includePlateProxies", false);
        restoreBodyOrder = Argument("--restoreBodyOrder", "");
        initialPoseMethod = Argument("--initialPoseMethod", "move-position");
        sceneBodyCount = IntArgument("--sceneBodyCount", 4);
        primaryBodyFileId = IntArgument("--primaryBodyFileId", 0);
        warmupFrames = IntArgument("--warmupFrames", 30);
        precheckpointFrames = IntArgument("--precheckpointFrames", 0);
        frozenFrames = IntArgument("--frozenFrames", 6);
        continuationFrames = IntArgument("--continuationFrames", 60);
        restoreWrites = IntArgument("--restoreWrites", 2);
        if (mode != "physics-only" && mode != "move-position" && mode != "gravity-move" && mode != "move-x" && mode != "game-neutral")
            throw new ArgumentException("Use --mode physics-only, move-position, gravity-move, move-x or game-neutral.");
        if (String.IsNullOrEmpty(sceneManifestPath) && geometry != "flat" && geometry != "box-step" && geometry != "mesh-step")
            throw new ArgumentException("Use --geometry flat, box-step or mesh-step.");
        if (restoreLifecycle != "stay-frozen" && restoreLifecycle != "dynamic-roundtrip")
            throw new ArgumentException("Use --restoreLifecycle stay-frozen or dynamic-roundtrip.");
        if (initialPoseMethod != "move-position" && initialPoseMethod != "body-position" &&
            initialPoseMethod != "transform-position" && initialPoseMethod != "local-position")
            throw new ArgumentException("Use --initialPoseMethod move-position, body-position, transform-position or local-position.");

        if (String.IsNullOrEmpty(sceneManifestPath))
        {
            CreateFloor();
            GameObject actor = new GameObject("CapsuleActor");
            if (parented)
            {
                GameObject parent = new GameObject("PhysicsContainerParent");
                parent.transform.position = new Vector3(0f, 0.75f, 0f);
                actor.transform.SetParent(parent.transform, false);
                actor.transform.localPosition = new Vector3(startX, startY - 0.75f, 0f);
            }
            else actor.transform.position = new Vector3(startX, startY, 0f);
            body = actor.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.drag = 0f;
            body.angularDrag = 0.05f;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            capsule = actor.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1f, 0f);
            capsule.radius = 0.4f;
            capsule.height = 2f;
            capsule.direction = 1;
            bodies.Add(body);
            controlledBodies.Add(body);
            bodyFileIds[body] = 0;
        }
        else
        {
            geometry = "story11-manifest";
            CreateSceneFromManifest();
            startX = body.position.x;
            startY = body.position.y;
            parented = body.transform.parent != null;
        }

        float initialMoveTargetY;
        bool initialMoveTargetApplied = Single.TryParse(Argument("--initialMoveTargetY", ""),
            NumberStyles.Float, CultureInfo.InvariantCulture, out initialMoveTargetY);
        if (initialMoveTargetApplied)
            foreach (Rigidbody target in controlledBodies) ApplyInitialY(target, initialMoveTargetY);
        precheckpointMoveTargetApplied = Single.TryParse(Argument("--precheckpointMoveTargetY", ""),
            NumberStyles.Float, CultureInfo.InvariantCulture, out precheckpointMoveTargetY);
        if (precheckpointFrames > 0 && !precheckpointMoveTargetApplied)
            throw new ArgumentException("--precheckpointFrames requires --precheckpointMoveTargetY.");

        report = new Report();
        report.unityVersion = Application.unityVersion;
        report.mode = mode;
        report.geometry = geometry;
        report.restoreLifecycle = restoreLifecycle;
        report.architecture = IntPtr.Size == 4 ? "x86" : "x64";
        report.outputPath = outputPath;
        report.initialPoseMethod = initialPoseMethod;
        report.sceneManifestPath = sceneManifestPath;
        report.sceneSourceSha256 = sceneManifest == null ? "" : sceneManifest.sourceSha256;
        report.startX = startX;
        report.startY = startY;
        report.moveSpeed = moveSpeed;
        report.floorTop = floorTop;
        report.fixedDeltaTime = Time.fixedDeltaTime;
        report.capsuleRadius = capsule.radius;
        report.capsuleHeight = capsule.height;
        report.contactOffset = capsule.contactOffset;
        report.warmupFrames = warmupFrames;
        report.precheckpointFrames = precheckpointFrames;
        report.frozenFrames = frozenFrames;
        report.continuationFrames = continuationFrames;
        report.restoreWrites = restoreWrites;
        report.initialMoveTargetApplied = initialMoveTargetApplied;
        report.initialMoveTargetY = initialMoveTargetY;
        report.precheckpointMoveTargetApplied = precheckpointMoveTargetApplied;
        report.precheckpointMoveTargetY = precheckpointMoveTargetY;
        report.parented = parented;
        report.syncAfterRestore = syncAfterRestore;
        report.includeTriggers = includeTriggers;
        report.includeStaticCapsules = includeStaticCapsules;
        report.includePlateProxies = includePlateProxies;
        report.restoreBodyOrder = restoreBodyOrder;
        report.sceneBodyCount = bodies.Count;
        report.serializedBodyCount = sceneBodyCount;
        report.sceneObjectCount = sceneManifest == null || sceneManifest.counts == null ? 0 : sceneManifest.counts.objects;
        report.sceneComponentCount = sceneManifest == null || sceneManifest.counts == null ? 0 : sceneManifest.counts.components;
        report.primaryBodyFileId = bodyFileIds[body];
        StartCoroutine(Run());
    }

    private void ApplyInitialY(Rigidbody target, float y)
    {
        if (initialPoseMethod == "move-position")
        {
            Vector3 position = target.position;
            target.MovePosition(new Vector3(position.x, y, position.z));
        }
        else if (initialPoseMethod == "body-position")
        {
            Vector3 position = target.position;
            target.position = new Vector3(position.x, y, position.z);
        }
        else if (initialPoseMethod == "transform-position")
        {
            Vector3 position = target.transform.position;
            target.transform.position = new Vector3(position.x, y, position.z);
        }
        else
        {
            Vector3 position = target.transform.localPosition;
            float parentY = target.transform.parent == null ? 0f : target.transform.parent.position.y;
            target.transform.localPosition = new Vector3(position.x, y - parentY, position.z);
        }
    }

    private void Update()
    {
        if (!activeSimulation || body == null || mode != "game-neutral") return;
        foreach (Rigidbody target in controlledBodies)
        {
            if (target == null || target.isKinematic) continue;
            target.velocity = Vector3.zero;
            target.MovePosition(target.position);
            target.MovePosition(target.position);
        }
    }

    private void CreateSceneFromManifest()
    {
        if (!File.Exists(sceneManifestPath)) throw new FileNotFoundException("Scene manifest not found", sceneManifestPath);
        sceneManifest = JsonUtility.FromJson<SceneManifest>(File.ReadAllText(sceneManifestPath));
        if (sceneManifest == null || sceneManifest.kind != "unity-yaml-physics-scene" || sceneManifest.version != 1)
            throw new InvalidOperationException("Unsupported scene manifest.");
        if (sceneManifest.objects == null || sceneManifest.components == null)
            throw new InvalidOperationException("Scene manifest is incomplete.");

        Dictionary<int, GameObject> objects = new Dictionary<int, GameObject>();
        Dictionary<int, Transform> transforms = new Dictionary<int, Transform>();
        foreach (SceneObject source in sceneManifest.objects)
        {
            if (source.localPosition == null || source.localPosition.Length != 3 ||
                source.localRotation == null || source.localRotation.Length != 4 ||
                source.localScale == null || source.localScale.Length != 3)
                throw new InvalidOperationException("Invalid Transform in scene manifest: " + source.fileId);
            GameObject value = new GameObject(source.name);
            value.SetActive(false);
            value.layer = source.layer;
            objects.Add(source.fileId, value);
            transforms.Add(source.transformFileId, value.transform);
        }
        foreach (SceneObject source in sceneManifest.objects)
        {
            GameObject value = objects[source.fileId];
            if (source.parentTransformFileId != 0)
            {
                Transform parent;
                if (!transforms.TryGetValue(source.parentTransformFileId, out parent))
                    throw new InvalidOperationException("Missing parent Transform: " + source.parentTransformFileId);
                value.transform.SetParent(parent, false);
            }
            value.transform.localPosition = V3(source.localPosition);
            value.transform.localRotation = Q(source.localRotation);
            value.transform.localScale = V3(source.localScale);
        }

        HashSet<int> allBodyOwners = new HashSet<int>();
        List<SceneComponent> bodySources = new List<SceneComponent>();
        foreach (SceneComponent source in sceneManifest.components)
            if (source.classId == 54)
            {
                allBodyOwners.Add(source.owner);
                bodySources.Add(source);
            }
        if (sceneBodyCount < 1 || sceneBodyCount > bodySources.Count)
            throw new ArgumentOutOfRangeException("--sceneBodyCount", "Select between one and all serialized bodies.");
        HashSet<int> selectedBodyOwners = new HashSet<int>();
        for (int i = 0; i < sceneBodyCount; i++) selectedBodyOwners.Add(bodySources[i].owner);

        Dictionary<int, Rigidbody> bodiesByFileId = new Dictionary<int, Rigidbody>();
        foreach (SceneComponent source in sceneManifest.components)
        {
            GameObject owner;
            if (!objects.TryGetValue(source.owner, out owner))
                throw new InvalidOperationException("Missing physics component owner: " + source.owner);
            if (source.classId == 54)
            {
                if (!selectedBodyOwners.Contains(source.owner)) continue;
                Rigidbody value = owner.AddComponent<Rigidbody>();
                value.mass = source.mass;
                value.drag = source.drag;
                value.angularDrag = source.angularDrag;
                value.useGravity = source.useGravity;
                value.isKinematic = source.kinematic;
                value.interpolation = (RigidbodyInterpolation)source.interpolate;
                value.constraints = (RigidbodyConstraints)source.constraints;
                value.collisionDetectionMode = (CollisionDetectionMode)source.collisionDetection;
                bodies.Add(value);
                controlledBodies.Add(value);
                bodyFileIds[value] = source.fileId;
                bodiesByFileId.Add(source.fileId, value);
                continue;
            }
            if (source.trigger && !includeTriggers) continue;
            if (allBodyOwners.Contains(source.owner) && !selectedBodyOwners.Contains(source.owner)) continue;
            if (source.classId == 65)
            {
                BoxCollider value = owner.AddComponent<BoxCollider>();
                value.center = V3(source.center);
                value.size = V3(source.size);
                value.sharedMaterial = Material(source.material);
                value.isTrigger = source.trigger;
                value.enabled = source.enabled;
            }
            else if (source.classId == 136)
            {
                if (!allBodyOwners.Contains(source.owner) && !includeStaticCapsules) continue;
                CapsuleCollider value = owner.AddComponent<CapsuleCollider>();
                value.center = V3(source.center);
                value.radius = source.radius;
                value.height = source.height;
                value.direction = source.direction;
                value.sharedMaterial = Material(source.material);
                value.isTrigger = source.trigger;
                value.enabled = source.enabled;
            }
            else if (source.classId == 64)
            {
                Mesh mesh = BuiltinMesh(source.mesh);
                MeshCollider value = owner.AddComponent<MeshCollider>();
                value.sharedMesh = mesh;
                value.convex = source.convex;
                value.sharedMaterial = Material(source.material);
                value.isTrigger = source.trigger;
                value.enabled = source.enabled;
            }
        }

        foreach (SceneObject source in sceneManifest.objects)
            if (source.parentTransformFileId != 0 && source.active) objects[source.fileId].SetActive(true);
        foreach (SceneObject source in sceneManifest.objects)
            if (source.parentTransformFileId == 0 && source.active) objects[source.fileId].SetActive(true);

        if (bodies.Count != sceneBodyCount) throw new InvalidOperationException("Scene body construction count differs.");
        if (includePlateProxies) CreatePlateProxies();
        if (primaryBodyFileId == 0) primaryBodyFileId = bodyFileIds[bodies[0]];
        if (!bodiesByFileId.TryGetValue(primaryBodyFileId, out body))
            throw new ArgumentException("--primaryBodyFileId must select one of the included Rigidbody file IDs.");
        capsule = body.GetComponent<CapsuleCollider>();
        if (capsule == null) throw new InvalidOperationException("Primary scene body lacks its CapsuleCollider.");
    }

    private void CreatePlateProxies()
    {
        CreatePlateProxy(47, 10.8f, 0.5f, -3.6f);
        CreatePlateProxy(48, 16.8f, 0.5f, -3.6f);
        CreatePlateProxy(49, 12f, 0.5f, -3.6f);
        CreatePlateProxy(50, 18f, 0.5f, -3.6f);
    }

    private void CreatePlateProxy(int entityId, float x, float y, float z)
    {
        GameObject proxy = new GameObject("PlateProxy" + entityId.ToString(CultureInfo.InvariantCulture));
        proxy.transform.position = new Vector3(x, y, z);
        Rigidbody value = proxy.AddComponent<Rigidbody>();
        value.mass = 1f;
        value.drag = 2f;
        value.angularDrag = 1f;
        value.constraints = (RigidbodyConstraints)80;
        value.interpolation = RigidbodyInterpolation.None;
        value.collisionDetectionMode = CollisionDetectionMode.Discrete;
        value.useGravity = true;
        value.isKinematic = true;
        bodies.Add(value);
        bodyFileIds[value] = entityId;
    }

    private PhysicMaterial Material(SceneReference reference)
    {
        if (reference == null || reference.fileId == 0) return null;
        string key = reference.guid ?? "";
        PhysicMaterial value;
        if (materialCache.TryGetValue(key, out value)) return value;
        value = new PhysicMaterial(key == "56bae9020ce972c43a7e8b2d0ca1c276" ? "Player" : "ExtractedMaterial-" + key);
        if (key == "56bae9020ce972c43a7e8b2d0ca1c276")
        {
            value.dynamicFriction = 0f;
            value.staticFriction = 0f;
            value.bounciness = 0f;
            value.frictionCombine = PhysicMaterialCombine.Minimum;
            value.bounceCombine = PhysicMaterialCombine.Minimum;
        }
        materialCache.Add(key, value);
        return value;
    }

    private static Mesh BuiltinMesh(SceneReference reference)
    {
        if (reference == null || reference.guid != "0000000000000000e000000000000000" || reference.fileId != 10209)
            throw new InvalidOperationException("Unsupported extracted MeshCollider mesh.");
        Mesh value = Resources.GetBuiltinResource<Mesh>("New-Plane.fbx");
        if (value == null) value = Resources.GetBuiltinResource<Mesh>("Plane.fbx");
        if (value == null) throw new InvalidOperationException("Unity built-in plane mesh was not found.");
        return value;
    }

    private static Vector3 V3(float[] value)
    {
        if (value == null || value.Length != 3) throw new InvalidOperationException("Expected a three-component vector.");
        return new Vector3(value[0], value[1], value[2]);
    }

    private static Quaternion Q(float[] value)
    {
        if (value == null || value.Length != 4) throw new InvalidOperationException("Expected a four-component quaternion.");
        return new Quaternion(value[0], value[1], value[2], value[3]);
    }

    private void CreateFloor()
    {
        if (geometry == "flat")
        {
            CreateFloorBox("StaticFloor", 0f, 20f, floorTop);
            return;
        }
        if (geometry == "box-step")
        {
            CreateFloorBox("LowerFloor", -5f, 10f, 0f);
            CreateFloorBox("UpperFloor", 5f, 10f, floorTop);
            return;
        }
        GameObject floor = new GameObject("StaticMeshStep");
        Mesh mesh = new Mesh();
        mesh.name = "TwoHeightFloorMesh";
        mesh.vertices = new[] {
            new Vector3(-10f,0f,-10f),new Vector3(0f,0f,-10f),new Vector3(0f,0f,10f),new Vector3(-10f,0f,10f),
            new Vector3(0f,floorTop,-10f),new Vector3(10f,floorTop,-10f),new Vector3(10f,floorTop,10f),new Vector3(0f,floorTop,10f)
        };
        mesh.triangles = new[] {0,2,1,0,3,2,4,6,5,4,7,6};
        mesh.RecalculateBounds();
        floor.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private static void CreateFloorBox(string name, float centerX, float width, float top)
    {
        GameObject floor = new GameObject(name);
        floor.transform.position = new Vector3(centerX, top - 0.5f, 0f);
        BoxCollider collider = floor.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, 1f, 20f);
    }

    private void FixedUpdate()
    {
        fixedSteps++;
        if (!activeSimulation || body == null || body.isKinematic) return;
        if (mode == "gravity-move")
            body.AddForce(Physics.gravity, ForceMode.Acceleration);
        if (mode == "move-position" || mode == "gravity-move")
            body.MovePosition(body.position);
        if (mode == "move-x")
            body.MovePosition(body.position + Vector3.right * moveSpeed * Time.fixedDeltaTime);
    }

    private IEnumerator Run()
    {
        activeSimulation = true;
        List<Sample> warmup = new List<Sample>();
        phaseFixedStart = fixedSteps;
        if (warmupFrames > 0)
            yield return RecordFrames(warmup, warmupFrames);
        report.warmup = warmup.ToArray();
        Frozen[] checkpointFrozen = FreezeAll();
        activeSimulation = false;
        List<Sample> precheckpoint = new List<Sample>();
        if (precheckpointMoveTargetApplied)
        {
            foreach (Rigidbody target in controlledBodies)
            {
                Vector3 position = target.position;
                target.MovePosition(new Vector3(position.x, precheckpointMoveTargetY, position.z));
            }
            phaseFixedStart = fixedSteps;
            if (precheckpointFrames > 0)
                yield return RecordFrames(precheckpoint, precheckpointFrames);
        }
        report.precheckpoint = precheckpoint.ToArray();
        yield return Frames(frozenFrames);
        BodyCheckpoint[] checkpointBodies = CaptureBodyCheckpoints();
        report.checkpoint = Capture(-1);
        report.checkpoint.fixedStep = 0;

        UnfreezeAll(checkpointFrozen);
        activeSimulation = true;
        List<Sample> original = new List<Sample>();
        phaseFixedStart = fixedSteps;
        yield return RecordFrames(original, continuationFrames);
        Frozen[] futureFrozen = FreezeAll();
        activeSimulation = false;
        yield return Frames(frozenFrames);
        report.futureBoundary = Capture(-1);

        if (restoreLifecycle == "dynamic-roundtrip")
            UnfreezeAll(futureFrozen);
        RestoreBodyCheckpoints(checkpointBodies);
        foreach (Frozen value in checkpointFrozen)
        {
            value.body.velocity = value.velocity;
            value.body.angularVelocity = value.angularVelocity;
        }
        if (syncAfterRestore)
            Physics.SyncTransforms();
        Frozen[] restoredFrozen;
        if (restoreLifecycle == "dynamic-roundtrip")
        {
            UnfreezeAll(checkpointFrozen);
            restoredFrozen = FreezeAll();
        }
        else restoredFrozen = checkpointFrozen;
        activeSimulation = false;
        yield return Frames(frozenFrames);
        report.restoredBoundary = Capture(-1);
        report.restoredBoundary.fixedStep = 0;

        UnfreezeAll(restoredFrozen);
        activeSimulation = true;
        List<Sample> replay = new List<Sample>();
        phaseFixedStart = fixedSteps;
        yield return RecordFrames(replay, continuationFrames);
        activeSimulation = false;
        FreezeAll();

        report.original = original.ToArray();
        report.replay = replay.ToArray();
        report.firstDifference = FirstDifference(report.original, report.replay);
        report.passed = report.firstDifference == null && Same(report.checkpoint, report.restoredBoundary);
        if (!Same(report.checkpoint, report.restoredBoundary) && report.firstDifference == null)
            report.firstDifference = "restored boundary differs from checkpoint";
        report.completed = true;
        WriteReport();
        Application.Quit();
    }

    private IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return new WaitForEndOfFrame();
    }

    private IEnumerator RecordFrames(List<Sample> samples, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new WaitForEndOfFrame();
            samples.Add(Capture(i));
        }
    }

    private Frozen[] FreezeAll()
    {
        List<Frozen> values = new List<Frozen>();
        foreach (Rigidbody target in bodies)
        {
            Frozen value = new Frozen { body = target, velocity = target.velocity, angularVelocity = target.angularVelocity,
                kinematic = target.isKinematic, gravity = target.useGravity };
            target.velocity = Vector3.zero;
            target.angularVelocity = Vector3.zero;
            target.isKinematic = true;
            target.useGravity = false;
            values.Add(value);
        }
        return values.ToArray();
    }

    private static void UnfreezeAll(Frozen[] values)
    {
        foreach (Frozen value in values)
        {
            value.body.velocity = value.velocity;
            value.body.angularVelocity = value.angularVelocity;
            value.body.isKinematic = value.kinematic;
            value.body.useGravity = value.gravity;
        }
    }

    private BodyCheckpoint[] CaptureBodyCheckpoints()
    {
        List<BodyCheckpoint> values = new List<BodyCheckpoint>();
        foreach (Rigidbody target in BodiesInRestoreOrder())
            values.Add(new BodyCheckpoint { body = target, bodyPosition = target.position,
                bodyRotation = target.rotation, localPosition = target.transform.localPosition,
                localRotation = target.transform.localRotation });
        return values.ToArray();
    }

    private IEnumerable<Rigidbody> BodiesInRestoreOrder()
    {
        if (String.IsNullOrEmpty(restoreBodyOrder)) return bodies;
        string[] ids = restoreBodyOrder.Split(',');
        if (ids.Length != bodies.Count)
            throw new ArgumentException("--restoreBodyOrder must contain every included body ID exactly once.");
        Dictionary<int, Rigidbody> byId = new Dictionary<int, Rigidbody>();
        foreach (Rigidbody target in bodies) byId.Add(bodyFileIds[target], target);
        List<Rigidbody> ordered = new List<Rigidbody>();
        HashSet<int> seen = new HashSet<int>();
        foreach (string text in ids)
        {
            int id;
            if (!Int32.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ||
                !seen.Add(id) || !byId.ContainsKey(id))
                throw new ArgumentException("--restoreBodyOrder contains an invalid or duplicate body ID: " + text);
            ordered.Add(byId[id]);
        }
        return ordered;
    }

    private void RestoreBodyCheckpoints(BodyCheckpoint[] values)
    {
        for (int write = 0; write < restoreWrites; write++)
            foreach (BodyCheckpoint value in values)
            {
                if (value.body.transform.localPosition != value.localPosition)
                    value.body.transform.localPosition = value.localPosition;
                if (value.body.transform.localRotation != value.localRotation)
                    value.body.transform.localRotation = value.localRotation;
                if (value.body.position != value.bodyPosition)
                    value.body.position = value.bodyPosition;
                if (value.body.rotation != value.bodyRotation)
                    value.body.rotation = value.bodyRotation;
            }
    }

    private Sample Capture(int ordinal)
    {
        Vector3 p = body.position, t = body.transform.position, l = body.transform.localPosition, v = body.velocity;
        Quaternion r = body.rotation, tr = body.transform.rotation, lr = body.transform.localRotation;
        Vector3 a = body.angularVelocity;
        Bounds bounds = capsule.bounds;
        return new Sample { ordinal = ordinal, renderFrame = Time.frameCount, fixedStep = fixedSteps - phaseFixedStart,
            bodyX = p.x, bodyY = p.y, bodyZ = p.z, transformX = t.x, transformY = t.y, transformZ = t.z,
            localX = l.x, localY = l.y, localZ = l.z,
            bodyRotationX = r.x, bodyRotationY = r.y, bodyRotationZ = r.z, bodyRotationW = r.w,
            transformRotationX = tr.x, transformRotationY = tr.y, transformRotationZ = tr.z, transformRotationW = tr.w,
            localRotationX = lr.x, localRotationY = lr.y, localRotationZ = lr.z, localRotationW = lr.w,
            velocityX = v.x, velocityY = v.y, velocityZ = v.z,
            angularVelocityX = a.x, angularVelocityY = a.y, angularVelocityZ = a.z,
            boundsCenterY = bounds.center.y, boundsMinY = bounds.min.y,
            bodyXBits = Bits(p.x), bodyYBits = Bits(p.y), bodyZBits = Bits(p.z),
            transformXBits = Bits(t.x), transformYBits = Bits(t.y), transformZBits = Bits(t.z),
            localXBits = Bits(l.x), localYBits = Bits(l.y), localZBits = Bits(l.z),
            bodyRotationXBits = Bits(r.x), bodyRotationYBits = Bits(r.y), bodyRotationZBits = Bits(r.z), bodyRotationWBits = Bits(r.w),
            transformRotationXBits = Bits(tr.x), transformRotationYBits = Bits(tr.y), transformRotationZBits = Bits(tr.z), transformRotationWBits = Bits(tr.w),
            localRotationXBits = Bits(lr.x), localRotationYBits = Bits(lr.y), localRotationZBits = Bits(lr.z), localRotationWBits = Bits(lr.w),
            velocityXBits = Bits(v.x), velocityYBits = Bits(v.y), velocityZBits = Bits(v.z),
            angularVelocityXBits = Bits(a.x), angularVelocityYBits = Bits(a.y), angularVelocityZBits = Bits(a.z),
            boundsCenterYBits = Bits(bounds.center.y), boundsMinYBits = Bits(bounds.min.y),
            kinematic = body.isKinematic, sleeping = body.IsSleeping() };
    }

    private static string FirstDifference(Sample[] a, Sample[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return "sample count differs";
        for (int i = 0; i < a.Length; i++)
            if (!Same(a[i], b[i]))
                return "frame " + i.ToString(CultureInfo.InvariantCulture) + ": original bodyY=" +
                    a[i].bodyY.ToString("R", CultureInfo.InvariantCulture) + " replay bodyY=" +
                    b[i].bodyY.ToString("R", CultureInfo.InvariantCulture);
        return null;
    }

    private static bool Same(Sample a, Sample b)
    {
        return a != null && b != null && a.fixedStep == b.fixedStep &&
            a.bodyXBits == b.bodyXBits && a.bodyYBits == b.bodyYBits && a.bodyZBits == b.bodyZBits &&
            a.transformXBits == b.transformXBits && a.transformYBits == b.transformYBits && a.transformZBits == b.transformZBits &&
            a.localXBits == b.localXBits && a.localYBits == b.localYBits && a.localZBits == b.localZBits &&
            a.bodyRotationXBits == b.bodyRotationXBits && a.bodyRotationYBits == b.bodyRotationYBits &&
            a.bodyRotationZBits == b.bodyRotationZBits && a.bodyRotationWBits == b.bodyRotationWBits &&
            a.transformRotationXBits == b.transformRotationXBits && a.transformRotationYBits == b.transformRotationYBits &&
            a.transformRotationZBits == b.transformRotationZBits && a.transformRotationWBits == b.transformRotationWBits &&
            a.localRotationXBits == b.localRotationXBits && a.localRotationYBits == b.localRotationYBits &&
            a.localRotationZBits == b.localRotationZBits && a.localRotationWBits == b.localRotationWBits &&
            a.velocityXBits == b.velocityXBits && a.velocityYBits == b.velocityYBits && a.velocityZBits == b.velocityZBits &&
            a.angularVelocityXBits == b.angularVelocityXBits && a.angularVelocityYBits == b.angularVelocityYBits &&
            a.angularVelocityZBits == b.angularVelocityZBits &&
            a.boundsCenterYBits == b.boundsCenterYBits && a.boundsMinYBits == b.boundsMinYBits &&
            a.kinematic == b.kinematic && a.sleeping == b.sleeping;
    }

    private void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
        Debug.Log("PHYSICS_REPRO_RESULT " + (report.passed ? "PASS" : "FAIL") + " " + report.firstDifference);
    }

    private static int Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }
    private static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
    private static int IntArgument(string name, int fallback)
    {
        int value; return Int32.TryParse(Argument(name, fallback.ToString(CultureInfo.InvariantCulture)),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
    }
    private static float FloatArgument(string name, float fallback)
    {
        float value; return Single.TryParse(Argument(name, fallback.ToString("R", CultureInfo.InvariantCulture)),
            NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
    }
    private static bool BoolArgument(string name, bool fallback)
    {
        bool value; return Boolean.TryParse(Argument(name, fallback.ToString()), out value) ? value : fallback;
    }
}
