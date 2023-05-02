using System.Collections.Generic;
using System.Numerics;
using ClipperLib;
using Hpmv;

namespace Hpmv {
    public static class SaveUtils {
        public static Save.Vector2 ToProto(this Vector2 v) {
            return new Save.Vector2 {
                X = v.X,
                Y = v.Y
            };
        }

        public static Vector2 FromProto(this Save.Vector2 v) {
            return new Vector2(v.X, v.Y);
        }

        public static Save.Vector3 ToProto(this Vector3 v) {
            return new Save.Vector3 {
                X = v.X,
                Y = v.Y,
                Z = v.Z,
            };
        }

        public static Vector3 FromProto(this Save.Vector3 v) {
            return new Vector3(v.X, v.Y, v.Z);
        }

        public static Save.Quaternion ToProto(this System.Numerics.Quaternion q) {
            return new Save.Quaternion {
                X = q.X,
                Y = q.Y,
                Z = q.Z,
                W = q.W,
            };
        }

        public static System.Numerics.Quaternion FromProto(this Save.Quaternion q) {
            return new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W);
        }

        public static Save.IntPoint ToProto(this IntPoint p) {
            return new Save.IntPoint {
                X = p.X,
                Y = p.Y
            };
        }

        public static IntPoint FromProto(this Save.IntPoint p) {
            return new IntPoint(p.X, p.Y);
        }

        public static Save.VersionedInt ToProto(this Versioned<int> ver) {
            var result = new Save.VersionedInt { InitialValue = ver.initialValue };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value);
            }
            return result;
        }

        public static Versioned<int> FromProto(this Save.VersionedInt ver) {
            var result = new Versioned<int>(ver.InitialValue);
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i], ver.Frame[i]);
            }
            return result;
        }

        public static Save.VersionedDouble ToProto(this Versioned<double> ver) {
            var result = new Save.VersionedDouble { InitialValue = ver.initialValue };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value);
            }
            return result;
        }

        public static Versioned<double> FromProto(this Save.VersionedDouble ver) {
            var result = new Versioned<double>(ver.InitialValue);
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i], ver.Frame[i]);
            }
            return result;
        }

        public static Save.VersionedBool ToProto(this Versioned<bool> ver) {
            var result = new Save.VersionedBool { InitialValue = ver.initialValue };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value);
            }
            return result;
        }

        public static Versioned<bool> FromProto(this Save.VersionedBool ver) {
            var result = new Versioned<bool>(ver.InitialValue);
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i], ver.Frame[i]);
            }
            return result;
        }


        public static Save.VersionedVector3 ToProto(this Versioned<Vector3> ver) {
            var result = new Save.VersionedVector3 { InitialValue = ver.initialValue.ToProto() };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value.ToProto());
            }
            return result;
        }

        public static Versioned<Vector3> FromProto(this Save.VersionedVector3 ver) {
            var result = new Versioned<Vector3>(ver.InitialValue.FromProto());
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i].FromProto(), ver.Frame[i]);
            }
            return result;
        }

        public static Save.VersionedQuaternion ToProto(this Versioned<System.Numerics.Quaternion> ver) {
            var result = new Save.VersionedQuaternion { InitialValue = ver.initialValue.ToProto() };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value.ToProto());
            }
            return result;
        }

        public static Versioned<System.Numerics.Quaternion> FromProto(this Save.VersionedQuaternion ver) {
            var result = new Versioned<System.Numerics.Quaternion>(ver.InitialValue.FromProto());
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i].FromProto(), ver.Frame[i]);
            }
            return result;
        }

        public static GameEntityRecord FromProtoRef(this Save.EntityPath path, LoadContext context) {
            if (path == null) {
                return null;
            }
            return context.PathToRecord[path.FromProto().ToString()];
        }



        public static Save.VersionedSpecificEntityData ToProto(this Versioned<SpecificEntityData> ver) {
            var result = new Save.VersionedSpecificEntityData { InitialValue = ver.initialValue.ToProto() };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value.ToProto());
            }
            return result;
        }

        public static Versioned<SpecificEntityData> FromProto(this Save.VersionedSpecificEntityData ver, LoadContext context) {
            var result = new Versioned<SpecificEntityData>(ver.InitialValue.FromProto(context));
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i].FromProto(context), ver.Frame[i]);
            }
            return result;
        }

        public static Save.VersionedChefState ToProto(this Versioned<ChefState> ver) {
            var result = new Save.VersionedChefState { InitialValue = ver.initialValue.ToProto() };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value.ToProto());
            }
            return result;
        }

        public static Versioned<ChefState> FromProto(this Save.VersionedChefState ver, LoadContext context) {
            var result = new Versioned<ChefState>(ver.InitialValue.FromProto(context));
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i].FromProto(context), ver.Frame[i]);
            }
            return result;
        }

        public static Save.VersionedActualControllerInput ToProto(this Versioned<ActualControllerInput> ver) {
            var result = new Save.VersionedActualControllerInput { InitialValue = ver.initialValue.ToProto() };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value.ToProto());
            }
            return result;
        }

        public static Versioned<ActualControllerInput> FromProto(this Save.VersionedActualControllerInput ver) {
            var result = new Versioned<ActualControllerInput>(ver.InitialValue.FromProto());
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i].FromProto(), ver.Frame[i]);
            }
            return result;
        }


        public static Save.VersionedControllerState ToProto(this Versioned<ControllerState> ver) {
            var result = new Save.VersionedControllerState { InitialValue = ver.initialValue.ToProto() };
            foreach (var (frame, value) in ver.changes) {
                result.Frame.Add(frame);
                result.Data.Add(value.ToProto());
            }
            return result;
        }

        public static Versioned<ControllerState> FromProto(this Save.VersionedControllerState ver) {
            var result = new Versioned<ControllerState>(ver.InitialValue.FromProto());
            for (int i = 0; i < ver.Frame.Count; i++) {
                result.ChangeTo(ver.Data[i].FromProto(), ver.Frame[i]);
            }
            return result;
        }
    }

    public class LoadContext {
        public readonly Dictionary<string, GameEntityRecord> PathToRecord = new Dictionary<string, GameEntityRecord>();
        public readonly GameEntityRecords Records = new GameEntityRecords();

        public GameEntityRecord GetRootRecord(int id) {
            return PathToRecord[id.ToString()];
        }

        public void Load(Save.GameEntityRecords records) {
            foreach (var record in records.AllRecords) {
                var obj = new GameEntityRecord {
                    path = record.Path.FromProto(),
                    displayName = record.DisplayName,
                    className = record.ClassName,
                    prefab = record.Prefab.FromProto(),
                };
                PathToRecord[record.Path.FromProto().ToString()] = obj;
                if (obj.path.ids.Length == 1) {
                    Records.FixedEntities.Add(obj);
                }
            }

            foreach (var record in records.AllRecords) {
                PathToRecord[record.Path.FromProto().ToString()].ReadMutableDataFromProto(record, this);
            }
            foreach (var (chef, controllerState) in records.Controllers) {
                Records.Chefs[GetRootRecord(chef)] = controllerState.FromProto();
            }
        }
    }
}