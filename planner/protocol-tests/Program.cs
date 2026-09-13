using System.Globalization;
using System.Text.Json;
using Oc2Tas;

internal static class Program
{
    private static int checks;
    private static void Main()
    {
        var request=JsonRead.Request("""
        {"version":1,"command":"step","steps":120,"seed":-2147483648,"isolateRecipeRandom":true,"path":"M:\\artifacts\\score\u0020\ud83c\udf7d.png","inputs":[{"player":0,"x":0.25,"y":-1e-2,"pickup":true,"use":false,"dash":true},{"player":1},{"player":2,"use":true},{"player":3,"x":-0.125}]}
        """);
        Check(request.inputs.Length==4,"all nested inputs present");
        Check(request.inputs[0].x==.25f&&request.inputs[0].y==-.01f&&request.inputs[0].pickup&&request.inputs[0].dash,"axes/exponent/buttons");
        Check(request.inputs[1].x==0&&!request.inputs[1].pickup&&request.inputs[2].use,"neutral defaults");
        Check(request.seed==int.MinValue&&request.steps==120&&request.isolateRecipeRandom,"scalar fields");
        Check(request.path=="M:\\artifacts\\score 🍽.png","unicode pair and slash escapes");
        Check(JsonRead.Request("{\"version\":1,\"command\":\"inspect\"}").steps==1,"request default steps");
        Check(JsonRead.Request("{\"version\":1,\"command\":\"preview\",\"count\":72}").count==72,"native preview count");
        Check(JsonRead.Request("{\"version\":1,\"command\":\"step\",\"inputs\":null}").inputs==null,"null inputs neutral");
        var escaped=JsonRead.Request("{\"version\":1,\"command\":\"inspect\",\"path\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0001\"}");
        Check(escaped.path=="\"\\/\b\f\n\r\t\u0001","all string escapes");

        string[] rejected={
            "", "[]", "null", "{}", "{\"version\":2,\"command\":\"inspect\"}",
            "{\"version\":1,\"command\":\"inspect\",\"command\":\"step\"}",
            "{\"version\":1,\"command\":\"inspect\",\"\\u0063ommand\":\"step\"}",
            "{\"version\":1,\"command\":\"inspect\",}",
            "{\"version\":01,\"command\":\"inspect\"}",
            "{\"version\":1e0,\"command\":\"inspect\"}",
            "{\"version\":1,\"command\":null}",
            "{\"version\":1,\"command\":\"\"}",
            "{\"version\":1,\"command\":\"inspect\",\"steps\":2147483648}",
            "{\"version\":1,\"command\":\"inspect\",\"seed\":1.5}",
            "{\"version\":1,\"command\":\"inspect\",\"unknown\":1}",
            "{\"version\":1,\"command\":\"inspect\",\"path\":\"\\ud800\"}",
            "{\"version\":1,\"command\":\"inspect\",\"path\":\"\\udc00\"}",
            "{\"version\":1,\"command\":\"inspect\",\"path\":\"\\uxxxx\"}",
            "{\"version\":1,\"command\":\"inspect\",\"path\":\"a\nb\"}",
            "{\"version\":1,\"command\":\"inspect\",\"path\":\"\\q\"}",
            "{\"version\":1,\"command\":\"inspect\"} false",
            "{\"version\":1,\"command\":\"inspect\",\"isolateRecipeRandom\":1}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":{}}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":4}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0},{\"player\":0}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"x\":1e999}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"x\":1e39}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"x\":NaN}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"x\":.1}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"x\":1.}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"x\":1e+}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"use\":\"true\"}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0,\"typo\":true}]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0},]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[{\"player\":0},null]}",
            "{\"version\":1,\"command\":\"step\",\"inputs\":[0,1,2,3,4]}"
        };
        foreach(string json in rejected)Reject(json);
        Reject(new string('[',26)+"0"+new string(']',26));

        // Independently parse outgoing JSON with the platform implementation.
        // This catches dropped nested DTO arrays and malformed string escapes.
        var response=new ResponseStub
        {
            inputs=request.inputs,
            state=new StateStub{chefs=new[]{new ChefStub{playerId=3,position=new Vec3{x=1.25f,y=-0.5f,z=3.5f},name="chef\n\"\\ 🍽"}},score=5123,warnings=new[]{"a\b\f\u0001","日本語"}},
            nan=float.NaN,positiveInfinity=double.PositiveInfinity
        };
        string output=JsonText.Serialize(response);
        using(var doc=JsonDocument.Parse(output))
        {
            var root=doc.RootElement;
            Check(root.GetProperty("inputs").GetArrayLength()==4,"outgoing nested input arrays preserved");
            Check(root.GetProperty("inputs")[2].GetProperty("use").GetBoolean(),"outgoing nested bool");
            Check(root.GetProperty("state").GetProperty("chefs")[0].GetProperty("position").GetProperty("x").GetSingle()==1.25f,"outgoing third-level object");
            Check(root.GetProperty("state").GetProperty("chefs")[0].GetProperty("name").GetString()==response.state.chefs[0].name,"outgoing string controls and Unicode");
            Check(root.GetProperty("nan").ValueKind==JsonValueKind.Null&&root.GetProperty("positiveInfinity").ValueKind==JsonValueKind.Null,"nonfinite outgoing values become null");
        }
        var roundtrip=JsonRead.Request(JsonText.Serialize(request));
        Check(roundtrip.inputs.Length==4&&roundtrip.inputs[3].x==-.125f&&roundtrip.path==request.path,"serializer/parser request roundtrip");
        var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            var invariant=JsonRead.Request(JsonText.Serialize(request));
            Check(invariant.inputs[0].x==.25f&&invariant.inputs[0].y==-.01f,"invariant numeric culture");
        }
        finally{CultureInfo.CurrentCulture=previous;}
        Console.WriteLine("PASS: "+checks+" plugin JSON assertions.");
    }
    private static void Reject(string json)
    {
        try{JsonRead.Request(json);}
        catch(FormatException){checks++;return;}
        throw new Exception("Accepted malformed request: "+json);
    }
    private static void Check(bool condition,string label)
    {
        if(!condition)throw new Exception("FAIL: "+label);checks++;
    }
    private sealed class ResponseStub{public int version=1;public ChefInput[] inputs;public StateStub state;public float nan;public double positiveInfinity;}
    private sealed class StateStub{public ChefStub[] chefs;public int score;public string[] warnings;}
    private sealed class ChefStub{public int playerId;public string name;public Vec3 position;}
    private sealed class Vec3{public float x,y,z;}
}

namespace Oc2Tas
{
    public class Request
    {
        public int version=1;
        public string command;
        public int steps=1,seed=0,count=60;
        public int width=1280,height=720,renderRate=60;
        public bool isolateRecipeRandom;
        public ChefInput[] inputs;
        public string path;
    }
    public class ChefInput{public int player;public float x,y;public bool pickup,use,dash;}
}
