namespace Hpmv {
    public class WaitForWashingProgressAction : GameAction {
        public IEntityReference Entity { get; set; }
        public double WashingProgress { get; set; }

        public override string Describe() {
            return $"Wait {Entity} washing progress {WashingProgress} ";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Entity.GetEntityRecord(input);
            if (entity != null) {
                if (entity.washingProgress[input.Frame] >= WashingProgress) {
                    return new GameActionOutput {
                        Done = true,
                    };
                }
            }
            return default;
        }

        public new Save.WaitForWashingProgressAction ToProto() {
            return new Save.WaitForWashingProgressAction {
                Entity = Entity.ToProto(),
                WashingProgress = WashingProgress
            };
        }
    }

    public static class WaitForWashingProgressActionFromProto {
        public static WaitForWashingProgressAction FromProto(this Save.WaitForWashingProgressAction action, LoadContext context) {
            return new WaitForWashingProgressAction {
                Entity = action.Entity.FromProto(context),
                WashingProgress = action.WashingProgress
            };
        }
    }

    public class WaitForChoppingProgressAction : GameAction {
        public IEntityReference Entity { get; set; }
        public double ChoppingProgress { get; set; }

        public override string Describe() {
            return $"Wait {Entity} chopping progress {ChoppingProgress} ";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Entity.GetEntityRecord(input);
            if (entity != null) {
                if (entity.choppingProgress[input.Frame] >= ChoppingProgress) {
                    return new GameActionOutput {
                        Done = true,
                    };
                }
            }
            return default;
        }

        public new Save.WaitForChoppingProgressAction ToProto() {
            return new Save.WaitForChoppingProgressAction {
                Entity = Entity.ToProto(),
                ChoppingProgress = ChoppingProgress
            };
        }
    }

    public static class WaitForChoppingProgressActionFromProto {
        public static WaitForChoppingProgressAction FromProto(this Save.WaitForChoppingProgressAction action, LoadContext context) {
            return new WaitForChoppingProgressAction {
                Entity = action.Entity.FromProto(context),
                ChoppingProgress = action.ChoppingProgress
            };
        }
    }

    public class WaitForCookingProgressAction : GameAction {
        public IEntityReference Entity { get; set; }
        public double CookingProgress { get; set; }

        public override string Describe() {
            return $"Wait {Entity} cooking progress {CookingProgress} ";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Entity.GetEntityRecord(input);
            if (entity != null) {
                if (entity.cookingProgress[input.Frame] >= CookingProgress) {
                    return new GameActionOutput {
                        Done = true,
                    };
                }
            }
            return default;
        }

        public new Save.WaitForCookingProgressAction ToProto() {
            return new Save.WaitForCookingProgressAction {
                Entity = Entity.ToProto(),
                CookingProgress = CookingProgress
            };
        }
    }

    public static class WaitForCookingProgressActionFromProto {
        public static WaitForCookingProgressAction FromProto(this Save.WaitForCookingProgressAction action, LoadContext context) {
            return new WaitForCookingProgressAction {
                Entity = action.Entity.FromProto(context),
                CookingProgress = action.CookingProgress
            };
        }
    }

    public class WaitForMixingProgressAction : GameAction {
        public IEntityReference Entity { get; set; }
        public double MixingProgress { get; set; }

        public override string Describe() {
            return $"Wait {Entity} mixing progress {MixingProgress} ";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Entity.GetEntityRecord(input);
            if (entity != null) {
                if (entity.mixingProgress[input.Frame] >= MixingProgress) {
                    return new GameActionOutput {
                        Done = true,
                    };
                }
            }
            return default;
        }

        public new Save.WaitForMixingProgressAction ToProto() {
            return new Save.WaitForMixingProgressAction {
                Entity = Entity.ToProto(),
                MixingProgress = MixingProgress
            };
        }
    }

    public static class WaitForMixingProgressActionFromProto {
        public static WaitForMixingProgressAction FromProto(this Save.WaitForMixingProgressAction action, LoadContext context) {
            return new WaitForMixingProgressAction {
                Entity = action.Entity.FromProto(context),
                MixingProgress = action.MixingProgress
            };
        }
    }
}