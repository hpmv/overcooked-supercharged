using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

public class RoundDataAuxMessage : Serialisable
{
    public AssembledDefinitionNode[] recipes;
    public int currentIndex;

    public bool Deserialise(BitStreamReader reader)
    {
        var length = reader.ReadUInt32(32);
        recipes = new AssembledDefinitionNode[length];
        for (var i = 0; i < length; i++)
        {
            recipes[i] = AssembledDefinitionNodeFactory.CreateNode((int)reader.ReadUInt32(4));
            recipes[i].Deserialise(reader);
        }
        currentIndex = (int)reader.ReadUInt32(32);
        return true;
    }

    public void Serialise(BitStreamWriter writer)
    {
        throw new NotImplementedException();
    }
}