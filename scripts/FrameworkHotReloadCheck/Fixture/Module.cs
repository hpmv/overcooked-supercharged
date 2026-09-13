using System;
using System.IO;
using System.Collections.Generic;
using SuperchargedPatch.Authoring;

public sealed class Module : IAuthoringModule
{
    private string receipt;
    public string Name { get {
#if REVISION_Two
        return "revision-two";
#else
        return "revision-one";
#endif
    } }
    public int ApiVersion {get{return 1;}}
    public object Invoke(string operation,Dictionary<string,object> args)
    {
        if(operation=="configure")receipt=(string)args["receipt"];
        if(operation=="fail")throw new InvalidOperationException("intentional probe failure");
        return Name;
    }
    public void Dispose(){if(receipt!=null)File.WriteAllText(receipt,Name);}
}
public sealed class Unsupported : IAuthoringModule
{
    public string Name {get{return "unsupported";}}
    public int ApiVersion {get{return 999;}}
    public object Invoke(string operation,Dictionary<string,object> args){return null;}
    public void Dispose(){}
}
