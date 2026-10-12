"""Author the shipped Soluna example. Does not touch accounts or private maps."""
import json, sys
from pathlib import Path
from soluna_maps import authored_maps

ROOT = Path(__file__).resolve().parents[1] / 'games/soluna/data'
(ROOT / 'maps').mkdir(parents=True, exist_ok=True)
for m in authored_maps():
    (ROOT / f'maps/{m["Id"]}.json').write_text(json.dumps(m, ensure_ascii=False, separators=(',', ':')))
if '--maps-only' in sys.argv:
 print('Rebuilt the two public example maps; rules, accounts and private maps untouched.')
 raise SystemExit(0)
items=[{'id':100,'name':'Fragmento de lâmina','slot':'Weapon','attack':2,'starter':True},
 {'id':101,'name':'Espada de ferro pálido','slot':'Weapon','attack':6,'price':60},
 {'id':102,'name':'Lâmina de ecos','slot':'Weapon','attack':10,'luck':-2},
 {'id':103,'name':'Talismã das marés','slot':'Neck','defense':2,'luck':5},
 {'id':104,'name':'Manto do peregrino','slot':'Back','defense':4,'luck':1}]
(ROOT/'items.json').write_text(json.dumps(items,ensure_ascii=False,indent=2))
npcs=[{'id':1,'name':'Mercador de cinzas','behaviour':'Friendly','hp':80,'sprite':'characters/echoes/merchant.png'},
 {'id':2,'name':'A última vigília','behaviour':'Friendly','hp':80,'sprite':'characters/echoes/keeper.png'}]
for id,name,sprite,hp,attack,exp,behavior in [(10,'Eco errante','wisp',24,6,12,'Aggressive'),(11,'Raiz desperta','thorn',35,7,16,'Passive'),(12,'Cinza faminta','cinder',42,9,20,'Aggressive'),(99,'Guardião do Eclipse','guardian',130,15,75,'Aggressive')]:
 npcs.append({'id':id,'name':name,'sprite':f'characters/echoes/{sprite}.png','behaviour':behavior,'hp':hp,'attack':attack,'defense':2 if id!=99 else 5,'exp':exp,'range':5,'moveMs':700,'attackMs':1500,'respawnSeconds':35,'boss':id==99})
(ROOT/'npcs.json').write_text(json.dumps(npcs,ensure_ascii=False,indent=2))
resources=[('wood','Madeira antiga',2),('ore','Minério pálido',3),('herb','Erva amarga',2),('fish','Peixe prateado',3),('echo','Essência bruta',4),('ingot','Lingote pálido',9)]
activities=[('fishing','fish','fish','A linha retorna do escuro.',4,2),('forage','gather','herb','Você recolhe o que resiste.',4,3),('mining','mine','ore','A pedra cede sob suas mãos.',4,4),('woodcutting','wood','wood','O bosque entrega um fragmento.',4,-1),('extraction','extract','echo','Um eco se desprende do cristal.',5,2)]
recipes=[('tonic','Tônico de vida','Restaura 40 de vida.',{'herb':2,'fish':1},0,'',0,2),('ether','Éter azul','Restaura 25 de essência.',{'herb':1,'echo':2},0,'',1,2),('smelt','Fundir lingote','Metal para novas ferramentas.',{'ore':3,'wood':2},0,'ingot',-1,1),('blade','Espada de ferro pálido','Arma · +6 ataque.',{'ingot':2,'wood':3},101,'',-1,1),('echo-blade','Lâmina de ecos','Arma · +10 ataque, -2 sorte.',{'ingot':3,'echo':8},102,'',-1,1),('talisman','Talismã das marés','Pescoço · +2 defesa, +5 sorte.',{'fish':5,'echo':5},103,'',-1,1),('cloak','Manto do peregrino','Costas · +4 defesa, +1 sorte.',{'herb':6,'wood':5,'echo':2},104,'',-1,1),('tea','Chá dos espíritos','Sorte favorecida por dois minutos.',{'herb':3,'fish':1},0,'',5,1),('salt','Sal de cinzas','Dissipa uma maldição.',{'ore':2,'echo':2},0,'',6,1)]
content={'EmergentAppearance':True,'DeathResetsLife':True,'Dungeon':{'FirstMapId':1001,'Name':'Ruínas do Eclipse','Tileset':'soluna/atlas.png','FloorTile':5,'WallTile':7,'Enemies':[10,11,12],'Boss':99,'BossEvery':3},'Resources':[{'id':i,'name':n,'value':v} for i,n,v in resources], 'Activities':[dict(zip(['id','habit','resource','message','cooldownSeconds','supply'],v)) for v in activities],'Recipes':[dict(zip(['id','name','description','costs','item','resource','supply','amount'],r)) for r in recipes]}
(ROOT/'adventure.json').write_text(json.dumps(content,ensure_ascii=False,indent=2))
rules=[]
def rule(id,name,kind,required,weights,tint,accent,silhouette,trace,**stats):
 rules.append({'id':id,'name':name,'kind':kind,'requires':required,'weights':weights,'tint':tint,'accent':accent,'silhouette':silhouette,'trace':trace,**stats})
rule('reaper','Ceifador','calling',{'melee':12,'hunt':3},{'melee':1,'hunt':4},'#76657f','#dab18b','blade','Seu corpo aprendeu o peso de uma lâmina.',attack=4,defense=2,health=10)
rule('weaver','Tecelão de ecos','calling',{'magic':6},{'magic':7,'extract':3},'#656c9c','#b1d7d4','robe','O silêncio agora vibra entre seus dedos.',mana=24,skillCost=5)
rule('tidekeeper','Guardião das marés','calling',{'fish':8},{'fish':6,'spirits':2},'#487c7a','#b5d8c0','hood','Sua respiração encontrou o ritmo das águas.',health=18,defense=2)
rule('marshblade','Lâmina do brejo','calling',{'fish':5,'melee':8},{'fish':5,'melee':2,'hunt':3},'#596f65','#ccd099','blade','Paciência e violência deixaram a mesma marca.',attack=3,health=12,skillCooldownMs=2000)
rule('forgeborn','Filho da forja','calling',{'mine':5,'craft':3},{'mine':4,'craft':8,'wood':2},'#826551','#d8ae83','plate','Seus braços guardam a memória do metal.',defense=5,attack=2)
rule('rootwalker','Andarilho de raízes','calling',{'gather':5,'wood':5},{'gather':5,'wood':5,'explore':1},'#57785c','#c5c78a','hood','O bosque reconhece seus passos.',health=20,skillCooldownMs=2000)
rule('moonbound','Lunário','bloodline',{'moonfood':3,'magic':3},{'moonfood':10,'magic':2},'#8497b5','#d5e8c9','robe','Um brilho frio atravessa a pele que você chamava sua.',mana=16)
rule('wildborn','Silvestre','bloodline',{'wildfood':3,'gather':5},{'wildfood':10,'gather':3,'fish':2},'#679381','#d6c78f','hood','Pequenas raízes procuram a luz sob sua pele.',health=20)
rule('emberkin','Ígneo','bloodline',{'emberfood':3,'hunt':6},{'emberfood':10,'hunt':3},'#a46e68','#ffd09c','blade','Há brasas onde antes havia apenas sombra.',attack=3)
(ROOT/'evolution.json').write_text(json.dumps({'rules':rules},ensure_ascii=False,indent=2))
print('Soluna example: village, forest, nine recipes, five professions and nine emergent forms.')
