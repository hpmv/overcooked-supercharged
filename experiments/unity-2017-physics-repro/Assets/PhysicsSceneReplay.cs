using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;

// A separate, command-line-only canary for replaying the Unity-owned physics
// scene while retaining the exact managed GameObject and Component instances.
public sealed class PhysicsSceneReplay : MonoBehaviour
{
    [Serializable]
    private sealed class PhysicsEvent
    {
        public int step;
        public string owner, kind, other;
        public int contactCount;
        public int[] relativeVelocityBits;
    }

    [Serializable]
    private sealed class StepSample
    {
        public int step;
        public bool primarySleeping, lateActive, lateSleeping;
        public int[] primaryBits, lateBits, floorBits;
        public string[] raycastHits, overlapHits;
    }

    [Serializable]
    private sealed class Identity
    {
        public int floorObject, floorCollider, primaryObject, primaryBody,
            primaryCollider, lateObject, lateBody, lateCollider;
    }

    [Serializable]
    private sealed class Report
    {
        public string kind = "unity-2017-same-object-physics-replay";
        public int version = 1;
        public string unityVersion, architecture, outputPath, firstDifference, error, resetRemovalOrder;
        public string qualification = "Behavioral canary only: matching public state, queries, and callbacks does not prove private PhysX scene/cache parity.";
        public bool completed, passed, lateSpawn, autoSimulationDisabled, identitiesPreserved, emptyResetStep;
        public bool checkpointMatched;
        public int checkpointStep, totalSteps, spawnStep;
        public Identity identityBefore, identityAfter;
        public StepSample originalCheckpoint, replayCheckpoint;
        public StepSample[] original, replay;
        public PhysicsEvent[] originalEvents, replayEvents;
        public double originalSimulationMs, resetMs, replaySimulationMs;
    }

    private const int DefaultSteps = 24;
    private const int DefaultCheckpoint = 12;
    private const int DefaultSpawn = 7;
    private GameObject floorObject, primaryObject, lateObject;
    private BoxCollider floorCollider, lateCollider;
    private CapsuleCollider primaryCollider;
    private Rigidbody primaryBody, lateBody;
    private readonly List<PhysicsEvent> currentEvents = new List<PhysicsEvent>();
    private bool recording;
    private int currentStep;
    private Report report;
    private string outputPath;

    public static bool IsRequested()
    {
        string[] args = Environment.GetCommandLineArgs();
        foreach (string arg in args)
            if (arg == "--physicsReplayReset") return true;
        return false;
    }

    private void Awake()
    {
        Physics.autoSimulation = false;
        Time.captureFramerate = 60;
        Time.fixedDeltaTime = 0.02f;
        outputPath = Path.GetFullPath(Argument("--out", Path.Combine(
            Application.persistentDataPath, "physics-scene-replay.json")));
        report = new Report();
        report.outputPath = outputPath;
        report.unityVersion = Application.unityVersion;
        report.architecture = IntPtr.Size == 4 ? "x86" : "x64";
        report.lateSpawn = Boolean.Parse(Argument("--lateSpawn", "false"));
        report.resetRemovalOrder = Argument("--resetRemovalOrder", "reverse");
        report.emptyResetStep = Boolean.Parse(Argument("--emptyResetStep", "false"));
        report.totalSteps = Int32.Parse(Argument("--steps", DefaultSteps.ToString(CultureInfo.InvariantCulture)),
            CultureInfo.InvariantCulture);
        report.checkpointStep = Int32.Parse(Argument("--checkpoint", DefaultCheckpoint.ToString(CultureInfo.InvariantCulture)),
            CultureInfo.InvariantCulture);
        report.spawnStep = Int32.Parse(Argument("--spawnStep", DefaultSpawn.ToString(CultureInfo.InvariantCulture)),
            CultureInfo.InvariantCulture);
        report.autoSimulationDisabled = !Physics.autoSimulation;
    }

