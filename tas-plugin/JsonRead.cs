using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Oc2Tas
{
    // Deliberately independent of Unity's serializer: its omission of nested
    // plugin DTO arrays silently discarded all four controller inputs.
    internal static class JsonRead
    {
        public static Request Request(string text)
        {
            Dictionary<string,object> root=AsObject(new Parser(text).Parse(),"request");
            Fields(root,new string[]{"version","command","steps","seed","count","inputs","path","isolateRecipeRandom","width","height","renderRate"},"request");
            Request request=new Request();
            request.version=Integer(Required(root,"version"),"version");
            if(request.version!=1)throw new FormatException("Unsupported protocol version");
            request.command=Text(Required(root,"command"),"command");
            if(request.command.Length==0||request.command.Length>64)throw new FormatException("command must be 1..64 characters");
            object value;
            if(root.TryGetValue("steps",out value))request.steps=Integer(value,"steps");
            if(root.TryGetValue("seed",out value))request.seed=Integer(value,"seed");
            if(root.TryGetValue("count",out value))request.count=Integer(value,"count");
            if(root.TryGetValue("width",out value))request.width=Integer(value,"width");
            if(root.TryGetValue("height",out value))request.height=Integer(value,"height");
            if(root.TryGetValue("renderRate",out value))request.renderRate=Integer(value,"renderRate");
            if(root.TryGetValue("path",out value))request.path=value==null?null:Text(value,"path");
            if(root.TryGetValue("isolateRecipeRandom",out value))request.isolateRecipeRandom=Boolean(value,"isolateRecipeRandom");
            if(root.TryGetValue("inputs",out value)&&value!=null)
            {
                ArrayList inputs=value as ArrayList;
                if(inputs==null||inputs.Count>4)throw new FormatException("inputs must be an array with at most four entries");
                request.inputs=new ChefInput[inputs.Count];
                bool[] seen=new bool[4];
                for(int i=0;i<inputs.Count;i++)
                {
                    Dictionary<string,object> input=AsObject(inputs[i],"inputs entry");
                    Fields(input,new string[]{"player","x","y","pickup","use","dash"},"inputs entry");
                    ChefInput chef=new ChefInput();
                    chef.player=Integer(Required(input,"player"),"player");
                    if(chef.player<0||chef.player>3||seen[chef.player])throw new FormatException("Input players must be unique indices 0..3");
                    seen[chef.player]=true;
                    if(input.TryGetValue("x",out value))chef.x=Float(value,"x");
                    if(input.TryGetValue("y",out value))chef.y=Float(value,"y");
                    if(input.TryGetValue("pickup",out value))chef.pickup=Boolean(value,"pickup");
                    if(input.TryGetValue("use",out value))chef.use=Boolean(value,"use");
                    if(input.TryGetValue("dash",out value))chef.dash=Boolean(value,"dash");
                    request.inputs[i]=chef;
                }
            }
            return request;
        }

        private static Dictionary<string,object> AsObject(object value,string field)
        {
            Dictionary<string,object> result=value as Dictionary<string,object>;
            if(result==null)throw new FormatException(field+" must be an object");return result;
        }
        private static object Required(Dictionary<string,object> value,string key)
        {
            object result;if(!value.TryGetValue(key,out result))throw new FormatException("Missing "+key);return result;
        }
        private static void Fields(Dictionary<string,object> value,string[] allowed,string field)
        {
            foreach(string key in value.Keys)if(Array.IndexOf(allowed,key)<0)throw new FormatException("Unknown "+field+" field: "+key);
        }
        private static string Text(object value,string field)
        {
            string result=value as string;if(result==null)throw new FormatException(field+" must be a string");return result;
        }
        private static bool Boolean(object value,string field)
        {
            if(!(value is bool))throw new FormatException(field+" must be boolean");return (bool)value;
        }
        private static int Integer(object value,string field)
        {
            Number number=value as Number;int result;
            if(number==null||!int.TryParse(number.Token,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out result))
                throw new FormatException(field+" must be a 32-bit integer");
            return result;
        }
        private static float Float(object value,string field)
        {
            Number number=value as Number;
            if(number==null)throw new FormatException(field+" must be a number");
            float result=(float)number.Value;
            if(float.IsNaN(result)||float.IsInfinity(result))throw new FormatException(field+" must be a finite single-precision number");
            return result;
        }
        private sealed class Number
        {
            public readonly string Token;
            public readonly double Value;
            public Number(string token,double value){Token=token;Value=value;}
        }

        private sealed class Parser
        {
            private readonly string text;
            private int at;
            public Parser(string source)
            {
                if(source==null||source.Length==0||source.Length>16*1024*1024)throw new FormatException("Invalid JSON length");
                text=source;
            }
            public object Parse()
            {
                object value=Value(0);Space();if(at!=text.Length)throw Error("Trailing content");return value;
            }
            private object Value(int depth)
            {
                if(depth>24)throw Error("JSON nesting exceeded");
                Space();if(at>=text.Length)throw Error("Unexpected end of JSON");
                char c=text[at];
                if(c=='{')return Object(depth);
                if(c=='[')return Array(depth);
                if(c=='\"')return String();
                if(c=='t'){Literal("true");return true;}
                if(c=='f'){Literal("false");return false;}
                if(c=='n'){Literal("null");return null;}
                if(c=='-'||Digit(c))return Numeric();
                throw Error("Unexpected JSON token");
            }
            private Dictionary<string,object> Object(int depth)
            {
                at++;Space();Dictionary<string,object> result=new Dictionary<string,object>(StringComparer.Ordinal);
                if(Take('}'))return result;
                while(true)
                {
                    Space();if(at>=text.Length||text[at]!='\"')throw Error("Object key must be a string");
                    string key=String();Space();Expect(':');
                    if(result.ContainsKey(key))throw Error("Duplicate JSON key: "+key);
                    if(result.Count>=10000)throw Error("Too many JSON object fields");
                    result.Add(key,Value(depth+1));Space();
                    if(Take('}'))return result;Expect(',');
                }
            }
            private ArrayList Array(int depth)
            {
                at++;Space();ArrayList result=new ArrayList();
                if(Take(']'))return result;
                while(true)
                {
                    if(result.Count>=10000)throw Error("Too many JSON array entries");
                    result.Add(Value(depth+1));Space();if(Take(']'))return result;Expect(',');
                }
            }
            private string String()
            {
                Expect('\"');StringBuilder result=new StringBuilder();
                while(at<text.Length)
                {
                    char c=text[at++];
                    if(c=='\"')
                    {
                        string value=result.ToString();
                        for(int i=0;i<value.Length;i++)
                        {
                            if(char.IsHighSurrogate(value[i]))
                            {
                                if(i+1>=value.Length||!char.IsLowSurrogate(value[i+1]))throw Error("Unpaired Unicode surrogate");i++;
                            }
                            else if(char.IsLowSurrogate(value[i]))throw Error("Unpaired Unicode surrogate");
                        }
                        return value;
                    }
                    if(c<32)throw Error("Unescaped control character");
                    if(c!='\\'){result.Append(c);continue;}
                    if(at>=text.Length)throw Error("Truncated escape");
                    c=text[at++];
                    switch(c)
                    {
                        case '\"':result.Append('\"');break;
                        case '\\':result.Append('\\');break;
                        case '/':result.Append('/');break;
                        case 'b':result.Append('\b');break;
                        case 'f':result.Append('\f');break;
                        case 'n':result.Append('\n');break;
                        case 'r':result.Append('\r');break;
                        case 't':result.Append('\t');break;
                        case 'u':
                            if(at+4>text.Length)throw Error("Truncated Unicode escape");
                            int code=0;
                            for(int i=0;i<4;i++)
                            {
                                char hex=text[at++];int digit=hex>='0'&&hex<='9'?hex-'0':hex>='a'&&hex<='f'?hex-'a'+10:hex>='A'&&hex<='F'?hex-'A'+10:-1;
                                if(digit<0)throw Error("Invalid Unicode escape");code=code*16+digit;
                            }
                            result.Append((char)code);break;
                        default:throw Error("Invalid string escape");
                    }
                }
                throw Error("Unterminated string");
            }
            private Number Numeric()
            {
                int start=at;
                Take('-');if(at>=text.Length)throw Error("Truncated number");
                if(Take('0')){if(at<text.Length&&Digit(text[at]))throw Error("Leading zero in number");}
                else
                {
                    if(text[at]<'1'||text[at]>'9')throw Error("Invalid number");
                    while(at<text.Length&&Digit(text[at]))at++;
                }
                if(Take('.')){int digits=at;while(at<text.Length&&Digit(text[at]))at++;if(digits==at)throw Error("Missing fraction digits");}
                if(at<text.Length&&(text[at]=='e'||text[at]=='E'))
                {
                    at++;if(at<text.Length&&(text[at]=='+'||text[at]=='-'))at++;
                    int digits=at;while(at<text.Length&&Digit(text[at]))at++;if(digits==at)throw Error("Missing exponent digits");
                }
                string token=text.Substring(start,at-start);double value;
                if(!double.TryParse(token,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value))throw Error("Number is not finite");
                return new Number(token,value);
            }
            private void Literal(string literal)
            {
                if(at+literal.Length>text.Length||string.CompareOrdinal(text,at,literal,0,literal.Length)!=0)throw Error("Invalid literal");at+=literal.Length;
            }
            private void Space(){while(at<text.Length&&(text[at]==' '||text[at]=='\t'||text[at]=='\r'||text[at]=='\n'))at++;}
            private bool Take(char c){if(at<text.Length&&text[at]==c){at++;return true;}return false;}
            private void Expect(char c){if(!Take(c))throw Error("Expected '"+c+"'");}
            private static bool Digit(char c){return c>='0'&&c<='9';}
            private FormatException Error(string message){return new FormatException(message+" at character "+at.ToString(CultureInfo.InvariantCulture));}
        }
    }
}
