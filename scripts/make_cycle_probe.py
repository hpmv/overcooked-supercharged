"""Measured native four-chef preparation/transport probe; no position writes."""
import json
from pathlib import Path

jobs=[]
def a(kind,station=None,**kw):
    return {"type":kind,**({"station":station} if station else {}),**kw}
def job(id,player,actions,deps=(),resources=()):
    jobs.append(dict(id=id,player=player,actions=actions,dependencies=list(deps),resources=list(resources)))
lb='chop:dlc08_countertop_01_chopping_circus@15.60,-14.40'
lu='chop:dlc08_countertop_01_chopping_circus@15.60,-12.00'
rb='chop:dlc08_countertop_01_chopping_circus@25.20,-14.40'
ru='chop:dlc08_countertop_01_chopping_circus@25.20,-12.00'
pot='pot:dlc08_utensil_pot_01@16.80,-10.80'
mix='mixer:dlc08_utensil_mixer_01@24.00,-10.99'
# Resolve the vessel's measured stable name through the current snapshot.
state=json.load(open('artifacts/cycle-start.json',encoding='utf-8-sig'))['state']
for e in state['entities']:
    if e['id']==6:
        p=e['position']; mix=f"bowl:{e['name'].lower()}@{p['x']:.2f},{p['z']:.2f}"
job('L01-sausage',2,[a('take','Frankfurter'),a('navigate',target={'x':13.6,'z':-12.2}),a('throw',targetEntityId=7)])
job('L02-boil',0,[a('navigate',station=lb)],['L01-sausage'])
job('L03-bun',2,[a('take','HotdogBun'),a('place',lb),a('chop',lb),a('aim-cannon',cannon='left')],['L01-sausage'])
job('L04-plate',0,[a('take','plate:equipment_plate_01@19.20,-15.60'),a('assemble',lb),a('cook',pot,timeoutFrames=1000),a('combine',pot,expectedIngredient='Frankfurter'),a('apply','condiment',expectedIngredient='Mustard',expectedRecipeId=158500),a('place',lu)],['L02-boil','L03-bun'])
job('L05-board',2,[a('take',lu),a('board-cannon',cannon='left',timeoutFrames=400)],['L04-plate'])
job('L06-fire',0,[a('fire-cannon',cannon='left',passengerPlayer=2,destinationRegion='lower-right')],['L05-board'])
job('L07-serve-return',2,[a('place','delivery'),a('portal','portal',destinationRegion='upper-left')],['L06-fire'])
job('R01-flour',1,[a('take','Flour'),a('place',ru)])
job('R02-add-flour',3,[a('take',ru),a('place',mix)],['R01-flour'])
job('R03-egg',1,[a('take','Egg'),a('place',ru)],['R02-add-flour'])
job('R04-add-egg',3,[a('take',ru),a('place',mix)],['R03-egg'])
job('R05-chocolate',1,[a('take','Chocolate'),a('place',rb),a('chop',rb)],['R03-egg'])
job('R06-mix',3,[a('take',rb),a('place',mix),a('mix',mix,timeoutFrames=1000)],['R04-add-egg','R05-chocolate'])
job('R07-board',1,[a('aim-cannon',cannon='right'),a('board-cannon',cannon='right')],['R06-mix'])
job('R08-fry',3,[a('take',mix),a('combine','basket:dlc08_frierbasket@24.00,-21.60'),a('place','mix-station:dlc08_workstation_mixer@24.00,-10.80')],['R06-mix'])
job('R09-fire',3,[a('fire-cannon',cannon='right',passengerPlayer=1,destinationRegion='lower-left')],['R07-board','R08-fry'])
job('W01-dirty-transfer',0,[a('wait',path='gameplayFrame',atLeast=2100,timeoutFrames=1500),a('take','dirty-return'),a('place','counter:dlc08_countertop_01_standard_circus@15.60,-20.40')],['L06-fire'])
job('W02-wash',1,[a('take','counter:dlc08_countertop_01_standard_circus@15.60,-20.40'),a('place','sink'),a('wash','sink',count=1)],['R09-fire','W01-dirty-transfer'])
job('W03-clean',1,[a('take','drying'),a('place','counter:dlc08_countertop_01_standard_circus@15.60,-20.40')],['W02-wash'])
job('R10-plate-donut',3,[a('take','plate:equipment_plate_01@21.60,-16.80'),a('cook','basket:dlc08_frierbasket@24.00,-21.60',timeoutFrames=900),a('combine','basket:dlc08_frierbasket@24.00,-21.60',expectedRecipeId=228996),a('place','counter:dlc08_countertop_01_standard_circus@25.20,-20.40')],['R09-fire'])
job('T01-throw-stage',2,[a('take','Frankfurter'),a('navigate',target={'x':13.6,'z':-12.2})],['W03-clean','L07-serve-return'])
job('T02-catch-stage',0,[a('navigate',target={'x':17.9,'z':-13.2}),a('face',target={'x':13.6,'z':-12.2},durationFrames=8)],['W03-clean'])
job('T03-throw-catch',2,[a('throw',recipientPlayer=0,timeoutFrames=180)],['T01-throw-stage','T02-catch-stage'])
job('T04-caught-sausage',0,[a('place','pot:dlc08_utensil_pot_01@18.00,-10.80')],['T03-throw-catch'])
Path('routes/probes/production-cycle.json').write_text(json.dumps({'timeoutFrames':4200,'jobs':jobs},indent=2))
print(mix)