    private void Start()
    {
        try
        {
            if (report.totalSteps < 2 || report.checkpointStep < 1 ||
                report.checkpointStep >= report.totalSteps ||
                report.spawnStep < 1 || report.spawnStep >= report.totalSteps)
                throw new ArgumentException("Invalid step, checkpoint, or spawn count.");
            if (report.resetRemovalOrder != "forward" && report.resetRemovalOrder != "reverse")
                throw new ArgumentException("Use --resetRemovalOrder forward or reverse.");
            ConstructObjects();
            report.identityBefore = CaptureIdentity();
            ActivateInitialObjects();
            report.original = SimulateRun(out report.originalEvents, out report.originalSimulationMs);

            Stopwatch timer = Stopwatch.StartNew();
            ResetSameObjects();
            timer.Stop();
            report.resetMs = timer.Elapsed.TotalMilliseconds;
            report.identityAfter = CaptureIdentity();
            report.identitiesPreserved = SameIdentity(report.identityBefore, report.identityAfter);
            report.replay = SimulateRun(out report.replayEvents, out report.replaySimulationMs);
            report.originalCheckpoint = report.original[report.checkpointStep - 1];
            report.replayCheckpoint = report.replay[report.checkpointStep - 1];
            report.checkpointMatched = SameSample(report.originalCheckpoint, report.replayCheckpoint);
            report.firstDifference = FirstDifference();
            report.passed = report.autoSimulationDisabled && report.identitiesPreserved &&
                report.firstDifference == null;
            report.completed = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            report.passed = false;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            UnityEngine.Debug.Log("PHYSICS_SCENE_REPLAY_RESULT " +
                (report.passed ? "PASS" : "FAIL") + " " + report.firstDifference + " " + report.error);
            Application.Quit();
        }
    }

    private void ConstructObjects()
    {
        // Each component is added to an inactive object. Baseline and replay
        // therefore insert the same objects into the scene in the same order.
        floorObject = new GameObject("ReplayFloor");
        floorObject.SetActive(false);
        floorObject.transform.position = new Vector3(0f, -0.45f, 0f);
        floorCollider = floorObject.AddComponent<BoxCollider>();
        floorCollider.size = new Vector3(20f, 1f, 20f);

        primaryObject = new GameObject("ReplayCapsule");
        primaryObject.SetActive(false);
        primaryObject.transform.position = Vector3.zero;
        primaryBody = primaryObject.AddComponent<Rigidbody>();
        primaryBody.mass = 1f;
        primaryBody.drag = 0f;
        primaryBody.angularDrag = 0.05f;
        primaryBody.useGravity = false;
        primaryBody.constraints = RigidbodyConstraints.FreezeRotation;
        primaryBody.interpolation = RigidbodyInterpolation.None;
        primaryBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        primaryCollider = primaryObject.AddComponent<CapsuleCollider>();
        primaryCollider.center = new Vector3(0f, 1f, 0f);
        primaryCollider.radius = 0.4f;
        primaryCollider.height = 2f;
        PhysicsReplayEventRecorder primaryRecorder = primaryObject.AddComponent<PhysicsReplayEventRecorder>();
        primaryRecorder.owner = this;
        primaryRecorder.label = "capsule";

        if (!report.lateSpawn) return;
        lateObject = new GameObject("ReplayLateBox");
        lateObject.SetActive(false);
        lateObject.transform.position = new Vector3(0.65f, 0f, 0f);
        lateBody = lateObject.AddComponent<Rigidbody>();
        lateBody.mass = 1f;
        lateBody.drag = 0f;
        lateBody.angularDrag = 0.05f;
        lateBody.useGravity = false;
        lateBody.constraints = RigidbodyConstraints.FreezeRotation;
        lateBody.interpolation = RigidbodyInterpolation.None;
        lateBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        lateCollider = lateObject.AddComponent<BoxCollider>();
        lateCollider.center = new Vector3(0f, 1f, 0f);
        lateCollider.size = Vector3.one;
        PhysicsReplayEventRecorder lateRecorder = lateObject.AddComponent<PhysicsReplayEventRecorder>();
        lateRecorder.owner = this;
        lateRecorder.label = "late-box";
    }

    private void ActivateInitialObjects()
    {
        floorObject.SetActive(true);
        primaryObject.SetActive(true);
        primaryBody.WakeUp();
        Physics.SyncTransforms();
    }

