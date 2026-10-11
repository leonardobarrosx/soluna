"""Author the shipped Soluna example. Does not touch accounts or private maps."""
import random, json, base64, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'games/soluna/data'
def world(id,name,w,h,seed):
 rng=random.Random(seed)
 m={'Id':id,'Name':name,'Width':w,'Height':h,'Revision':9,'Moral':0,'Depth':0,'Seed':seed,'Music':'','Links':{},'Warps':[],'Npcs':[],'Landmarks':[],'Props':[], 'Tilesets':['soluna/atlas.png'],'Layers':[[rng.choice([0]*32+[12]*9+[8,9,10,11]) for _ in range(w*h)]]+[[-1]*(w*h) for _ in range(4)],'FringeFrom':3,'SpawnX':w//2,'SpawnY':h-5}
 a=bytearray(w*h)
 def put(x,y,t,layer=0,block=False):
  if 0<=x<w and 0<=y<h:
   m['Layers'][layer][y*w+x]=t
   if block:a[y*w+x]=1
 def obj(x,y,cx,cy,ow,oh):
  if (cx,cy) in [(0,2),(4,2),(10,2)]:
   m['Props'].append({'Sprite':{0:'props/pine.png',4:'props/cottage.png',10:'props/ruin.png'}[cx],'X':x,'Y':y,'Width':ow,'Height':oh})
   for xx in range(ow):
    if 0<=x+xx<w and 0<=y+oh-1<h:a[(y+oh-1)*w+x+xx]=1
   return
  for yy in range(oh):
   for xx in range(ow):put(x+xx,y+yy,(cy+yy)*16+cx+xx,3 if yy<oh-1 else 1,yy==oh-1)
 def tree(x,y):obj(x,y,0,2,2,3)
 def point(kind,name,x,y,t):
  m['Landmarks'].append({'Kind':kind,'Name':name,'X':x,'Y':y});put(x,y,t,1);a[y*w+x]=3
 def road(x0,y0,x1,y1,t=2):
  for y in range(y0,y1+1):
   for x in range(x0,x1+1):put(x,y,rng.choice([t]*4+[13 if t==2 else 14]) if t in (2,3) else t);a[y*w+x]=0
 # Rim protects the world but keeps visible tree crowns inside.
 for x in range(w):a[x]=a[(h-1)*w+x]=1
 for y in range(h):a[y*w]=a[y*w+w-1]=1
 for x in range(1,w-2,3):tree(x,1);tree(x,h-4)
 for y in range(4,h-4,4):tree(1,y);tree(w-3,y)
 if id==1:
  m['SpawnX'],m['SpawnY']=22,22
  road(20,4,24,h-5);road(6,21,37,23);road(19,18,25,26,3)
  obj(10,15,4,2,5,4);obj(29,15,4,2,5,4);obj(10,27,4,2,5,4);obj(28,27,4,2,5,4)
  obj(21,8,10,2,3,3)
  for x,y in [(17,18),(27,18),(17,26),(27,26),(9,23),(34,21)]:put(x,y,96,1)
  for x,y in [(5,10),(35,8),(5,27),(33,5),(7,6),(18,7),(7,19),(34,21),(16,15),(29,25)]:tree(x,y)
  for y in range(26,31):
   for x in range(35,41):
    if (x in (35,40)) and (y in (26,30)):continue
    put(x,y,4,block=True)
  for x,y,t in [(10,19,107),(34,19,107),(11,31,108),(27,30,111),(21,17,109),(23,16,109),(20,10,110),(25,10,110),
                (17,20,108),(28,20,108),(18,28,108),(32,30,107),(33,27,111),(36,25,108)]:put(x,y,t,1)
  point(6,'Águas silenciosas',34,27,100)
  point(0,'Santuário das memórias',22,17,98)
  point(1,'Mercador de cinzas',15,22,97)
  point(11,'Bancada de ofícios',17,23,106)
  point(2,'O sino da vigília',24,25,100)
  m['Npcs']=[{'NpcId':1,'X':14,'Y':22},{'NpcId':2,'X':23,'Y':17}]
  for x in range(20,25):
   put(x,4,3);a[4*w+x]=2;m['Warps'].append({'X':x,'Y':4,'Map':2,'ToX':24,'ToY':41})
 else:
  m['SpawnX'],m['SpawnY']=24,41
  road(22,6,26,h-5);road(8,33,36,36);road(16,20,32,22)
  for _ in range(75):
   x=rng.randrange(4,w-5);y=rng.randrange(4,h-7)
   if 18<=x<=29 or 29<=y<=39:continue
   tree(x,y)
  for y in range(30,37):
   for x in range(5,12):
    if (x in (5,11)) and (y in (30,36)):continue
    put(x,y,4,block=True)
  for x,y,t in [(13,32,108),(15,37,111),(30,35,108),(17,30,111),(31,24,110),(21,8,110),(27,8,110),(27,10,109),
                (15,20,108),(30,19,111),(20,14,110),(28,14,110),(17,38,108)]:put(x,y,t,1)
  point(6,'Lago dos esquecidos',12,34,100)
  point(7,'Ervas amargas',19,37,99)
  point(8,'Veio de ferro',29,34,101)
  point(9,'Madeira antiga',18,31,104)
  point(10,'Cristais de eco',28,22,105)
  point(3,'Relicário do bosque',15,21,97)
  obj(23,5,10,2,3,3)
  point(4,'Limiar do Eclipse',24,9,102)
  for i,(x,y) in enumerate([(24,32),(25,27),(21,24),(28,18),(22,14),(32,21),(14,26),(30,30)]):
   m['Npcs'].append({'NpcId':[10,11,12][i%3],'X':x,'Y':y});a[y*w+x]=0
  for x in range(22,27):
   a[43*w+x]=2;m['Warps'].append({'X':x,'Y':43,'Map':1,'ToX':22,'ToY':6})
 m['Attributes']=base64.b64encode(a).decode()
 return m
ROOT.mkdir(parents=True,exist_ok=True)
for m in [world(1,'Vigília de Soluna',44,36,83),world(2,'Bosque dos Esquecidos',48,48,192)]:
 (ROOT/f'maps/{m["Id"]}.json').write_text(json.dumps(m,ensure_ascii=False,separators=(',',':')))
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
