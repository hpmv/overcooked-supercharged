using System.Globalization;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    // The option changes only ordinary navigation inputs. Native processing
    // waits, identity guards, reservations and walking-based deadline budgets
    // remain active. It does not shorten a cooking or mixing action.
    private JsonObject VesselAction(string type, int station, int timeout) => new()
    {
        ["type"] = type, ["station"] = station.ToString(CultureInfo.InvariantCulture),
        ["dash"] = options.ShortDashVessels, ["shortDash"] = options.ShortDashVessels,
        ["timeoutFrames"] = timeout
    };
}
