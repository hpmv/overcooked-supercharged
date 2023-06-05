using Hpmv;

namespace SuperchargedPatch
{
    public enum TASLogicalButtonType
    {
        Pickup,
        Use,
        Dash,
    }
    public enum TASLogicalValueType
    {
        MovementX,
        MovementY,
    }
    public class TASLogicalButton : ILogicalButton
    {
        private int playerEntityId;
        private TASLogicalButtonType type;
        private ILogicalButton original;

        public TASLogicalButton(int playerEntityId, TASLogicalButtonType type, ILogicalButton original)
        {
            this.playerEntityId = playerEntityId;
            this.type = type;
            this.original = original;
        }

        public void ClaimPressEvent()
        {
        }

        public void ClaimReleaseEvent()
        {
        }

        public float GetHeldTimeLength()
        {
            return 0;
        }

        public void GetLogicTreeData(out AcyclicGraph<ILogicalElement, LogicalLinkInfo> _graph, out AcyclicGraph<ILogicalElement, LogicalLinkInfo>.Node _head)
        {
            original.GetLogicTreeData(out _graph, out _head);
        }

        public bool HasUnclaimedPressEvent()
        {
            return false;
        }

        public bool HasUnclaimedReleaseEvent()
        {
            return false;
        }

        public bool IsDown()
        {
            var input = Injector.Server.CurrentInput;
            if (input.Input?.ContainsKey(playerEntityId) ?? false)
            {
                var inputData = input.Input[playerEntityId];
                switch (type)
                {
                    case TASLogicalButtonType.Pickup:
                        return inputData.Pickup.Down;
                    case TASLogicalButtonType.Use:
                        return inputData.Interact.Down;
                    case TASLogicalButtonType.Dash:
                        return inputData.Dash.Down;
                }
            }
            return original.IsDown();
        }

        public bool JustPressed()
        {
            var input = Injector.Server.CurrentInput;
            if (input.Input?.ContainsKey(playerEntityId) ?? false)
            {
                var inputData = input.Input[playerEntityId];
                switch (type)
                {
                    case TASLogicalButtonType.Pickup:
                        return inputData.Pickup.JustPressed;
                    case TASLogicalButtonType.Use:
                        return inputData.Interact.JustPressed;
                    case TASLogicalButtonType.Dash:
                        return inputData.Dash.JustPressed;
                }
            }
            return original.JustPressed();
        }

        public bool JustReleased()
        {
            var input = Injector.Server.CurrentInput;
            if (input.Input?.ContainsKey(playerEntityId) ?? false)
            {
                var inputData = input.Input[playerEntityId];
                switch (type)
                {
                    case TASLogicalButtonType.Pickup:
                        return inputData.Pickup.JustReleased;
                    case TASLogicalButtonType.Use:
                        return inputData.Interact.JustReleased;
                    case TASLogicalButtonType.Dash:
                        return inputData.Dash.JustReleased;
                }
            }
            return original.JustReleased();
        }
    }

    public class TASLogicalValue : ILogicalValue
    {
        private int playerEntityId;
        private TASLogicalValueType type;
        private ILogicalValue original;

        public TASLogicalValue(int playerEntityId, TASLogicalValueType type, ILogicalValue original)
        {
            this.playerEntityId = playerEntityId;
            this.type = type;
            this.original = original;
        }

        public void GetLogicTreeData(out AcyclicGraph<ILogicalElement, LogicalLinkInfo> _graph, out AcyclicGraph<ILogicalElement, LogicalLinkInfo>.Node _head)
        {
            original.GetLogicTreeData(out _graph, out _head);
        }

        public float GetValue()
        {
            var input = Injector.Server.CurrentInput;
            if (input.Input?.ContainsKey(playerEntityId) ?? false)
            {
                var inputData = input.Input[playerEntityId];
                switch (type)
                {
                    case TASLogicalValueType.MovementX:
                        return (float)inputData.Pad.X;
                    case TASLogicalValueType.MovementY:
                        return (float)inputData.Pad.Y;
                }
            }
            return original.GetValue();
        }
    }
}
