using System;
using System.Linq;

namespace Hpmv
{
    public partial class InteractAction
    {
        // Bind through persisted action StartFrame and versioned native attachment
        // history, not mutable current contents or a non-serialized side cache.
        // Rewinding retains the original record identities at that start boundary.
        public sealed class TransferObservation
        {
            public int StartFrame;
            public GameEntityRecord Subject, Source, Item;
            public bool BindingValid, Accepted;
            public string Pending;
        }

        public TransferObservation ObserveTransfer(GameActionInput input)
        {
            if (!Primary || Prepare || (!IsPickup && ExpectSpawn))
                throw new InvalidOperationException("Observed transfer supports pickup and attachment placement only.");
            int start = input.Frame - input.FrameWithinAction;
            if (start < 0 || input.FrameWithinAction < 0) throw new InvalidOperationException("Invalid observed transfer start frame.");
            var beginning = input; beginning.Frame = start; beginning.FrameWithinAction = 0;
            var subject = Subject.GetEntityRecord(beginning);
            var result = new TransferObservation { StartFrame = start, Subject = subject };
            TransferObservation Pending(string reason) { result.Pending = reason; return result; }
            if (subject == null || !subject.existed[start] || !subject.existed[input.Frame] || !Chef.existed[input.Frame])
                return Pending("Pinned transfer subject/chef is not live.");
            var currentHeld = Chef.data[input.Frame].attachment;
            if (IsPickup)
            {
                if (Chef.data[start].attachment != null) return Pending("Pickup did not start empty-handed.");
                if (ExpectSpawn)
                {
                    result.Source = subject;
                    result.BindingValid = subject.prefab.Spawns.Count > 0;
                    if (!result.BindingValid) return Pending("Pickup producer has no declared native child index space.");
                    var children = subject.spawned.Where(c => c.existed[input.Frame] && !c.existed[start]).ToArray();
                    if (children.Length != 1) return Pending("Waiting for one exact new native child; no duplicate spawn is accepted.");
                    result.Item = children[0];
                    if (result.Item.spawner != subject || (result.Item.spawnOwner[input.Frame] != -1 && result.Item.spawnOwner[input.Frame] != ActionId))
                        return Pending("New native child belongs to another producer/claim.");
                }
                else
                {
                    result.Item = subject.prefab.IsAttachStation ? subject.data[start].attachment : subject;
                    if (result.Item == null || !result.Item.existed[start] || !result.Item.prefab.CanBeAttached)
                        return Pending("No exact attachable pickup item at the start boundary.");
                    result.Source = result.Item.data[start].attachmentParent;
                    if ((subject.prefab.IsAttachStation && result.Source != subject) ||
                        (result.Source != null && (result.Source.prefab.IsChef || result.Source.data[start].attachment != result.Item)))
                        return Pending("Pickup source does not have the exact original attachment pair.");
                    result.BindingValid = true;
                }
                if (!result.Item.existed[input.Frame]) return Pending("Pinned pickup item no longer exists.");
                result.Accepted = currentHeld == result.Item && result.Item.data[input.Frame].attachmentParent == Chef &&
                    (result.Source == null || (result.Source.existed[input.Frame] && result.Source.data[input.Frame].attachment != result.Item));
                return result.Accepted ? result : Pending("Waiting for exact item-to-chef attachment and source release.");
            }
            result.Source = Chef; result.Item = Chef.data[start].attachment;
            if (!subject.prefab.IsAttachStation || subject.data[start].attachment != null || result.Item == null ||
                !result.Item.existed[start] || result.Item.data[start].attachmentParent != Chef)
                return Pending("Placement requires an empty attach station and the exact originally held item.");
            result.BindingValid = true;
            if (!result.Item.existed[input.Frame]) return Pending("Pinned placed item was consumed; attachment placement cannot certify a combine.");
            result.Accepted = currentHeld == null && subject.data[input.Frame].attachment == result.Item && result.Item.data[input.Frame].attachmentParent == subject;
            return result.Accepted ? result : Pending("Waiting for exact item-to-target attachment and chef release.");
        }

        private GameActionOutput StepObservedTransfer(GameActionInput input)
        {
            var observed = ObserveTransfer(input);
            // Release only through the existing controller duration gate. An up
            // edge is never the effect proof and cannot unblock a dependency.
            if (input.ControllerState.PrimaryButtonDown)
                return input.ControllerState.RequestButtonUp() ? new GameActionOutput { ControllerInput = new DesiredControllerInput { primaryUp = true } } : default;
            if (observed.Accepted)
                return new GameActionOutput { Done = true, ControllerInput = new DesiredControllerInput(),
                    SpawningClaim = ExpectSpawn ? observed.Item : null };
            if (!observed.BindingValid || input.ControllerState.SecondaryButtonDown || observed.Subject == null || !observed.Subject.existed[input.Frame]) return default;
            GameEntityRecord target;
            if (IsPickup)
            {
                if (Chef.data[input.Frame].attachment != null || Chef.data[observed.StartFrame].attachment != null) return default;
                if (ExpectSpawn)
                {
                    // Once a real child exists, wait for its observed acceptance.
                    // Repeated crate presses must not manufacture extra candidates.
                    if (observed.Subject.spawned.Any(c => c.existed[input.Frame] && !c.existed[observed.StartFrame])) return default;
                    target = observed.Subject;
                }
                else
                {
                    var item = observed.Item; var source = observed.Source;
                    if (item == null || !item.existed[input.Frame] || item.data[input.Frame].attachmentParent != source ||
                        (source != null && (!source.existed[input.Frame] || source.data[input.Frame].attachment != item || source.prefab.IsChef))) return default;
                    target = source ?? item;
                }
            }
            else
            {
                if (observed.Item == null || !observed.Item.existed[input.Frame] || !observed.Subject.prefab.IsAttachStation ||
                    observed.Subject.data[observed.StartFrame].attachment != null || observed.Subject.data[input.Frame].attachment != null ||
                    Chef.data[input.Frame].attachment != observed.Item || observed.Item.data[input.Frame].attachmentParent != Chef) return default;
                target = observed.Subject;
            }
            var chef = Chef.chefState[input.Frame];
            bool highlighted = (IsPickup ? chef.highlightedForPickup : chef.highlightedForPlacement) == target;
            if (highlighted)
            {
                bool allowed = IsPickup ? input.ControllerState.RequestButtonDownForPickup() : input.ControllerState.RequestButtonDown();
                return allowed ? new GameActionOutput { ControllerInput = new DesiredControllerInput { primaryDown = true, primaryDownIsForPickup = IsPickup } } : default;
            }
            return CalculateGotoResult(target, input);
        }
    }
}
