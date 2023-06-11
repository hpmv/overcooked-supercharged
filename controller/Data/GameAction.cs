using System;
using System.Collections.Generic;

namespace Hpmv {

    public abstract class GameAction {
        public int ActionId { get; set; }
        public GameEntityRecord Chef { get; set; }
        public abstract string Describe();
        public abstract GameActionOutput Step(GameActionInput input);

        public override string ToString() {
            return $"Action#{ActionId}";
        }

        public Save.GameAction ToProto() {
            var result = new Save.GameAction();
            switch (this) {
                case CatchAction catchAction:
                    result.Catch = catchAction.ToProto();
                    break;
                case GotoAction gotoAction:
                    result.Goto = gotoAction.ToProto();
                    break;
                case InteractAction interactAction:
                    result.Interact = interactAction.ToProto();
                    break;
                case ThrowAction throwAction:
                    result.Throw = throwAction.ToProto();
                    break;
                case WaitAction waitAction:
                    result.Wait = waitAction.ToProto();
                    break;
                case WaitForCleanPlateAction waitForCleanPlateAction:
                    result.WaitForCleanPlate = waitForCleanPlateAction.ToProto();
                    break;
                case WaitForDirtyPlateAction waitForDirtyPlateAction:
                    result.WaitForDirtyPlate = waitForDirtyPlateAction.ToProto();
                    break;
                case WaitForWashingProgressAction waitForWashingProgressAction:
                    result.WaitForWashingProgress = waitForWashingProgressAction.ToProto();
                    break;
                case WaitForChoppingProgressAction waitForChoppingProgressAction:
                    result.WaitForChoppingProgress = waitForChoppingProgressAction.ToProto();
                    break;
                case WaitForCookingProgressAction waitForCookingProgressAction:
                    result.WaitForCookingProgress = waitForCookingProgressAction.ToProto();
                    break;
                case WaitForMixingProgressAction waitForMixingProgressAction:
                    result.WaitForMixingProgress = waitForMixingProgressAction.ToProto();
                    break;
                case WaitForSpawnAction waitForSpawnAction:
                    result.WaitForSpawn = waitForSpawnAction.ToProto();
                    break;
                default:
                    throw new Exception("Unexpected type");
            };
            return result;
        }
    }

    public static class GameActionFromProto {
        public static GameAction FromProto(this Save.GameAction action, LoadContext context) {
            switch (action.ActionCase) {
                case Save.GameAction.ActionOneofCase.Catch:
                    return action.Catch.FromProto();
                case Save.GameAction.ActionOneofCase.Goto:
                    return action.Goto.FromProto(context);
                case Save.GameAction.ActionOneofCase.Interact:
                    return action.Interact.FromProto(context);
                case Save.GameAction.ActionOneofCase.Throw:
                    return action.Throw.FromProto(context);
                case Save.GameAction.ActionOneofCase.Wait:
                    return action.Wait.FromProto();
                case Save.GameAction.ActionOneofCase.WaitForCleanPlate:
                    return action.WaitForCleanPlate.FromProto(context);
                case Save.GameAction.ActionOneofCase.WaitForDirtyPlate:
                    return action.WaitForDirtyPlate.FromProto(context);
                case Save.GameAction.ActionOneofCase.WaitForWashingProgress:
                    return action.WaitForWashingProgress.FromProto(context);
                case Save.GameAction.ActionOneofCase.WaitForChoppingProgress:
                    return action.WaitForChoppingProgress.FromProto(context);
                case Save.GameAction.ActionOneofCase.WaitForCookingProgress:
                    return action.WaitForCookingProgress.FromProto(context);
                case Save.GameAction.ActionOneofCase.WaitForMixingProgress:
                    return action.WaitForMixingProgress.FromProto(context);
                case Save.GameAction.ActionOneofCase.WaitForSpawn:
                    return action.WaitForSpawn.FromProto(context);
            }
            throw new Exception("Invalid GameAction proto");
        }
    }
}