    private void ResetSameObjects()
    {
        recording = false;
        if (report.resetRemovalOrder == "forward")
        {
            floorObject.SetActive(false);
            primaryObject.SetActive(false);
            if (lateObject != null) lateObject.SetActive(false);
        }
        else
        {
            if (lateObject != null) lateObject.SetActive(false);
            primaryObject.SetActive(false);
            floorObject.SetActive(false);
        }
        if (report.emptyResetStep) Physics.Simulate(0.02f);

        floorObject.transform.position = new Vector3(0f, -0.45f, 0f);
        floorObject.transform.rotation = Quaternion.identity;
        primaryObject.transform.position = Vector3.zero;
        primaryObject.transform.rotation = Quaternion.identity;
        primaryBody.position = Vector3.zero;
        primaryBody.rotation = Quaternion.identity;
        primaryBody.velocity = Vector3.zero;
        primaryBody.angularVelocity = Vector3.zero;
        primaryBody.isKinematic = false;
        primaryBody.useGravity = false;
        if (lateObject != null)
        {
            lateObject.transform.position = new Vector3(0.65f, 0f, 0f);
            lateObject.transform.rotation = Quaternion.identity;
            lateBody.position = new Vector3(0.65f, 0f, 0f);
            lateBody.rotation = Quaternion.identity;
            lateBody.velocity = Vector3.zero;
            lateBody.angularVelocity = Vector3.zero;
            lateBody.isKinematic = false;
            lateBody.useGravity = false;
        }
        ActivateInitialObjects();
    }

    private StepSample[] SimulateRun(out PhysicsEvent[] events, out double simulationMs)
    {
        List<StepSample> samples = new List<StepSample>();
        currentEvents.Clear();
        recording = true;
        Stopwatch timer = Stopwatch.StartNew();
        for (int step = 0; step < report.totalSteps; step++)
        {
            currentStep = step;
            if (lateObject != null && step == report.spawnStep)
            {
                lateObject.SetActive(true);
                lateBody.WakeUp();
            }
            if (step == 4) primaryBody.AddForce(new Vector3(0.2f, 0f, 0f), ForceMode.Impulse);
            if (step == 14) primaryBody.velocity = new Vector3(-0.1f, 0f, 0f);
            Physics.Simulate(0.02f);
            samples.Add(CaptureSample(step));
        }
        timer.Stop();
        recording = false;
        events = currentEvents.ToArray();
        simulationMs = timer.Elapsed.TotalMilliseconds;
        return samples.ToArray();
    }

