using BitStream;

namespace SuperchargedPatch.AlteredComponents
{
    public class RoundDataAuxMessage : AuxMessageBase
    {
        public AssembledDefinitionNode[] recipes;
        public int currentIndex;

        public override AuxEntityType GetAuxEntityType()
        {
            return AuxEntityType.RoundDataAux;
        }

        public override void Serialise(BitStreamWriter writer)
        {
            writer.Write((uint)recipes.Length, 32);
            foreach (var recipe in recipes)
            {
                writer.Write((uint)AssembledDefinitionNodeFactory.GetNodeType(recipe), 4);
                recipe.Serialise(writer);
            }
            writer.Write((uint)currentIndex, 32);
        }
    }
}
