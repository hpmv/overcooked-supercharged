using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Oc2Tas
{
    [Serializable]
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
    [Serializable]
    public class Response
    {
        public int version=1;
        public bool ok=true;
        public string error="",message="",session="";
        public long frame,physicsFrame;
        public bool paused;
        public ChefInput[] inputs;
        public WorldState state;
        public RecipePreviewState preview;
        public TransportState transport;
    }
    [DefaultExecutionOrder(-32000)]
    [BepInPlugin("local.oc2tas.newbot", "Overcooked 2 TAS", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        private readonly TransportQueue inbox=new TransportQueue();
        private readonly AutoResetEvent signal=new AutoResetEvent(false);
        private TcpListener listener;
        private Thread server;
        private volatile bool running=true;
        private bool paused;
        private Envelope stepping;
        private Envelope loading;
        private int remaining;
        private string taskRoot;
        private Harmony harmony;
        public long engineFrame;

        private void Awake()
        {
            Instance=this;
            taskRoot=Environment.GetEnvironmentVariable("OC2TAS_ROOT") ?? Directory.GetParent(Paths.GameRootPath).FullName;
            Directory.CreateDirectory(Path.Combine(taskRoot,"artifacts"));
            Application.runInBackground=true;
            QualitySettings.vSyncCount=0;
            Application.targetFrameRate=60;
            harmony=new Harmony("local.oc2tas.newbot");
            SaveIsolation.Install(harmony,taskRoot);
            Inputs.Install(harmony);
            NativeTime.Install(harmony);
            GameEvents.Install(harmony);
            EntityRegistrationAudit.Install(harmony);
            int port=17634;
            int.TryParse(Environment.GetEnvironmentVariable("OC2TAS_PORT")??"17634",out port);
            listener=new TcpListener(IPAddress.Loopback,port);
            listener.Start();
            server=new Thread(ServerLoop); server.IsBackground=true; server.Start();
            Logger.LogInfo("OC2TAS protocol 1 listening on 127.0.0.1:"+port+"; root="+taskRoot);
            StartCoroutine(FrameBoundary());
        }

        private void Update() { engineFrame++; NativeTime.Advance(); }
        private void FixedUpdate() { NativeTime.FixedFrame++; }

        private IEnumerator FrameBoundary()
        {
            while(running)
            {
                yield return new WaitForEndOfFrame();
                if(loading!=null && ((SessionSetup.KitchenReady && NativeTime.LevelReady) || SessionSetup.Failed))
                {
                    paused=true;
                    Complete(loading,SessionSetup.Failed?SessionSetup.LastError:null,"load completed");loading=null;
                }
                if(stepping!=null && --remaining<=0)
                {
                    paused=true; Complete(stepping,null,""); stepping=null;
                }
                bool wait;
                do
                {
                    bool dispatched=inbox.DispatchNext(ReleaseDisconnected,Handle);
                    wait=paused && stepping==null;
                    if(wait && !dispatched) signal.WaitOne(100);
                } while(running && wait);
            }
        }

        private void ReleaseDisconnected(TransportConnection owner)
        {
            Inputs.Release();
            GameEvents.RecordInputRelease("controller-disconnected");
            if(stepping!=null && object.ReferenceEquals(stepping.Owner,owner))
            { Complete(stepping,"connection lost","");stepping=null; }
            // A neutral resume followed by a cancelled queued step must also
            // stop. An asynchronous native load is allowed to finish with all
            // inputs neutral; its existing completion branch then pauses.
            paused=loading==null;
        }

        private void Handle(Envelope e)
        {
            try
            {
                Request r=JsonRead.Request(e.json);
                if(r==null||r.version!=1) throw new ArgumentException("Expected protocol version 1");
                switch(r.command)
                {
                    case "inspect": case "state": Complete(e,null,"");break;
                    case "preview": Complete(e,null,"native recipe preview",RecipePreview.Preview(r.seed,r.count));break;
                    case "pause": paused=true;Inputs.Release();Complete(e,null,"");break;
                    case "resume": paused=false;Inputs.Apply(r.inputs);Complete(e,null,"");break;
                    case "step":
                        if(r.steps<1||r.steps>60000)throw new ArgumentException("steps must be 1..60000");
                        Inputs.Apply(r.inputs); paused=false;stepping=e;remaining=r.steps;break;
                    case "setup": case "players":
                        paused=false;Inputs.Release();Complete(e,null,SessionSetup.Execute("setup"));break;
                    case "load": case "restart":
                        paused=false;Inputs.Release();NativeTime.IsolateRecipeRandom=r.isolateRecipeRandom;NativeTime.Begin(r.seed);
                        SessionSetup.Execute(r.command);loading=e;break;
                    case "screenshot":
                        string destination=CheckedOutput(r.path,"screenshot.png");
                        Texture2D texture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
                        texture.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);texture.Apply();
                        File.WriteAllBytes(destination,texture.EncodeToPNG());UnityEngine.Object.Destroy(texture);
                        Complete(e,null,destination);break;
                    case "render":
                        if(r.width<640||r.width>3840||r.height<360||r.height>2160||r.renderRate<0||r.renderRate>240)
                            throw new ArgumentException("render expects width 640..3840, height 360..2160, renderRate 0..240 (0 is unlimited)");
                        QualitySettings.vSyncCount=0;Application.targetFrameRate=r.renderRate==0?-1:r.renderRate;
                        Screen.SetResolution(r.width,r.height,false);Complete(e,null,"render settings applied");break;
                    case "quit":
                        Inputs.Release();Complete(e,null,"quitting");paused=false;Application.Quit();break;
                    default: throw new ArgumentException("Unknown command: "+r.command);
                }
            }
            catch(Exception ex) {Logger.LogError(ex);Complete(e,ex.ToString(),"");}
        }
        private string CheckedOutput(string requested,string name)
        {
            string root=Path.GetFullPath(Path.Combine(taskRoot,"artifacts"))+Path.DirectorySeparatorChar;
            string path=Path.GetFullPath(string.IsNullOrEmpty(requested)?Path.Combine(root,name):requested);
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Output must be inside workspace artifacts");
            Directory.CreateDirectory(Path.GetDirectoryName(path));return path;
        }
        private void Complete(Envelope e,string error,string message)
        { Complete(e,error,message,null); }
        private void Complete(Envelope e,string error,string message,RecipePreviewState preview)
        {
            try
            {
                Response response=new Response {ok=error==null,error=error??"",message=message,paused=paused,frame=NativeTime.Frame,physicsFrame=NativeTime.FixedFrame,inputs=Inputs.Current(),session=SessionSetup.Status(),state=Telemetry.Capture(),preview=preview,transport=inbox.Capture()};
                e.result=JsonText.Serialize(response);
            }
            catch(Exception ex) {e.result="{\"version\":1,\"ok\":false,\"error\":\"telemetry serialization failed\",\"message\":\""+Escape(ex.Message)+"\"}";Logger.LogError(ex);}
            e.complete.Set();
        }
        private static string Escape(string value){return value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");}

        private void ServerLoop()
        {
            while(running)
            {
                TcpClient client=null;
                TransportConnection owner=null;
                try
                {
                    client=listener.AcceptTcpClient();client.NoDelay=true;
                    owner=inbox.Open();
                    NetworkStream stream=client.GetStream();
                    while(running)
                    {
                        byte[] size=ReadExactly(stream,4);int count=BitConverter.ToInt32(size,0);
                        if(count<2||count>16*1024*1024)throw new IOException("Bad message size");
                        Envelope e=inbox.Enqueue(owner,Encoding.UTF8.GetString(ReadExactly(stream,count)));
                        signal.Set();
                        while(running&&!e.complete.WaitOne(100))
                        {
                            if(client.Client.Poll(0,SelectMode.SelectRead)&&client.Client.Available==0)throw new IOException("Controller disconnected");
                        }
                        if(!running)break;
                        byte[] data=Encoding.UTF8.GetBytes(e.result);
                        byte[] prefix=BitConverter.GetBytes(data.Length);
                        stream.Write(prefix,0,4);stream.Write(data,0,data.Length);stream.Flush();
                    }
                }
                catch(Exception ex) {if(running)Logger.LogInfo("Controller connection ended: "+ex.Message);}
                finally {inbox.Disconnect(owner);if(client!=null)client.Close();signal.Set();}
            }
        }
        private static byte[] ReadExactly(Stream stream,int n)
        {
            byte[] buffer=new byte[n];int p=0;
            while(p<n){int got=stream.Read(buffer,p,n-p);if(got==0)throw new EndOfStreamException();p+=got;}return buffer;
        }
        private void OnDestroy()
        {
            running=false;signal.Set();if(listener!=null)listener.Stop();
            if(harmony!=null)harmony.UnpatchSelf();
        }
    }
}
