"""Native delivery message/ledger audit, independent of action completion.

Codec and scoring order are checked against the installed Assembly-CSharp IL:
KitchenFlowMessage.Serialise, TeamScoreStats.Serialise, OrderID.Serialise,
ServerKitchenFlowControllerBase.OnSuccessfulDelivery and PlateStationMessage.
All event bytes are retained. A PlateStation success flag alone is not a recipe
acceptance proof: the native station emits it after either kitchen outcome.
"""
import base64
import math
import struct

from compare_framework_frames import bits, message_info
from framework_story11_planner import require


def decode_delivery(info):
    if info.get("type") != 4 or info.get("nativeComponentType") not in {8,31}:
        return None
    data=base64.b64decode(info["bytes"],validate=True); offset=14
    def read(count):
        nonlocal offset
        value=bits(data,offset,count);offset+=count;return value
    result={"entityId":info["entityId"],"bytes":info["bytes"]}
    if info["nativeComponentType"] == 8:
        result.update(kind="plate-station",plate=read(10),success=bool(read(1)))
        return result
    subtype=read(2);team=read(2)
    if subtype != 0:
        return None
    result.update(kind="kitchen-delivery",team=team,success=bool(read(1)))
    ledger={k:read(n) for k,n in (("baseScore",16),("tips",16),("multiplier",3),("combo",8),("deductions",16))}
    result["comboMaintained"]=bool(read(1));ledger["deliveries"]=read(8)
    ledger["total"]=ledger["baseScore"]+ledger["tips"]-ledger["deductions"]
    result["ledger"]=ledger
    if result["success"]:
        result.update(station=read(10),order=read(8),wasCombo=bool(read(1)))
        fraction=struct.unpack(">f",read(32).to_bytes(4,"big"))[0]
        require(math.isfinite(fraction) and 0 <= fraction <= 1,"Invalid native delivery lifetime fraction")
        result.update(timeProportion=fraction,tip=read(6))
    return result


def delivery_events(captures, registry):
    """One or more original raw-capture dictionaries, not summarized events."""
    registry={str(r["EntityId"]):r for r in registry};result=[];seen=set()
    for capture in captures:
        require(capture.get("validation")=="exact-four-pad-frame-coverage","Missing exact emitted-input coverage")
        start,end=capture["startExclusive"],capture["endInclusive"]
        for row in capture["rawExchanges"]:
            out=row.get("output") or {};frame=out.get("FrameNumber")
            for entry in out.get("EntityRegistry") or []:registry[str(entry["EntityId"])]=entry
            # Each exchange pairs output frame N with the accepted input tagged
            # NextFrame N+1. Coverage is therefore proved by the input tag, while
            # native events retain their original output-frame number.
            accepted=(row.get("input") or {}).get("NextFrame")
            if not isinstance(accepted,int) or not start < accepted <= end:continue
            require(accepted not in seen,"Ambiguous duplicate native delivery frame")
            seen.add(accepted)
            if out.get("LastFramePaused") is not False:continue
            require(isinstance(frame,int),"Advancing native output lacks its original frame number")
            for ordinal,message in enumerate(out.get("ServerMessages") or []):
                info=message_info(message,registry)
                decoded=decode_delivery(info)
                if decoded is not None:result.append(dict(decoded,frame=frame,rawEventOrdinal=ordinal))
        require(set(range(start+1,end+1)) <= seen,"Missing native advancing output during delivery audit")
    return result


def verify_delivery(case, before, after, events, flow):
    kitchen=[e for e in events if e["kind"]=="kitchen-delivery"]
    require(len(kitchen)==1,"Need exactly one native kitchen delivery outcome")
    event=kitchen[0]
    require(event["entityId"]==flow and event["success"] and event["station"]==case["serve"] and event["order"]==case["head"]["id"],
            "Native kitchen did not accept the exact reserved order at its selected station")
    station=[e for e in events if e["kind"]=="plate-station"]
    require(len(station)==1 and station[0]["success"] and station[0]["entityId"]==case["serve"] and station[0]["plate"]==case["plate"]
            and station[0]["frame"]==event["frame"],"Missing same-frame exact plate/station receipt")
    require(event["ledger"]==after,"Native wire score and live native ledger disagree")
    require(after["baseScore"]-before["baseScore"]==case["head"]["baseValue"] and after["deliveries"]==before["deliveries"]+1,
            "Native base meal value/delivery count changed unexpectedly")
    require(after["deductions"]==before["deductions"] and after["tips"]-before["tips"]==event["tip"],"Native deduction or tip ledger mismatch")
    require(event["wasCombo"] and after["combo"]==before["combo"]+1 and after["multiplier"]==min(before["multiplier"]+1,4),
            "Expected sequential native head-order combo was not retained")
    old_multiplier=max(before["multiplier"],1)
    require(event["tip"] % old_multiplier == 0,"Native tip does not use the previous score multiplier")
    require(after["total"]-before["total"]==case["head"]["baseValue"]+event["tip"],"Native total score does not equal base plus emitted multiplied tip")
    return {"passed":True,"order":event["order"],"plate":case["plate"],"frame":event["frame"],
            "ledgerBefore":before,"ledgerAfter":after,"baseDelta":case["head"]["baseValue"],
            "nativeMultipliedTip":event["tip"],"previousMultiplier":old_multiplier,
            "tipBoundaryValue":event["tip"]//old_multiplier,"events":events,
            "scope":"Native wire outcome and live ledger; tip boundary is observed, not predicted from unobserved GameConfig"}
