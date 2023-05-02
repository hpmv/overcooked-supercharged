using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuperchargedPatch
{
    /// <summary>
    /// Represents an EntityPath passed from the controller. This is used to cross reference
    /// an object that is being spawned but does not yet have a concrete entity ID.
    /// </summary>
    public class EntityPathReference
    {
        // Intentionally not accessible. EntityPathReference should not be unpacked.
        // It should be completely opaque in the patch project.
        private readonly int[] ids;

        internal EntityPathReference(int[] ids)
        {
            this.ids = ids;
        }

        public override string ToString()
        {
            return string.Join(".", ids.Select(id => "" + id).ToArray());
        }

        public override bool Equals(object obj)
        {
            if (obj is EntityPathReference other)
            {
                return ids.SequenceEqual(other.ids);
            }
            return false;
        }

        public override int GetHashCode()
        {
            // from stackoverflow
            int hc = ids.Length;
            foreach (int val in ids)
            {
                hc = unchecked(hc * 314159 + val);
            }
            return hc;
        }

        public Hpmv.EntityPathReference ToThrift()
        {
            var result = new Hpmv.EntityPathReference();
            result.Ids = ids.ToList();
            return result;
        }
    }

    public static class EntityPathReferenceExt
    {
        public static EntityPathReference FromThrift(this Hpmv.EntityPathReference thrift)
        {
            return new EntityPathReference(thrift.Ids.ToArray());
        }
    }
}
