// Controlled native-reader fixture. It does not simulate Unity or the game codec.
public sealed class AssembledDefinitionNode { public int Identity; }
public sealed class ServerIngredientContainer
{
    public List<AssembledDefinitionNode> Contents = new();
    public int Reads;
    public bool Unavailable;
    public AssembledDefinitionNode[] GetContents() { Reads++; return Unavailable ? null : Contents.ToArray(); }
}
public sealed class IngredientContainerMessage
{
    public AssembledDefinitionNode[] Contents;
    public void Initialise(AssembledDefinitionNode[] contents) { Contents = contents; }
}
