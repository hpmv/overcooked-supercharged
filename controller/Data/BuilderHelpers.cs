namespace Hpmv {
    public static class BuilderHelpers {
        public static GameActionGraphBuilder GetFromCrateAndChop(this GameActionGraphBuilder chef, GameEntityRecord crate, GameEntityRecord board, GameActionGraphBuilder aka) {
            return chef.GetFromCrate(crate).Aka(aka).PutOnto(board).DoWork(board).WaitForSpawn(aka).Aka(aka);
        }
    }
}