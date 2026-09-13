// Adapted from plugin/JsonRead.cs; strict bounded CLR-only parsing.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SuperchargedPatch.Bridge
{
    internal static class JsonRead
    {
        public static Dictionary<string, object> Object(string text)
        {
            var result = new Parser(text).Parse() as Dictionary<string, object>;
            if (result == null) throw new FormatException("JSON root must be an object.");
            return result;
        }
        public static string Text(Dictionary<string, object> value, string key, string fallback)
        {
            object result;
            if (!value.TryGetValue(key, out result)) return fallback;
            if (result == null) return null;
            var text = result as string;
            if (text == null) throw new FormatException(key + " must be a string.");
            return text;
        }
        public static int Integer(Dictionary<string, object> value, string key, int fallback)
        {
            object result;
            if (!value.TryGetValue(key, out result)) return fallback;
            var number = result as Number; int parsed;
            if (number == null || !Int32.TryParse(number.Token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed))
                throw new FormatException(key + " must be a 32-bit integer.");
            return parsed;
        }
        internal sealed class Number
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