    private StepSample CaptureSample(int step)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(0f, 4f, 0f), Vector3.down, 10f);
        string[] hitNames = new string[hits.Length];
        for (int i = 0; i < hits.Length; i++)
            hitNames[i] = hits[i].collider.gameObject.name + ":" +
                Bits(hits[i].distance).ToString(CultureInfo.InvariantCulture);
        Collider[] overlaps = Physics.OverlapSphere(new Vector3(0f, 0.5f, 0f), 1.5f);
        string[] overlapNames = new string[overlaps.Length];
        for (int i = 0; i < overlaps.Length; i++)
            overlapNames[i] = overlaps[i].gameObject.name;
        return new StepSample
        {
            step = step,
            primarySleeping = primaryBody.IsSleeping(),
            primaryBits = BodyBits(primaryBody, primaryCollider),
            floorBits = BoundsBits(floorCollider.bounds),
            lateActive = lateObject != null && lateObject.activeSelf,
            lateSleeping = lateObject != null && lateObject.activeSelf && lateBody.IsSleeping(),
            lateBits = lateObject != null && lateObject.activeSelf ? BodyBits(lateBody, lateCollider) : new int[0],
            raycastHits = hitNames,
            overlapHits = overlapNames
        };
    }

    private static int[] BodyBits(Rigidbody body, Collider collider)
    {
        Vector3 p = body.position, v = body.velocity, a = body.angularVelocity, t = body.transform.position;
        Quaternion r = body.rotation, tr = body.transform.rotation;
        Bounds b = collider.bounds;
        return new[] {
            Bits(p.x), Bits(p.y), Bits(p.z), Bits(r.x), Bits(r.y), Bits(r.z), Bits(r.w),
            Bits(v.x), Bits(v.y), Bits(v.z), Bits(a.x), Bits(a.y), Bits(a.z),
            Bits(t.x), Bits(t.y), Bits(t.z), Bits(tr.x), Bits(tr.y), Bits(tr.z), Bits(tr.w),
            Bits(b.center.x), Bits(b.center.y), Bits(b.center.z),
            Bits(b.extents.x), Bits(b.extents.y), Bits(b.extents.z)
        };
    }

    private static int[] BoundsBits(Bounds value)
    {
        return new[] { Bits(value.center.x), Bits(value.center.y), Bits(value.center.z),
            Bits(value.extents.x), Bits(value.extents.y), Bits(value.extents.z) };
    }

    private Identity CaptureIdentity()
    {
        return new Identity {
            floorObject = floorObject.GetInstanceID(), floorCollider = floorCollider.GetInstanceID(),
            primaryObject = primaryObject.GetInstanceID(), primaryBody = primaryBody.GetInstanceID(),
            primaryCollider = primaryCollider.GetInstanceID(),
            lateObject = lateObject == null ? 0 : lateObject.GetInstanceID(),
            lateBody = lateBody == null ? 0 : lateBody.GetInstanceID(),
            lateCollider = lateCollider == null ? 0 : lateCollider.GetInstanceID()
        };
    }

    private static bool SameIdentity(Identity a, Identity b)
    {
        return a.floorObject == b.floorObject && a.floorCollider == b.floorCollider &&
            a.primaryObject == b.primaryObject && a.primaryBody == b.primaryBody &&
            a.primaryCollider == b.primaryCollider && a.lateObject == b.lateObject &&
            a.lateBody == b.lateBody && a.lateCollider == b.lateCollider;
    }

    private string FirstDifference()
    {
        if (report.original.Length != report.replay.Length) return "sample-count";
        int originalEvent = 0, replayEvent = 0;
        for (int i = 0; i < report.original.Length; i++)
        {
            StepSample a = report.original[i], b = report.replay[i];
            if (!SameBodySample(a, b))
                return "body sample step " + i.ToString(CultureInfo.InvariantCulture);
            int originalStart = originalEvent, replayStart = replayEvent;
            while (originalEvent < report.originalEvents.Length && report.originalEvents[originalEvent].step == i)
                originalEvent++;
            while (replayEvent < report.replayEvents.Length && report.replayEvents[replayEvent].step == i)
                replayEvent++;
            if (originalEvent - originalStart != replayEvent - replayStart)
                return "callback count step " + i.ToString(CultureInfo.InvariantCulture);
            for (int j = 0; j < originalEvent - originalStart; j++)
                if (!SameEvent(report.originalEvents[originalStart + j], report.replayEvents[replayStart + j]))
                    return "callback order/value step " + i.ToString(CultureInfo.InvariantCulture) +
                        " event " + j.ToString(CultureInfo.InvariantCulture);
            if (!Same(a.raycastHits, b.raycastHits) || !Same(a.overlapHits, b.overlapHits))
                return "query order/value step " + i.ToString(CultureInfo.InvariantCulture);
        }
        if (originalEvent != report.originalEvents.Length || replayEvent != report.replayEvents.Length)
            return "callback trailing count";
        return null;
    }

    private static bool SameSample(StepSample a, StepSample b)
    {
        return SameBodySample(a, b) && Same(a.raycastHits, b.raycastHits) &&
            Same(a.overlapHits, b.overlapHits);
    }

    private static bool SameBodySample(StepSample a, StepSample b)
    {
        return a != null && b != null && a.step == b.step &&
            a.primarySleeping == b.primarySleeping && a.lateActive == b.lateActive &&
            a.lateSleeping == b.lateSleeping && Same(a.primaryBits, b.primaryBits) &&
            Same(a.lateBits, b.lateBits) && Same(a.floorBits, b.floorBits);
    }

    private static bool SameEvent(PhysicsEvent a, PhysicsEvent b)
    {
        return a.step == b.step && a.owner == b.owner && a.kind == b.kind &&
            a.other == b.other && a.contactCount == b.contactCount &&
            Same(a.relativeVelocityBits, b.relativeVelocityBits);
    }

    private static bool Same<T>(T[] a, T[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
        return true;
    }

    public void Record(string label, string kind, Collision value)
    {
        if (!recording) return;
        Vector3 relative = value.relativeVelocity;
        currentEvents.Add(new PhysicsEvent { step = currentStep, owner = label, kind = kind,
            other = value.gameObject.name, contactCount = value.contacts.Length,
            relativeVelocityBits = new[] { Bits(relative.x), Bits(relative.y), Bits(relative.z) } });
    }

    public void Record(string label, string kind, Collider value)
    {
        if (!recording) return;
        currentEvents.Add(new PhysicsEvent { step = currentStep, owner = label, kind = kind,
            other = value.gameObject.name, contactCount = 0, relativeVelocityBits = new int[0] });
    }

    private static int Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }

    private static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
