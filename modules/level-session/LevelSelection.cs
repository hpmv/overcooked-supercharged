using System;
using System.Collections.Generic;

namespace SuperchargedPatch.Authoring.Modules
{
    // Pure selection policy; native adapters supply actual directory observations.
    public sealed class LevelCandidate
    {
        public int Index, Players;
        public string Label, World, Theme, Scene, Config;
        public bool Allowed, Hidden;
    }
    public static class LevelSelection
    {
        public const string Scene = "s_sushi_1_1";
        public static LevelCandidate MainOneOne(IEnumerable<LevelCandidate> observed)
        {
            if (observed == null) throw new ArgumentNullException("observed");
            LevelCandidate selected = null;
            foreach (var row in observed)
            {
                if (row == null || row.Label != "Text.Menu.Level01" || row.World != "One" || row.Theme != "Sushi") continue;
                if (selected != null) throw new InvalidOperationException("Ambiguous native main 1-1 directory entries.");
                if (row.Index < 0 || row.Players != 4 || !row.Allowed || row.Hidden ||
                    !String.Equals(row.Scene, Scene, StringComparison.OrdinalIgnoreCase) || String.IsNullOrEmpty(row.Config))
                    throw new InvalidOperationException("Native main 1-1 lacks its expected allowed four-player scene/config.");
                selected = row;
            }
            if (selected == null) throw new InvalidOperationException("Native directory has no unique main 1-1 entry.");
            return selected;
        }
    }
}
