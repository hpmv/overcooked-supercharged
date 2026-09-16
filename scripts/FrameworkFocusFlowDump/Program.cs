using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

string assemblyPath = args.Length > 0 ? args[0] : "runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll";
using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots)
    {
        yield return type;
        foreach (var nested in Types(type.NestedTypes)) yield return nested;
    }
}

string Id(MethodReference method) => method.DeclaringType.FullName + "::" + method.Name;
var methods = Types(assembly.MainModule.Types).SelectMany(type => type.Methods).Where(method => method.HasBody).ToArray();
var focusReads = methods.Where(method => method.Body.Instructions.Any(instruction =>
    instruction.Operand is MethodReference called && called.DeclaringType.FullName == "UnityEngine.Application" && called.Name == "get_isFocused"))
    .Select(method => method.FullName).OrderBy(value => value).ToArray();
var lifecycle = methods.Where(method => method.Name is "OnApplicationFocus" or "OnApplicationPause")
    .Select(method => new
    {
        method = method.FullName,
        calls = method.Body.Instructions.Where(instruction => instruction.Operand is MethodReference)
            .Select(instruction => Id((MethodReference)instruction.Operand)).ToArray(),
        fields = method.Body.Instructions.Where(instruction => instruction.Operand is FieldReference)
            .Select(instruction => ((FieldReference)instruction.Operand).FullName).ToArray()
    }).OrderBy(value => value.method).ToArray();

if (args.Length > 2 && args[1] == "--names")
{
    var patterns = args.Skip(2).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(methods.Where(method => patterns.Any(pattern =>
        method.DeclaringType.FullName.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        .Select(method => method.FullName).OrderBy(value => value).ToArray(), new JsonSerializerOptions { WriteIndented = true }));
    return;
}

var relevantCallNames = new HashSet<string>(new[]
{
    "CanButtonBePressed", "CanProcessInput", "JustPressed", "JustReleased",
    "HasUnclaimedPressEvent", "HasUnclaimedReleaseEvent", "GetGated"
});
var directInputCallers = methods.Where(method => method.Body.Instructions.Any(instruction =>
    instruction.Operand is MethodReference called && relevantCallNames.Contains(called.Name))).ToArray();
var selected = new HashSet<MethodDefinition>();
if (args.Length > 1)
{
    var patterns = args.Skip(1).ToArray();
    foreach (var method in methods)
        if (patterns.Any(pattern => method.FullName.Contains(pattern, StringComparison.OrdinalIgnoreCase))) selected.Add(method);
}
else
{
    foreach (var method in directInputCallers) selected.Add(method);
    foreach (var candidate in methods)
        if (candidate.Body.Instructions.Any(instruction => instruction.Operand is MethodReference called &&
            directInputCallers.Any(target => called.DeclaringType.FullName == target.DeclaringType.FullName && called.Name == target.Name)))
            selected.Add(candidate);
    foreach (var method in focusReads.Select(name => methods.Single(candidate => candidate.FullName == name))) selected.Add(method);
}

var targets = selected.OrderBy(method => method.FullName)
    .Select(method => new
    {
        method = method.FullName,
        il = method.Body.Instructions.Select(instruction => new
        {
            offset = instruction.Offset,
            opcode = instruction.OpCode.Name,
            operand = instruction.Operand switch
            {
                MethodReference called => called.FullName,
                FieldReference field => field.FullName,
                Instruction branch => "IL_" + branch.Offset.ToString("x4"),
                Instruction[] branches => String.Join(",", branches.Select(value => "IL_" + value.Offset.ToString("x4"))),
                null => null,
                _ => instruction.Operand.ToString()
            }
        }).ToArray()
    }).ToArray();

Console.WriteLine(JsonSerializer.Serialize(new { assembly = Path.GetFullPath(assemblyPath), focusReads, lifecycle, targets },
    new JsonSerializerOptions { WriteIndented = true }));
