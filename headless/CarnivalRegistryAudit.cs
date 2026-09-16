using Hpmv;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

/// <summary>Checks received initial registrations; never substitutes setup predictions for missing observations.</summary>
public sealed class CarnivalRegistryAudit
{
    public const double PositionTolerance = .0001;
    private static readonly Lazy<JsonObject> Reference = new(() =>
    {
        using var stream = typeof(CarnivalRegistryAudit).Assembly.GetManifestResourceStream("Headless.Reference.Carnival34NativeReference.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    });
    private readonly GameSetup setup;
    private readonly bool discoveryOnly;
    private readonly JsonObject reference;
    private readonly Dictionary<int, Vector3> declaredRegistrationPositions;
    private readonly Dictionary<int, Point> settledChefReference = new();
    private readonly Dictionary<int, (Point Position, string Source)> settledChefs = new();
    private bool nativeInLevelObserved;
    private readonly Dictionary<int, EntityRegistryData> first = new();
    private readonly Dictionary<int, EntityRegistryData> latest = new();
    private readonly HashSet<int> changed = new();
    private readonly HashSet<int> removed = new();
    private sealed class FixedReincarnationReceipt
    {
        public int Frame;
        public Dictionary<int, EntityRegistryData> Metadata = new();
    }
    private FixedReincarnationReceipt fixedReincarnation;
    private readonly HashSet<int> poisonedFixedReincarnations = new();
    private sealed class MappedReceipt
    {
        public PrefabRecord Prefab;
        public EntityRegistryData Metadata;
        public int LastNativeId, FirstFrame;
        public HashSet<int> NativeIds = new();
        public string Error;
        public bool AcceptRebranchSubset;
        public GameEntityRecord SpawnParent;
        public int? SpawnableId;
        public HashSet<int> SpawnParentNativeIds = new();
        public HashSet<int> SpawnChildNativeIds = new();
    }
    private readonly Dictionary<GameEntityRecord, MappedReceipt> mappedReceipts = new();
    private readonly Dictionary<int, GameEntityRecord> physicalContainers = new();
    // Explicit framework synchronizer replacements; retain and report their
    // identities rather than accepting arbitrary suffixes as native types.
    private static readonly HashSet<string> CannonReplacements = new(new[] { "ClientCannon", "ServerCannon", "ClientCannonPlayerHandler", "ServerCannonPlayerHandler",
        "ClientCannonSessionInteractable", "ServerCannonSessionInteractable", "ClientCannonCosmeticDecisions", "ServerCannonCosmeticDecisions" });
    private bool fresh;
    public CarnivalRegistryAudit(GameSetup setup, bool discoveryOnly = false, string levelProfile = null)
    {
        this.setup = setup;
        this.discoveryOnly = discoveryOnly || setup is Story11FourLevel { InspectionOnly: true };
        levelProfile ??= setup is Story11FourLevel ? "story11" : "carnival34";
        if (levelProfile == "carnival34") reference = Reference.Value;
        else if (levelProfile == "story11") {
            using var stream = typeof(CarnivalRegistryAudit).Assembly.GetManifestResourceStream("Headless.Reference.Story11NativeReference.json")!;
            reference = JsonNode.Parse(stream)!.AsObject();
        } else throw new ArgumentException("Unknown native registry profile: " + levelProfile);
        // The framework subsequently replaces versioned frame-zero poses with
        // actual startup physics. Registration precedes native attachment
        // settling, so its reference must not alias that mutable history.
        declaredRegistrationPositions = setup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0], e => e.position[0]);
        if (reference["settledChefPositions"] is JsonArray chefPositions)
            foreach (var chef in chefPositions)
                settledChefReference.Add(chef!["id"]!.GetValue<int>(), chef["position"]!.Deserialize<Point>(RuntimeHost.Json)!);
    }
    public void Reset() { first.Clear(); latest.Clear(); changed.Clear(); removed.Clear(); mappedReceipts.Clear(); physicalContainers.Clear(); settledChefs.Clear(); fixedReincarnation = null; poisonedFixedReincarnations.Clear(); nativeInLevelObserved = false; fresh = true; }
    public void ObserveStartupFrame(OutputData output)
    {
        if (!fresh || settledChefReference.Count == 0) return;
        if (output.ServerMessages?.Any(m => m.Type == (int)MessageType.GameState &&
            Deserializer.Deserialize(m.Type, m.Message) is GameStateMessage { m_State: GameState.InLevel }) == true)
            nativeInLevelObserved = true;
        if (!nativeInLevelObserved) return;
        foreach (int id in settledChefReference.Keys)
        {
            if (settledChefs.ContainsKey(id)) continue;
            var expected = settledChefReference[id];
            if (output.Items?.TryGetValue(id, out var item) == true && item.__isset.pos && item.Pos is not null &&
                WithinSettledChefTolerance(item.Pos, expected))
            {
                settledChefs.Add(id, (item.Pos.DeepCopy(), "first matching native ItemData position at/after InLevel"));
                continue;
            }
            if (output.EntityRegistry?.FirstOrDefault(e => e.EntityId == id) is { Pos: not null } metadata &&
                WithinSettledChefTolerance(metadata.Pos, expected))
                settledChefs.Add(id, (metadata.Pos.DeepCopy(), "first matching native registry metadata position at/after InLevel"));
        }
    }
    private static bool WithinSettledChefTolerance(Point observed, Point expected)
    {
        if (!double.IsFinite(observed.X) || !double.IsFinite(observed.Y) || !double.IsFinite(observed.Z)) return false;
        double dx = observed.X - expected.X, dy = observed.Y - expected.Y, dz = observed.Z - expected.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= PositionTolerance;
    }
    public JsonArray SettledChefPositions() => new(settledChefs.OrderBy(p => p.Key).Select(p => (JsonNode)new JsonObject {
        ["id"] = p.Key, ["position"] = JsonSerializer.SerializeToNode(p.Value.Position, RuntimeHost.Json), ["source"] = p.Value.Source }).ToArray());
    public void ObserveSettledChefInspection(JsonObject inspection)
    {
        if (!fresh || settledChefReference.Count == 0) return;
        // New full inspections carry the immutable first matching native startup receipt.
        // Older captured post-kitchen fixtures carry the original refreshed registry.
        if (inspection["settledChefPositions"] is JsonArray receipts) {
            foreach (var entry in receipts) {
                int id = entry!["id"]!.GetValue<int>();
                if (settledChefReference.ContainsKey(id)) settledChefs.TryAdd(id,
                    (entry["position"]!.Deserialize<Point>(RuntimeHost.Json)!, entry["source"]!.ToString()));
            }
        } else if (inspection["initialRegistry"] is JsonArray && inspection["registry"] is JsonArray registry) {
            foreach (var entry in registry.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!)
                if (settledChefReference.ContainsKey(entry.EntityId) && entry.Pos is not null)
                    settledChefs.TryAdd(entry.EntityId, (entry.Pos.DeepCopy(), "captured post-kitchen inspection registry metadata"));
        }
    }
    public void ObserveRemoval(int id) { removed.Add(id); physicalContainers.Remove(id); }
    public JsonObject InspectRetiredOwnerProxy(int id, IReadOnlyDictionary<int, GameEntityRecord> live, int frame)
    {
        if (!fresh || live.ContainsKey(id) || changed.Contains(id) || removed.Contains(id) ||
            !physicalContainers.TryGetValue(id, out var owner) || owner.existed[frame] || owner.path.ids.Length < 2 ||
            !mappedReceipts.TryGetValue(owner, out var receipt) || receipt.Error is not null || !ReferenceEquals(receipt.Prefab, owner.prefab) ||
            live.Values.Any(e => ReferenceEquals(e, owner)) || !latest.TryGetValue(id, out var metadata) ||
            metadata.Components?.Contains("Rigidbody") != true || !metadata.Components.Contains("ObjectContainer") ||
            metadata.SyncEntityTypes?.Contains((int)EntityType.PhysicsObject) != true) return null;
        return new JsonObject { ["nativeId"] = id, ["frame"] = frame, ["logicalOwnerPath"] = JsonSerializer.SerializeToNode(owner.path.ids),
            ["lastObservedOwnerNativeId"] = receipt.LastNativeId, ["ownerExists"] = false,
            ["nativeName"] = metadata.Name, ["registrySha256"] = RegistryHash(),
            ["source"] = "observed SpawnPhysicalAttachment association, retired logical owner, and framework retirement message",
            ["scope"] = "Proxy has no logical entity record; logical lookup skipped only for this proven association. Message retained unchanged. Historical native RemoveEntry provenance is not implied; retain the emitter's observation receipt." };
    }
    public void ObservePhysicalContainers(IEnumerable<ServerMessage> messages, IReadOnlyDictionary<int, GameEntityRecord> live)
    {
        foreach (var message in messages)
        {
            if (message.Type != (int)MessageType.SpawnPhysicalAttachment || Deserializer.Deserialize(message.Type, message.Message) is not SpawnPhysicalAttachmentMessage spawn) continue;
            int owner = (int)spawn.m_SpawnEntityData.m_DesiredHeader.m_uEntityID, body = (int)spawn.m_ContainerHeader.m_uEntityID;
            if (body > 0 && body != owner && live.TryGetValue(owner, out var record))
            {
                if (physicalContainers.TryGetValue(body, out var previous) && !ReferenceEquals(previous, record)) throw new InvalidOperationException("Native physical container changed owner without an actual retirement receipt.");
                physicalContainers[body] = record;
            }
        }
    }
    public void ObserveSpawnMappings(IEnumerable<ServerMessage> messages, IReadOnlyDictionary<int, GameEntityRecord> live, int frame)
    {
        foreach (var message in messages)
        {
            SpawnEntityMessage spawn = message.Type switch
            {
                (int)MessageType.SpawnEntity => Deserializer.Deserialize(message.Type, message.Message) as SpawnEntityMessage,
                (int)MessageType.SpawnPhysicalAttachment =>
                    (Deserializer.Deserialize(message.Type, message.Message) as SpawnPhysicalAttachmentMessage)?.m_SpawnEntityData,
                _ => null
            };
            if (spawn is null) continue;
            int parentId = (int)spawn.m_SpawnerHeader.m_uEntityID;
            int childId = (int)spawn.m_DesiredHeader.m_uEntityID;
            if (!live.TryGetValue(childId, out var child) || !child.existed[frame] ||
                !mappedReceipts.TryGetValue(child, out var receipt)) continue;
            // Some replacement messages consume their parent later in the same
            // output packet.  The post-packet map cannot authenticate that
            // header; leave it unrecorded so ordinary ordered parent metadata
            // must carry the proof.  A missing-order parent will still reject.
            if (!live.TryGetValue(parentId, out var parent) || !parent.existed[frame]) continue;
            if (!ReferenceEquals(child.spawner, parent) || spawn.m_SpawnableID < 0 ||
                spawn.m_SpawnableID >= parent.prefab.Spawns.Count ||
                !ReferenceEquals(parent.prefab.Spawns[spawn.m_SpawnableID], child.prefab))
            {
                receipt.Error = "Observed native spawn headers do not identify the reconstructed parent/prefab index.";
                continue;
            }
            if ((receipt.SpawnParent is not null && !ReferenceEquals(receipt.SpawnParent, parent)) ||
                (receipt.SpawnableId is int prior && prior != spawn.m_SpawnableID))
            {
                receipt.Error = "Observed native spawn parent/prefab index changed across incarnations.";
                continue;
            }
            receipt.SpawnParent = parent;
            receipt.SpawnableId = spawn.m_SpawnableID;
            receipt.SpawnParentNativeIds.Add(parentId);
            receipt.SpawnChildNativeIds.Add(childId);
        }
    }
    private bool ValidPhysicalContainer(int id, IReadOnlyDictionary<int, GameEntityRecord> live, int frame)
    {
        if (!physicalContainers.TryGetValue(id, out var owner) || !owner.existed[frame] || !live.Values.Any(e => ReferenceEquals(e, owner)) ||
            !latest.TryGetValue(id, out var body)) return false;
        return !changed.Contains(id) && body.Components?.Contains("Rigidbody") == true && body.Components.Contains("ObjectContainer") &&
            body.SyncEntityTypes?.Contains((int)EntityType.PhysicsObject) == true;
    }
    public void ObserveMappedEntities(IReadOnlyDictionary<int, GameEntityRecord> live, int frame)
    {
        if (!fresh) return;
        foreach (var (id, entity) in live)
        {
            if (!entity.existed[frame] || !latest.TryGetValue(id, out var metadata)) continue;
            if (!mappedReceipts.TryGetValue(entity, out var receipt))
                mappedReceipts[entity] = receipt = new() { Prefab = entity.prefab, Metadata = metadata.DeepCopy(), LastNativeId = id, FirstFrame = frame };
            else
            {
                bool compatibleRebranchSubset = receipt.AcceptRebranchSubset && ReferenceEquals(receipt.Prefab, entity.prefab) &&
                    receipt.Metadata.Name == metadata.Name && receipt.Metadata.Components is not null && metadata.Components is { Count: > 0 } &&
                    !metadata.Components.Except(receipt.Metadata.Components).Any() &&
                    (metadata.SpawnNames is null || (receipt.Metadata.SpawnNames is not null && metadata.SpawnNames.SequenceEqual(receipt.Metadata.SpawnNames)));
                bool completeRebranchMetadata = compatibleRebranchSubset && !receipt.Metadata.Components.Except(metadata.Components).Any() &&
                    (receipt.Metadata.SpawnNames is null || (metadata.SpawnNames is not null && receipt.Metadata.SpawnNames.SequenceEqual(metadata.SpawnNames)));
                if (compatibleRebranchSubset && !completeRebranchMetadata)
                {
                    receipt.LastNativeId = id; receipt.NativeIds.Add(id); continue;
                }
                receipt.AcceptRebranchSubset = false;
                if (!ReferenceEquals(receipt.Prefab, entity.prefab) || receipt.Metadata.Name != metadata.Name ||
                    receipt.Metadata.Components?.Except(metadata.Components ?? new()).Any() == true ||
                    (receipt.Metadata.SpawnNames is not null && (metadata.SpawnNames is null || !receipt.Metadata.SpawnNames.SequenceEqual(metadata.SpawnNames))))
                    receipt.Error = "Observed record prefab/name/component/spawn identity changed across native mappings.";
                receipt.Metadata = metadata.DeepCopy(); receipt.LastNativeId = id;
            }
            receipt.NativeIds.Add(id);
        }
    }
    public JsonObject RebranchAfterSuccessfulWarp(IReadOnlyDictionary<int, GameEntityRecord> live, int frame,
        IReadOnlySet<int> freshlyRegisteredIds)
    {
        if (!fresh) return new JsonObject { ["frame"] = frame, ["active"] = false };
        JsonObject fixedReincarnationResult = RebranchStory11InitialPlateReincarnation(live, frame, freshlyRegisteredIds);
        // A consumed dynamic record is still required when a descendant exists
        // at the restored frame: its observed SpawnNames bind every child path
        // to the native prefab index.  Such an ancestor is part of the current
        // branch even though it no longer has a live native ID.  Only records
        // absent from both the target and every current target ancestry chain
        // belong exclusively to the abandoned future.
        var retainedAncestors = new HashSet<GameEntityRecord>();
        foreach (var entity in live.Values.Where(entity => entity.existed[frame]))
            for (var ancestor = entity.spawner; ancestor is not null; ancestor = ancestor.spawner)
                if (ancestor.path.ids.Length > 1 && !ancestor.existed[frame]) retainedAncestors.Add(ancestor);
        // A record absent at the target is not necessarily from the abandoned
        // future: it may have existed and been consumed earlier on the retained
        // branch.  Keep those receipts so a later non-adjacent rewind can still
        // authenticate the historical spawn chain.  FirstFrame is the first
        // actual native mapping observation for this logical record, so only a
        // receipt first observed after the target can belong exclusively to the
        // discarded future.
        var discarded = mappedReceipts.Where(pair => pair.Key.path.ids.Length > 1 && !pair.Key.existed[frame] &&
                !retainedAncestors.Contains(pair.Key) && pair.Value.FirstFrame > frame)
            .Select(pair => pair.Key).ToArray();
        foreach (var entity in discarded) mappedReceipts.Remove(entity);
        var retainedHistory = mappedReceipts.Where(pair => pair.Key.path.ids.Length > 1 && !pair.Key.existed[frame] &&
                !retainedAncestors.Contains(pair.Key) && pair.Value.FirstFrame <= frame)
            .Select(pair => pair.Key).ToArray();
        var rebased = new JsonArray();
        foreach (var (id, entity) in live.OrderBy(pair => pair.Key))
        {
            if (entity.path.ids.Length < 2 || !entity.existed[frame] || !freshlyRegisteredIds.Contains(id) ||
                !latest.TryGetValue(id, out var metadata)) continue;
            mappedReceipts.TryGetValue(entity, out var previous);
            bool preservePriorMetadata = previous is not null && ReferenceEquals(previous.Prefab, entity.prefab) &&
                previous.Metadata.Name == metadata.Name && previous.Metadata.Components is not null && metadata.Components is { Count: > 0 } &&
                !metadata.Components.Except(previous.Metadata.Components).Any() &&
                (metadata.SpawnNames is null || (previous.Metadata.SpawnNames is not null && metadata.SpawnNames.SequenceEqual(previous.Metadata.SpawnNames)));
            var receipt = new MappedReceipt { Prefab = entity.prefab,
                Metadata = (preservePriorMetadata ? previous.Metadata : metadata).DeepCopy(),
                LastNativeId = id, FirstFrame = previous is null ? frame : Math.Min(previous.FirstFrame, frame) };
            receipt.AcceptRebranchSubset = preservePriorMetadata;
            if (previous is not null)
            {
                receipt.NativeIds.UnionWith(previous.NativeIds);
            }
            receipt.NativeIds.Add(id); mappedReceipts[entity] = receipt;
            rebased.Add(new JsonObject { ["id"] = id, ["path"] = JsonSerializer.SerializeToNode(entity.path.ids),
                ["prefab"] = entity.prefab?.Name, ["priorReceipt"] = previous is not null,
                ["preservedPriorMetadata"] = preservePriorMetadata });
        }
        return new JsonObject { ["frame"] = frame, ["active"] = true,
            ["freshlyRegisteredIds"] = JsonSerializer.SerializeToNode(freshlyRegisteredIds.Order()),
            ["discardedFuturePaths"] = JsonSerializer.SerializeToNode(discarded.Select(entity => entity.path.ids)),
            ["retainedHistoricalPaths"] = JsonSerializer.SerializeToNode(retainedHistory
                .OrderBy(entity => string.Join(",", entity.path.ids)).Select(entity => entity.path.ids)),
            ["retainedHistoricalAncestorPaths"] = JsonSerializer.SerializeToNode(retainedAncestors
                .Where(mappedReceipts.ContainsKey).OrderBy(entity => string.Join(",", entity.path.ids)).Select(entity => entity.path.ids)),
            ["rebasedCurrentMappings"] = rebased,
            ["fixedReincarnation"] = fixedReincarnationResult,
            ["nativeStateChanged"] = false,
            ["scope"] = "Successful-warp controller bookkeeping only: receipts first observed after the target and exclusive to the abandoned future are discarded; earlier historical receipts and observed metadata for consumed ancestors of current target entities are retained for later deeper rewinds; current dynamic mappings are rebased only from registry rows delivered by the verified warp acknowledgement. The Story 1-1 initial plate/container exception admits only the exact paired recreation metadata produced by the native restore." };
    }
    private JsonObject RebranchStory11InitialPlateReincarnation(IReadOnlyDictionary<int, GameEntityRecord> live, int frame,
        IReadOnlySet<int> freshlyRegisteredIds)
    {
        if (reference["scene"]?.ToString() != "s_sushi_1_1")
            return new JsonObject { ["active"] = false, ["reason"] = "level-not-story11" };
        int[] ids = { 2, 47 };
        var fixedIds = setup.entityRecords.FixedEntities.Select(entity => entity.path.ids[0]).ToHashSet();
        var freshFixed = freshlyRegisteredIds.Where(fixedIds.Contains).Order().ToArray();
        bool mentionsPair = ids.Any(freshlyRegisteredIds.Contains);
        if (!mentionsPair) return new JsonObject { ["active"] = false, ["reason"] = "pair-not-fresh" };
        if (!freshFixed.SequenceEqual(ids))
            throw new InvalidOperationException("Story 1-1 initial plate reincarnation requires exactly fixed IDs 2 and 47 to be freshly registered together.");
        foreach (int id in ids)
        {
            if (!live.TryGetValue(id, out var entity) || !entity.existed[frame] || !entity.path.ids.SequenceEqual(new[] { id }))
                throw new InvalidOperationException("Story 1-1 initial plate reincarnation requires exact live fixed paths [2] and [47] at the warp target.");
            if (!first.ContainsKey(id) || !latest.ContainsKey(id))
                throw new InvalidOperationException("Story 1-1 initial plate reincarnation is missing initial or current registry metadata.");
        }
        if (changed.Any(id => fixedIds.Contains(id) && id is not 2 and not 47))
            throw new InvalidOperationException("Another fixed native ID changed during the Story 1-1 initial plate reincarnation.");
        if (poisonedFixedReincarnations.Count != 0)
            throw new InvalidOperationException("Story 1-1 initial plate reincarnation metadata was mutated after authorization.");

        var owner = latest[2]; var body = latest[47];
        var initialOwner = first[2]; var initialBody = first[47];
        var origin = declaredRegistrationPositions[34];
        if (initialOwner.Name != "Plate 1 (3)" || initialBody.Name != "Plate 1 (3)_Rigidbody" ||
            owner.Name != "equipment_plate_01" || body.Name != "equipment_plate_01(Clone)_Rigidbody")
            throw new InvalidOperationException("Story 1-1 initial plate reincarnation names do not match the exact observed factory recreation.");
        if (!ExactPoint(owner.Pos, origin) || !ExactPoint(body.Pos, origin) || !ExactPoint(owner.Pos, body.Pos))
            throw new InvalidOperationException("Story 1-1 initial plate reincarnation registry positions do not match the exact plate-return factory origin.");
        if (!ExactComponentTransformation(initialOwner.Components, owner.Components, new[] { "CachedObject" }, Array.Empty<string>()) ||
            !ExactComponentTransformation(initialBody.Components, body.Components, new[] { "CachedObject" },
                new[] { "DynamicLandscapeParenting", "ServerDynamicLandscapeParenting", "ClientDynamicLandscapeParenting" }))
            throw new InvalidOperationException("Story 1-1 initial plate reincarnation component multisets do not match the exact observed transformation.");
        if (!ExactSequence(initialOwner.SyncEntityTypes, owner.SyncEntityTypes) || !ExactSequence(initialBody.SyncEntityTypes, body.SyncEntityTypes) ||
            owner.SpawnNames is not null || body.SpawnNames is not null)
            throw new InvalidOperationException("Story 1-1 initial plate reincarnation synchronization or spawn metadata differs from the exact observed recreation.");

        fixedReincarnation = new FixedReincarnationReceipt { Frame = frame,
            Metadata = ids.ToDictionary(id => id, id => latest[id].DeepCopy()) };
        return FixedReincarnationReport();
    }
    private static bool ExactPoint(Point value, Vector3 expected) => value is not null &&
        value.X == expected.X && value.Y == expected.Y && value.Z == expected.Z;
    private static bool ExactPoint(Point left, Point right) => left is not null && right is not null &&
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    private static bool ExactSequence<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) =>
        left is not null && right is not null && left.SequenceEqual(right);
    private static bool ExactMultiset(IEnumerable<string> left, IEnumerable<string> right) => left is not null && right is not null &&
        left.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count())
            .OrderBy(pair => pair.Key).SequenceEqual(right.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count()).OrderBy(pair => pair.Key));
    private static bool ExactComponentTransformation(IReadOnlyList<string> initial, IReadOnlyList<string> current,
        IEnumerable<string> removedComponents, IEnumerable<string> addedComponents)
    {
        if (initial is null || current is null) return false;
        var expected = initial.ToList();
        foreach (string component in removedComponents)
            if (!expected.Remove(component)) return false;
        expected.AddRange(addedComponents);
        return ExactMultiset(expected, current);
    }
    private static bool ExactMetadata(EntityRegistryData left, EntityRegistryData right) => left is not null && right is not null &&
        left.EntityId == right.EntityId && left.Name == right.Name && ExactPoint(left.Pos, right.Pos) &&
        ExactMultiset(left.Components, right.Components) && ExactSequence(left.SyncEntityTypes, right.SyncEntityTypes) &&
        ((left.SpawnNames is null && right.SpawnNames is null) || ExactSequence(left.SpawnNames, right.SpawnNames));
    private bool AuthorizedFixedReincarnation(int id) => fixedReincarnation?.Metadata.TryGetValue(id, out var expected) == true &&
        !poisonedFixedReincarnations.Contains(id) && latest.TryGetValue(id, out var actual) && ExactMetadata(expected, actual);
    private JsonObject FixedReincarnationReport() => fixedReincarnation is null
        ? new JsonObject { ["active"] = false }
        : new JsonObject { ["active"] = true, ["frame"] = fixedReincarnation.Frame,
            ["ids"] = JsonSerializer.SerializeToNode(fixedReincarnation.Metadata.Keys.Order()),
            ["currentMetadataExact"] = fixedReincarnation.Metadata.Keys.All(AuthorizedFixedReincarnation),
            ["poisonedIds"] = JsonSerializer.SerializeToNode(poisonedFixedReincarnations.Order()),
            ["source"] = "Exact paired fixed-ID recreation delivered by a successful native warp acknowledgement; validation bookkeeping only." };
    private EntityRegistryData MetadataFor(GameEntityRecord entity, IReadOnlyDictionary<int, GameEntityRecord> live)
    {
        if (mappedReceipts.TryGetValue(entity, out var receipt) && (receipt.Error is not null || !ReferenceEquals(receipt.Prefab, entity.prefab)))
            throw new InvalidOperationException(receipt.Error ?? "Historical record prefab identity changed.");
        var current = live.Where(p => ReferenceEquals(p.Value, entity)).ToArray();
        if (current.Length > 1) throw new InvalidOperationException("Multiple native IDs map to the same current logical record.");
        if (current.Length == 1 && latest.TryGetValue(current[0].Key, out var metadata)) return metadata;
        if (receipt is not null) return receipt.Metadata;
        throw new InvalidOperationException("Historical spawned parent/target has no actual mapped registry receipt.");
    }
    private void ValidateNativeChild(GameEntityRecord entity, GameEntityRecord parent, IReadOnlyDictionary<int, GameEntityRecord> live)
    {
        var own = MetadataFor(entity, live); var source = MetadataFor(parent, live);
        int prefabIndex = parent.prefab.Spawns.IndexOf(entity.prefab);
        // Framework Name is often a display label (Bun), while the observed
        // native prefab is HotdogBun. The original native SpawnEntity decoder
        // selected this exact declared prefab INDEX; bind the actual ordered
        // native entry/name to that reference, rather than inventing aliases.
        if (prefabIndex < 0) throw new InvalidOperationException("Actual native spawn does not identify a declared parent prefab index.");
        if (source.SpawnNames is not null)
        {
            if (source.SpawnNames.Count != parent.prefab.Spawns.Count || source.SpawnNames.Any(string.IsNullOrEmpty))
                throw new InvalidOperationException("Complete actual ordered parent spawn metadata does not match the declared index space.");
            string expected = source.SpawnNames[prefabIndex];
            if (own.Name != expected && own.Name != expected + "(Clone)") throw new InvalidOperationException("Actual spawned name conflicts with its observed native parent prefab index.");
        }
        else if (!mappedReceipts.TryGetValue(entity, out var receipt) || receipt.Error is not null ||
            !ReferenceEquals(receipt.Prefab, entity.prefab) || !ReferenceEquals(receipt.SpawnParent, parent) ||
            receipt.SpawnableId != prefabIndex || receipt.SpawnParentNativeIds.Count == 0 || receipt.SpawnChildNativeIds.Count == 0 ||
            live.Where(pair => ReferenceEquals(pair.Value, entity)).Any(pair => !receipt.SpawnChildNativeIds.Contains(pair.Key)) ||
            live.Where(pair => ReferenceEquals(pair.Value, parent)).Any(pair => !receipt.SpawnParentNativeIds.Contains(pair.Key)))
            throw new InvalidOperationException("Parent has no ordered spawn metadata and the child has no exact decoded native spawn-header receipt.");
        if (own.Components is null || own.Components.Count == 0 || own.SyncEntityTypes is null || own.SyncEntityTypes.Count == 0)
            throw new InvalidOperationException("Actual spawned component/synchronization metadata is missing.");
    }
    public void Observe(IEnumerable<EntityRegistryData> entries)
    {
        if (!fresh) return;
        foreach (var item in entries)
        {
            if (fixedReincarnation?.Metadata.TryGetValue(item.EntityId, out var authorized) == true && !ExactMetadata(authorized, item))
                poisonedFixedReincarnations.Add(item.EntityId);
            removed.Remove(item.EntityId); // A new observed registration is not an old tombstone.
            if (!first.TryGetValue(item.EntityId, out var before)) first[item.EntityId] = item.DeepCopy();
            else if (before.Name != item.Name || before.Components is null || item.Components is null ||
                before.Components.Except(item.Components).Any()) changed.Add(item.EntityId);
            latest[item.EntityId] = item.DeepCopy();
        }
    }
    public JsonObject Report()
    {
        if (discoveryOnly) return new JsonObject
        {
            ["ok"] = false, ["layoutValid"] = false, ["freshLoadObserved"] = fresh,
            ["level"] = "story11", ["discoveryOnly"] = true,
            ["expectedFixedEntities"] = 0, ["checkedFixedEntities"] = 0,
            ["observedRegistryCount"] = latest.Count, ["observedRegistrySha256"] = RegistryHash(),
            ["errors"] = new JsonArray(new JsonObject { ["id"] = 0,
                ["reason"] = "Story 1-1 registry, roles and navigation geometry have not yet been independently mapped from native observations." }),
            ["nativeQualification"] = false,
            ["limitations"] = "Inspection bootstrap only; no assumed Carnival IDs, prefab roles or walkable geometry. Input, actions, checkpoint and warp commands are unavailable."
        };
        var errors = new JsonArray(); var semantic = new JsonArray(); var spawn = new JsonArray(); var substitutions = new JsonArray();
        var startupPoses = new JsonArray(); var chefPhases = new JsonArray();
        var expected = reference["entities"]!.AsArray();
        var fixedById = setup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]);
        int checkedCount = 0; double maximumDistance = 0;
        void Error(int id, string reason) => errors.Add(new JsonObject { ["id"] = id, ["reason"] = reason });
        if (!fresh) Error(0, "No actual fresh level-load message observed.");
        foreach (var item in expected)
        {
            int id = item!["id"]!.GetValue<int>();
            if (!fixedById.TryGetValue(id, out var entity)) { Error(id, "Expected fixed setup path missing."); continue; }
            if (!first.TryGetValue(id, out var initial)) { Error(id, "Initial native registration missing."); continue; }
            var actual = latest[id];
            bool authorizedReincarnation = AuthorizedFixedReincarnation(id);
            if (changed.Contains(id) && !authorizedReincarnation) Error(id, "Fixed native ID registered again with a changed name/component identity.");
            if (authorizedReincarnation && poisonedFixedReincarnations.Contains(id)) Error(id, "Authorized fixed reincarnation metadata was subsequently mutated.");
            var referenceActual = authorizedReincarnation ? initial : actual;
            if (referenceActual.Name != item["name"]!.ToString()) Error(id, "Native name differs from captured installation reference.");
            if (initial.Pos is null || !double.IsFinite(initial.Pos.X) || !double.IsFinite(initial.Pos.Y) || !double.IsFinite(initial.Pos.Z)) Error(id, "Missing or nonfinite registration position.");
            else
            {
                var p = declaredRegistrationPositions[id];
                bool phaseSpecificChef = settledChefReference.ContainsKey(id);
                double distance = Math.Sqrt(Math.Pow(initial.Pos.X - p.X, 2) + (phaseSpecificChef ? 0 : Math.Pow(initial.Pos.Y - p.Y, 2)) + Math.Pow(initial.Pos.Z - p.Z, 2));
                maximumDistance = Math.Max(maximumDistance, distance);
                if (distance > PositionTolerance) Error(id, phaseSpecificChef ? "Chef registration XZ differs from setup initial position." : "Registration position differs from setup initial position.");
                if (phaseSpecificChef) {
                    var expectedSettled = settledChefReference[id];
                    bool observed = settledChefs.TryGetValue(id, out var receipt);
                    double settledDistance = observed && receipt.Position is not null ? Math.Sqrt(Math.Pow(receipt.Position.X - expectedSettled.X, 2) + Math.Pow(receipt.Position.Y - expectedSettled.Y, 2) + Math.Pow(receipt.Position.Z - expectedSettled.Z, 2)) : double.PositiveInfinity;
                    if (!double.IsFinite(settledDistance) || settledDistance > PositionTolerance) Error(id, "Chef settled startup native position missing, nonfinite or differs from captured post-kitchen reference.");
                    chefPhases.Add(new JsonObject { ["id"] = id,
                        ["registrationPosition"] = JsonSerializer.SerializeToNode(initial.Pos, RuntimeHost.Json),
                        ["registrationYDelta"] = initial.Pos.Y - p.Y, ["registrationXZDelta"] = distance,
                        ["expectedSettledPosition"] = JsonSerializer.SerializeToNode(expectedSettled, RuntimeHost.Json),
                        ["observedSettledPosition"] = observed ? JsonSerializer.SerializeToNode(receipt.Position, RuntimeHost.Json) : null,
                        ["settledPositionDelta"] = double.IsFinite(settledDistance) ? settledDistance : null, ["source"] = observed ? receipt.Source : null });
                }
            }
            var settled = entity.position[0];
            var declared = declaredRegistrationPositions[id];
            if (Vector3.Distance(settled, declared) > PositionTolerance)
                startupPoses.Add(new JsonObject { ["id"] = id,
                    ["declaredRegistrationPosition"] = JsonSerializer.SerializeToNode(new { x = declared.X, y = declared.Y, z = declared.Z }),
                    ["reconstructedFrameZeroPosition"] = JsonSerializer.SerializeToNode(new { x = settled.X, y = settled.Y, z = settled.Z }),
                    ["attachmentParentPath"] = entity.data[0].attachmentParent is null ? null : JsonSerializer.SerializeToNode(entity.data[0].attachmentParent.path.ids),
                    ["attachmentPath"] = entity.data[0].attachment is null ? null : JsonSerializer.SerializeToNode(entity.data[0].attachment.path.ids),
                    ["observedComponents"] = actual.Components is null ? null : JsonSerializer.SerializeToNode(actual.Components),
                    ["scope"] = "Native startup reconstruction retained separately from the earlier registration pose; no world state is changed by this report." });
            if (referenceActual.Components is null) Error(id, "Native component observation missing.");
            else
            {
                var counts = referenceActual.Components.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
                foreach (var group in item["requiredComponents"]!.AsArray().Select(c => c!.ToString()).GroupBy(c => c))
                    if (counts.GetValueOrDefault(group.Key) < group.Count())
                    {
                        if (id is 84 or 85 && CannonReplacements.Contains(group.Key) && counts.GetValueOrDefault(group.Key + "Mod") >= group.Count())
                            substitutions.Add(new JsonObject { ["id"] = id, ["original"] = group.Key, ["observed"] = group.Key + "Mod", ["source"] = "framework/patch/AlteredComponents/" + group.Key + "Mod.cs" });
                        else Error(id, "Missing required observed component: " + group.Key);
                    }
                if (referenceActual.Components.Contains("CampaignFlowController") && !entity.prefab.IsKitchenFlowController)
                    semantic.Add(new JsonObject { ["id"] = id, ["reason"] = "Native CampaignFlowController is not annotated IsKitchenFlowController; round timer/order/score warp coverage is unverified." });
                if (reference["scene"]?.ToString() == "s_sushi_1_1" && !entity.prefab.Ignore) {
                    foreach (var flag in new[] {
                        ("AttachStation", entity.prefab.IsAttachStation), ("Workstation", entity.prefab.IsBoard),
                        ("PlateReturnStation", entity.prefab.IsPlateReturnStation), ("CampaignFlowController", entity.prefab.IsKitchenFlowController),
                        ("IngredientContainer", entity.prefab.CanContainIngredients), ("PhysicalAttachment", entity.prefab.CanBeAttached) })
                        if (referenceActual.Components.Contains(flag.Item1) != flag.Item2) Error(id, "Native " + flag.Item1 + " component and mapped warp annotation disagree.");
                }
            }
            if (referenceActual.SyncEntityTypes is null || referenceActual.SyncEntityTypes.Count == 0) Error(id, "Native synchronization type list missing.");
            if (entity.prefab.Spawns.Count > 0)
            {
                string primary = item["primarySpawnName"]?.ToString();
                string status = referenceActual.SpawnNames is null ? "unavailable-at-registration" : primary is null ? "requires-explicit-ordered-mapping" : referenceActual.SpawnNames.Count > 0 && referenceActual.SpawnNames[0] == primary ? "primary-name-matches" : "primary-name-mismatch";
                spawn.Add(new JsonObject { ["id"] = id, ["status"] = status, ["expectedPrimaryName"] = primary,
                    ["observedNames"] = referenceActual.SpawnNames is null ? null : new JsonArray(referenceActual.SpawnNames.Select(n => (JsonNode)JsonValue.Create(n)).ToArray()) });
                if (status == "primary-name-mismatch") Error(id, "Observed first spawn conflicts with declared first prefab.");
            }
            checkedCount++;
        }
        var extra = first.Keys.Except(fixedById.Keys).Order().ToArray();
        foreach (int id in extra)
            if (first[id].Components?.Any(c => c.EndsWith("Collider", StringComparison.Ordinal)) == true)
                Error(id, "Unmodeled registered object has a physical collider; layout needs review.");
        return new JsonObject
        {
            ["ok"] = errors.Count == 0, ["layoutValid"] = errors.Count == 0, ["freshLoadObserved"] = fresh,
            ["expectedFixedEntities"] = expected.Count, ["checkedFixedEntities"] = checkedCount,
            ["positionTolerance"] = PositionTolerance, ["maximumInitialPositionDelta"] = maximumDistance,
            ["referenceSource"] = reference["source"]!.ToString(), ["referenceSourceSha256"] = reference["sourceSha256"]!.ToString(),
            ["observedRegistrySha256"] = RegistryHash(), ["errors"] = errors, ["semanticGaps"] = semantic, ["spawnMappings"] = spawn,
            ["registrationReferencePolicy"] = "Immutable supplied setup registration poses captured before native reconstruction; never read back from mutable frame-zero physics history.",
            ["startupPoseDifferences"] = startupPoses,
            ["fixedReincarnation"] = FixedReincarnationReport(),
            ["chefStartupPhases"] = chefPhases,
            ["chefPositionPolicy"] = settledChefReference.Count == 0 ? null : "Story11 chefs: finite initial registration XYZ retained; registration XZ and separately observed settled startup XYZ use the unchanged tolerance. Initial registration Y is reported, not used as a settled-floor measurement. Transient post-InLevel poses are ignored; the first matching startup receipt remains immutable after movement/warp.",
            ["metadataPolicy"] = "First position retained; latest name/components/spawn lists checked. Additive native metadata refresh does not replace initial pose.",
            ["frameworkComponentSubstitutions"] = substitutions,
            ["nativeQualification"] = false,
            ["limitations"] = "Validates initial fixed names, component requirements and positions. Collider shapes, attachment links, timings, RNG, score, and absent spawn collections are not validated by this registry. Explicit development warp remains an experiment."
        };
    }
    private string RegistryHash() => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { initial = first.Values.OrderBy(e => e.EntityId), latest = latest.Values.OrderBy(e => e.EntityId) }, RuntimeHost.Json)));
    public JsonNode InitialRegistrations() => JsonSerializer.SerializeToNode(first.Values.OrderBy(e => e.EntityId), RuntimeHost.Json);
    public void RequireLayout()
    {
        var report = Report();
        if (report["layoutValid"]!.GetValue<bool>() != true) throw new InvalidOperationException("Initial registry validation failed; inspect registryValidation.errors. " + report["errors"]!.ToJsonString());
    }
    public JsonObject RequireGraphMappings(IReadOnlyDictionary<int, GameEntityRecord> live, int frame)
    {
        var report = Report(); var fixedIds = setup.entityRecords.FixedEntities.Select(e => e.path.ids[0]).ToHashSet();
        var errors = report["errors"]!.AsArray().Where(e => fixedIds.Contains(e!["id"]!.GetValue<int>()) || e!["id"]!.GetValue<int>() == 0).Select(e => e!.DeepClone()).ToList();
        var validated = new JsonArray();
        foreach (int id in latest.Keys.Union(live.Keys).Where(id => !fixedIds.Contains(id)))
        {
            if (!latest.TryGetValue(id, out var observation)) { errors.Add(new JsonObject { ["id"] = id, ["reason"] = "Live spawned ID has no actual registry metadata." }); continue; }
            // Removed IDs remain in the registry receipt. They are not current
            // collision geometry and cannot be targets of a new action.
            if (!live.TryGetValue(id, out var entity))
            {
                if (!removed.Contains(id) && !ValidPhysicalContainer(id, live, frame)) errors.Add(new JsonObject { ["id"] = id, ["reason"] = "Extra registered ID has neither a live mapped path, an exact live native physical-container association, nor an actual removal receipt." });
                continue;
            }
            string error = null;
            if (!entity.existed[frame] || entity.path.ids.Length < 2 || entity.spawner is null) error = "Live extra ID lacks an observed spawned record/path.";
            else
            {
                var parent = entity.spawner; int slot = entity.path.ids[^1];
                if (!entity.path.ids.Take(entity.path.ids.Length - 1).SequenceEqual(parent.path.ids) || slot < 0 || slot >= parent.spawned.Count || !ReferenceEquals(parent.spawned[slot], entity))
                    error = "Spawned ordinal/path does not identify its actual reconstructed parent slot.";
                try
                {
                    ValidateNativeChild(entity, parent, live);
                }
                catch (InvalidOperationException invalid) { error = invalid.Message; }
            }
            if (error is not null) errors.Add(new JsonObject { ["id"] = id, ["reason"] = error });
            else validated.Add(new JsonObject { ["id"] = id, ["path"] = JsonSerializer.SerializeToNode(entity.path.ids), ["prefab"] = entity.prefab.Name,
                ["mappingSource"] = MetadataFor(entity.spawner, live).SpawnNames is null ? "decoded-native-spawn-header" : "ordered-parent-registry-spawn-names" });
        }
        if (errors.Count != 0) throw new InvalidOperationException("Graph registry/path validation failed: " + new JsonArray(errors.ToArray()).ToJsonString());
        return new JsonObject { ["ok"] = true, ["frame"] = frame, ["fixedMappingsValidated"] = true, ["spawnedMappings"] = validated, ["registrySha256"] = RegistryHash(),
            ["observedPhysicalContainers"] = JsonSerializer.SerializeToNode(physicalContainers.Where(p => ValidPhysicalContainer(p.Key, live, frame)).Select(p => new { nativeId = p.Key, logicalPath = p.Value.path.ids })),
            ["removedNativeIds"] = JsonSerializer.SerializeToNode(removed.Where(id => !live.ContainsKey(id) && !fixedIds.Contains(id)).Order()),
            ["geometryScope"] = "Observed fixed layout and exact spawn identities/ordered prefab lists; dynamic collider shapes and collision paths remain the original framework model, not independent native geometry proof." };
    }
    public void RequireFixedWarpPaths(int from, int to)
    {
        RequireLayout();
        if (setup.entityRecords.GenAllEntities().Any(e => e.path.ids.Length != 1 && (e.existed[from] || e.existed[to])))
            throw new InvalidOperationException("Development warp includes spawned paths whose ordered native spawn mapping has not been validated.");
    }
    public JsonObject RequireWarpPaths(IReadOnlyDictionary<int, GameEntityRecord> live, int from, int to, NativeWarpCapabilities capabilities)
    {
        var current = RequireGraphMappings(live, from);
        var dynamic = setup.entityRecords.GenAllEntities().Where(e => e.path.ids.Length != 1 && (e.existed[from] || e.existed[to])).ToArray();
        if (dynamic.Length != 0 && (capabilities is null || capabilities.Version != 1 || (capabilities.Features & 1) == 0))
            throw new InvalidOperationException("Current native output does not advertise version1 dynamic-warp preflight support.");
        var paths = new HashSet<string>(); var validated = new JsonArray();
        foreach (var entity in dynamic)
        {
            string key = string.Join(".", entity.path.ids);
            if (!paths.Add(key)) throw new InvalidOperationException("Duplicate live/historical dynamic logical path: " + key);
            var chain = new List<GameEntityRecord>(); var seen = new HashSet<GameEntityRecord>();
            for (var node = entity; node is not null; node = node.spawner)
            {
                if (!seen.Add(node)) throw new InvalidOperationException("Cyclic observed spawn parent chain.");
                chain.Add(node);
                if (node.path.ids.Length == 1) break;
                var parent = node.spawner ?? throw new InvalidOperationException("Spawned path lacks its original parent record.");
                int slot = node.path.ids[^1];
                if (!node.path.ids.Take(node.path.ids.Length - 1).SequenceEqual(parent.path.ids) || slot < 0 || slot >= parent.spawned.Count || !ReferenceEquals(parent.spawned[slot], node))
                    throw new InvalidOperationException("Historical spawn ordinal does not identify the observed parent record.");
                ValidateNativeChild(node, parent, live);
            }
            var root = chain[^1];
            if (root.path.ids.Length != 1 || !root.existed[from] || !root.existed[to] || !live.TryGetValue(root.path.ids[0], out var mappedRoot) || !ReferenceEquals(root, mappedRoot))
                throw new InvalidOperationException("The original fixed spawn root must remain observed/live at source and target.");
            if (entity.existed[to] && !live.Values.Any(e => ReferenceEquals(e, entity)))
            {
                var spawning = entity.prefab.SpawningPath;
                var forward = chain.AsEnumerable().Reverse().ToArray();
                var selectedIndices = forward.Skip(1).Select((child, index) => forward[index].prefab.Spawns.IndexOf(child.prefab)).ToArray();
                if (spawning is null || spawning.InitialFixedEntityId != root.path.ids[0] || !spawning.SpawnableIds.SequenceEqual(selectedIndices))
                    throw new InvalidOperationException("WarpCalculator recreation path differs from the exact observed native prefab chain.");
            }
            if (!mappedReceipts.TryGetValue(entity, out var receipt)) throw new InvalidOperationException("Dynamic target has no retained actual native mapping receipt.");
            validated.Add(new JsonObject { ["path"] = JsonSerializer.SerializeToNode(entity.path.ids), ["prefab"] = entity.prefab.Name,
                ["existedAtSource"] = entity.existed[from], ["existedAtTarget"] = entity.existed[to], ["observedNativeIds"] = JsonSerializer.SerializeToNode(receipt.NativeIds.Order().ToArray()),
                ["lastObservedNativeId"] = receipt.LastNativeId, ["firstObservedFrame"] = receipt.FirstFrame,
                ["nativeName"] = receipt.Metadata.Name,
                ["spawnChain"] = JsonSerializer.SerializeToNode(chain.AsEnumerable().Reverse().Select(n => new { path = n.path.ids, frameworkLabel = n.prefab.Name, nativeName = MetadataFor(n, live).Name })) });
        }
        return new() { ["fixedMappingsValidated"] = current["fixedMappingsValidated"]!.DeepClone(), ["fromFrame"] = from, ["targetFrame"] = to,
            ["observedPhysicalContainers"] = current["observedPhysicalContainers"]!.DeepClone(),
            ["nativeCapabilities"] = capabilities is null ? null : JsonSerializer.SerializeToNode(capabilities, RuntimeHost.Json), ["dynamicMappings"] = validated,
            ["registrySha256"] = RegistryHash(), ["scope"] = "Observed logical paths and exact historical native prefab metadata only. Native plugin preflight must verify current incarnations, references, component blocks and checkpoint availability before mutation. Native IDs/proxies may change; replay parity is independently measured." };
    }
    public static CarnivalRegistryAudit FromInspection(GameSetup setup, JsonObject inspection)
    {
        var audit = new CarnivalRegistryAudit(setup);
        if (inspection["freshLevelLoadObserved"]?.GetValue<bool>() == true) audit.Reset();
        var registry = inspection["registry"]?.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json) ?? throw new InvalidDataException("Full actual registry required.");
        if (inspection["initialRegistry"] is JsonArray initial) audit.Observe(initial.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!);
        audit.Observe(registry); audit.ObserveSettledChefInspection(inspection); return audit;
    }
    public GameSetup ImportInitialPositions()
    {
        RequireLayout();
        foreach (var entity in setup.entityRecords.FixedEntities)
        {
            var actual = first[entity.path.ids[0]].Pos;
            entity.position = new Versioned<Vector3>(new((float)actual.X, (float)actual.Y, (float)actual.Z));
        }
        return setup;
    }
}
