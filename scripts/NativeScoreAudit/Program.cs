using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

if (args.Length is < 2 or > 4) throw new ArgumentException("Usage: NativeScoreAudit FINAL_RESPONSE OUTPUT [INITIAL_RESPONSE [PREVIEW_CALL]]; file-only, no game connection.");
var response = JsonNode.Parse(File.ReadAllText(args[0]))!.AsObject();
var state = response["state"]!.AsObject();
Verification.ValidateObservers(state);
Verification.ValidateRoundDuration(state);
Verification.ValidateRegistration(state, false);
var native = Verification.ValidateScoreEvidence(state);
if (args.Length >= 3) Verification.ValidateInitial(JsonNode.Parse(File.ReadAllText(args[2]))!.AsObject());
if (args.Length >= 4)
{
    var preview = JsonNode.Parse(File.ReadAllText(args[3]))!;
    Verification.ValidatePreview(preview["request"]!.AsObject(), preview["response"]!.AsObject());
}
var result = new JsonObject
{
    ["sourceResponse"] = Path.GetFullPath(args[0]),
    ["sourceResponseSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant(),
    ["validatorAssembly"] = typeof(Verification).Assembly.Location,
    ["validatorAssemblySha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Verification).Assembly.Location))).ToLowerInvariant(),
    ["nativeScoreProof"] = native,
    ["nativeRoundComplete"] = Verification.IsFinished(state),
    ["targetScore"] = 5000,
    ["meetsTarget"] = state["score"]!.GetValue<double>() >= 5000,
    ["classification"] = Verification.IsFinished(state) ? "Native-ended diagnostic; target qualification assessed separately" : "Incomplete native delivery prefix",
    ["initialValidated"] = args.Length >= 3,
    ["previewRestorationValidated"] = args.Length >= 4
};
File.WriteAllText(args[1], result.ToJsonString(new() { WriteIndented = true }));
Console.WriteLine(result.ToJsonString());
