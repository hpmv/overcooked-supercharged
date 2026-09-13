using System;

namespace SuperchargedPatch
{
    // A controller observation, never a game event. The native update method
    // reports active-state changes and does not return the initial contents.
    internal static class NativeIngredientContentsObservation
    {
        internal static IngredientContainerMessage Capture(ServerIngredientContainer source)
        {
            if (ReferenceEquals(source, null)) throw new ArgumentNullException("source");
            var contents = source.GetContents();
            if (contents == null) throw new InvalidOperationException("Native ingredient contents were not observed.");
            var message = new IngredientContainerMessage();
            message.Initialise(contents);
            return message;
        }
    }
}
