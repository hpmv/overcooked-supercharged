"""Offline production adapter/installed source contract tests. No game connections."""
from pathlib import Path
import hashlib
import json
import subprocess

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts/framework-scripted-round-check-source"
OUT.mkdir(exist_ok=True)
wrapper = ROOT / "framework/patch/AlteredComponents/WarpableRoundData.cs"
manifest = json.loads((ROOT / "artifacts/framework-plugin-native-x/manifest.json").read_text(encoding="utf-8-sig"))
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
expected = next(x["Hash"] for x in manifest["sources"] if x["Path"].endswith("WarpableRoundData.cs"))
if digest(wrapper).upper() != expected:
    raise RuntimeError("Test shim must use the exact frozen-X wrapper source")
text = wrapper.read_text(encoding="utf-8-sig")
marker = "public override RecipeList.Entry[] GetNextRecipe(RoundInstanceDataBase data)\n        {"
if text.count(marker) != 1:
    raise RuntimeError("Ambiguous frozen wrapper test dispatch boundary")
shim = marker + "\n            RecipeList.Entry[] intercepted = null;\n            if (!SuperchargedPatch.Authoring.Modules.ScriptedRoundModule.BeforeNextRecipe(this,data,ref intercepted)) return intercepted;"
(OUT / "FrozenWrapperWithExplicitTestDispatch.cs").write_text(text.replace(marker, shim), encoding="utf-8")
sources = []
for name in ["RoundData.cs", "RoundDataBase.cs", "ArrayUtils.cs"]:
    source = ROOT / "artifacts/framework-native-semantics/native" / name
    (OUT / name).write_bytes(source.read_bytes())
    sources.append({"path": str(source), "sha256": digest(source)})
script = Path("H:/tiny2/Overcooked2/tinyoc2/Assets/Scripts/Assembly-CSharp/ScriptedRoundData.cs")
(OUT / script.name).write_bytes(script.read_bytes())
sources.append({"path": str(script), "sha256": digest(script)})
run = subprocess.run(["dotnet", "run", "--project", "scripts/FrameworkScriptedRoundCheck/FrameworkScriptedRoundCheck.csproj", "-c", "Release", "-p:UseAppHost=false"], cwd=ROOT, capture_output=True, text=True)
(ROOT / "artifacts/framework-scripted-round-tests-stdout.txt").write_text(run.stdout, encoding="utf-8")
(ROOT / "artifacts/framework-scripted-round-tests-stderr.txt").write_text(run.stderr, encoding="utf-8")
if run.returncode == 0:
    report = json.loads(run.stdout)
    report["inputs"] = sources + [{"path": str(wrapper), "sha256": digest(wrapper)}]
    report["generatedTestSources"] = [{"path": str(p), "sha256": digest(p)} for p in sorted(OUT.glob("*.cs"))]
    report["adapterSources"] = [{"path": str(p), "sha256": digest(p)} for p in sorted((ROOT / "framework/modules/scripted-round").glob("*.cs"))]
    (ROOT / "artifacts/framework-scripted-round-tests.json").write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
print(run.stdout, end="")
print(run.stderr, end="")
raise SystemExit(run.returncode)
