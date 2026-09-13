using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Oc2Tas
{
    // Unity 2017's serializer omits several nested plugin DTOs. Serialize the
    // public-field protocol explicitly, including nested sealed classes.
    internal static class JsonText
    {
        private static readonly Dictionary<Type,FieldInfo[]> Fields=new Dictionary<Type,FieldInfo[]>();
        public static string Serialize(object value)
        {
            StringBuilder output=new StringBuilder(32768);
            Write(output,value,0);return output.ToString();
        }
        private static void Write(StringBuilder b,object value,int depth)
        {
            if(depth>24)throw new InvalidOperationException("JSON nesting exceeded");
            if(value==null){b.Append("null");return;}
            string text=value as string;if(text!=null){Quoted(b,text);return;}
            if(value is bool){b.Append((bool)value?"true":"false");return;}
            if(value is float)
            {
                float f=(float)value;b.Append(float.IsNaN(f)||float.IsInfinity(f)?"null":f.ToString("R",CultureInfo.InvariantCulture));return;
            }
            if(value is double)
            {
                double d=(double)value;b.Append(double.IsNaN(d)||double.IsInfinity(d)?"null":d.ToString("R",CultureInfo.InvariantCulture));return;
            }
            Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||value is decimal)
            {b.Append(Convert.ToString(value,CultureInfo.InvariantCulture));return;}
            IEnumerable items=value as IEnumerable;
            if(items!=null)
            {
                b.Append('[');bool first=true;
                foreach(object item in items){if(!first)b.Append(',');first=false;Write(b,item,depth+1);}b.Append(']');return;
            }
            FieldInfo[] fields;
            if(!Fields.TryGetValue(type,out fields))
            {
                fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance);Fields.Add(type,fields);
            }
            b.Append('{');bool firstField=true;
            foreach(FieldInfo field in fields)
            {
                if(field.IsNotSerialized)continue;
                if(!firstField)b.Append(',');firstField=false;
                Quoted(b,field.Name);b.Append(':');Write(b,field.GetValue(value),depth+1);
            }
            b.Append('}');
        }
        private static void Quoted(StringBuilder b,string text)
        {
            b.Append('"');
            foreach(char c in text)
            {
                switch(c)
                {
                    case '"':b.Append("\\\"");break;
                    case '\\':b.Append("\\\\");break;
                    case '\n':b.Append("\\n");break;
                    case '\r':b.Append("\\r");break;
                    case '\t':b.Append("\\t");break;
                    default:if(c<32)b.Append("\\u"+((int)c).ToString("x4"));else b.Append(c);break;
                }
            }
            b.Append('"');
        }
    }
}
