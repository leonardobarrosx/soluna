"""Generate the original fallback sheets. Active art is supplied by .layout.json and assets/atlases.
Requires Pillow. This does not regenerate the authored image pages or their import metadata.
"""
from pathlib import Path
import random, json
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[1]/'games/soluna/assets'
rng=random.Random(714)
atlas=Image.new('RGBA',(512,256)); d=ImageDraw.Draw(atlas)
P={'ink':'#17212d','grass':'#304b43','light':'#526448','stone':'#465463','gold':'#d8aa68','teal':'#71b8a2','rose':'#9b6d80'}
def tile(index,base):
    x=(index%16)*32;y=(index//16)*32
    d.rectangle((x,y,x+31,y+31),fill=base)
    return x,y
for i,base,colors in [(0,'#344f44',['#3c5949','#415b49','#2e473f']),(1,'#2d453f',['#354e46','#3d5749']),
 (2,'#6d6155',['#796b58','#84725c','#62594f']),(3,'#57606a',['#667079','#717780','#444e5b']),
 (4,'#234454',['#2c5965','#366676','#407a83']),(5,'#39414f',['#444c58','#4d5260','#303845']),
 (6,'#343d49',['#444a57','#263440']),(7,'#1f2836',['#293545','#334052'])]:
    x,y=tile(i,base)
    if i in (3,5,6,7):
        for yy in range(0,32,8):
            for xx in range(-8 if yy%16 else 0,32,16):
                d.rectangle((x+max(0,xx),y+yy,x+min(31,xx+14),y+yy+6),fill=rng.choice(colors))
                d.line((x+max(0,xx),y+yy,x+min(31,xx+14),y+yy),fill=colors[-1])
    elif i==4:
        for k in range(9):
            xx=rng.randrange(24);yy=rng.randrange(32);d.line((x+xx,y+yy,x+xx+rng.randrange(3,8),y+yy),fill=rng.choice(colors))
    else:
        for k in range(45):
            xx=rng.randrange(30);yy=rng.randrange(31);d.rectangle((x+xx,y+yy,x+xx+2,y+yy),fill=rng.choice(colors))
# Outdoor flagstones, irregular outlines and lichens rather than wall bricks.
x,y=tile(3,'#4d5559')
for yy in range(0,32,8):
 for xx in range(0,32,8):
  c=rng.choice(['#667071','#5a6566','#707774','#626b69'])
  d.polygon([(x+xx+2,y+yy),(x+xx+6,y+yy),(x+xx+7,y+yy+3),(x+xx+6,y+yy+7),(x+xx+1,y+yy+6),(x+xx,y+yy+2)],fill=c)
  d.line((x+xx+2,y+yy+1,x+xx+5,y+yy+1),fill='#7c817a')
  if rng.randrange(4)==0:d.point((x+xx+1,y+yy+6),fill='#617159')
# Ground variants, glowing flowers and cobbled path details.
for i in range(8,16):
    x,y=tile(i,'#344f44')
    for k in range(12):
        xx=x+rng.randrange(3,29);yy=y+rng.randrange(5,29)
        d.line((xx,yy,xx-1,yy-4),fill='#52664d');d.point((xx,yy-4),fill=['#78958c','#a48b9b','#c5b698'][i%3])
# Pine tree: 64 x 96, arranged into six tiles.
x,y=0,64
d.ellipse((x+6,y+76,x+57,y+94),fill=(13,22,28,120))
d.rectangle((x+27,y+57,x+35,y+88),fill='#3b3538');d.rectangle((x+29,y+58,x+31,y+86),fill='#7c6553')
for yy,width in [(67,29),(50,25),(34,20),(20,13)]:
    d.polygon([(x+32,y+yy-24),(x+32-width,y+yy+12),(x+32+width,y+yy+12)],fill='#152f31')
    d.polygon([(x+31,y+yy-24),(x+32-width,y+yy+7),(x+32+width-6,y+yy+5)],fill='#27493f')
    d.polygon([(x+31,y+yy-23),(x+32-width+5,y+yy+4),(x+29,y+yy-1)],fill='#3f6250')
    for k in range(12):
        xx=rng.randrange(-width+7,width-6);zz=rng.randrange(1,9)
        d.line((x+32+xx,y+yy+zz,x+35+xx,y+yy+zz),fill='#456752')
# Timber house: 160 x 128, five by four tiles, copper/slate roof.
x,y=128,64
d.rectangle((x+9,y+105,x+150,y+126),fill='#202730')
d.rectangle((x+14,y+39,x+146,y+117),fill='#776757')
d.rectangle((x+18,y+44,x+142,y+112),fill='#9a8769')
for xx in [19,72,139]:d.rectangle((x+xx,y+45,x+xx+5,y+113),fill='#423b38')
for yy in [79,109]: d.rectangle((x+18,y+yy,x+144,y+yy+4),fill='#514439')
d.polygon([(x+3,y+53),(x+30,y+6),(x+127,y+6),(x+157,y+53)],fill='#1b2531')
for yy in range(11,53,6):
    left=int(30-(yy-6)*.55);right=int(127+(yy-6)*.58)
    d.rectangle((x+left,y+yy,x+right,y+yy+4),fill=['#485567','#536174','#3d4b60'][yy%3])
    for xx in range(left+yy%12,right,15):d.line((x+xx,y+yy,x+xx-2,y+yy+4),fill='#293647')
d.line((x+30,y+6,x+127,y+6),fill='#89939b',width=2)
d.rectangle((x+104,y+1,x+117,y+25),fill='#646573');d.rectangle((x+101,y,x+120,y+4),fill='#999087')
for xx in [32,104]:
    d.rectangle((x+xx,y+63,x+xx+22,y+91),fill='#303039');d.rectangle((x+xx+3,y+66,x+xx+19,y+86),fill='#e6b274')
    d.rectangle((x+xx+5,y+68,x+xx+17,y+74),fill='#ffe0a0');d.line((x+xx+11,y+65,x+xx+11,y+88),fill='#5f4942',width=2)
    d.line((x+xx+2,y+78,x+xx+21,y+78),fill='#5f4942',width=2)
d.rectangle((x+65,y+85,x+86,y+118),fill='#242c31');d.rectangle((x+69,y+90,x+83,y+117),fill='#4d4941');d.point((x+79,y+104),fill='#e0b074')
d.rectangle((x+60,y+117,x+92,y+122),fill='#91908a')
# Small ruined chapel 96 x 96.
x,y=320,64
d.polygon([(x+4,y+37),(x+45,y+2),(x+90,y+37)],fill='#293445')
d.polygon([(x+8,y+35),(x+45,y+8),(x+85,y+35)],fill='#586273')
d.rectangle((x+12,y+36,x+83,y+91),fill='#4a5260')
for yy in range(39,88,9):
 for xx in range(14,80,17):d.rectangle((x+xx,y+yy,x+xx+14,y+yy+6),fill=['#647080','#535f70'][rng.randrange(2)])
d.rectangle((x+32,y+50,x+61,y+95),fill='#171f30');d.arc((x+28,y+39,x+65,y+70),180,360,fill='#9faba6',width=4)
d.ellipse((x+38,y+24,x+53,y+39),fill='#6aa7a0');d.ellipse((x+44,y+22,x+56,y+34),fill='#354454')
# Individual decorations: lamps, chest, shrine, herb, notice, ore, exit, stairs, stump, crystal, forge.
def obj(i):return (i%16)*32,(i//16)*32
x,y=obj(96);d.rectangle((x+14,y+9,x+17,y+31),fill='#443c40');d.rectangle((x+10,y+4,x+21,y+16),fill='#332d36');d.rectangle((x+12,y+6,x+19,y+13),fill='#edb870');d.rectangle((x+14,y+7,x+17,y+12),fill='#fff0b4')
x,y=obj(97);d.rounded_rectangle((x+4,y+12,x+28,y+29),radius=3,fill='#352e3b');d.rectangle((x+5,y+13,x+26,y+23),fill='#8b604e');d.rectangle((x+5,y+15,x+26,y+17),fill='#c29965');d.rectangle((x+14,y+18,x+17,y+24),fill='#f0c982')
x,y=obj(98);d.ellipse((x+2,y+24,x+30,y+31),fill='#354b55');d.rectangle((x+10,y+13,x+21,y+29),fill='#687c86');d.polygon([(x+16,y+1),(x+23,y+12),(x+16,y+22),(x+9,y+12)],fill='#96d5ca');d.polygon([(x+16,y+2),(x+17,y+16),(x+11,y+12)],fill='#d8ede0')
x,y=obj(99)
for xx,yy in [(7,25),(16,23),(24,26)]:
 d.line((x+xx,y+yy,x+xx-3,y+yy-12),fill='#688065',width=2);d.ellipse((x+xx-7,y+yy-13,x+xx,y+yy-8),fill='#89a278');d.rectangle((x+xx-2,y+yy-13,x+xx+1,y+yy-10),fill='#b8a8b9')
x,y=obj(100);d.rectangle((x+14,y+10,x+17,y+31),fill='#635141');d.rectangle((x+4,y+3,x+28,y+20),fill='#3b3439');d.rectangle((x+6,y+5,x+26,y+17),fill='#a29171');d.line((x+9,y+8,x+22,y+8),fill='#594c46');d.line((x+9,y+12,x+19,y+12),fill='#594c46')
x,y=obj(101);d.polygon([(x+3,y+28),(x+7,y+14),(x+19,y+10),(x+29,y+20),(x+26,y+30)],fill='#4d576b');d.polygon([(x+7,y+15),(x+19,y+12),(x+23,y+19),(x+12,y+25)],fill='#849096');d.line((x+10,y+18,x+16,y+23,x+23,y+20),fill='#d6b987',width=2)
x,y=obj(102);d.ellipse((x+3,y+15,x+28,y+30),outline='#78bdb4',width=2);d.ellipse((x+7,y+18,x+24,y+27),outline='#b3d7c9');d.line((x+16,y+5,x+16,y+22),fill='#9ad4c3',width=2)
x,y=obj(103);d.rectangle((x+2,y+3,x+29,y+30),fill='#162331')
for n in range(6):d.rectangle((x+3+n,y+5+n*4,x+28-n,y+7+n*4),fill=['#6a6b7e','#555c70','#414b62','#353f53','#293447','#1f293d'][n])
x,y=obj(104);d.rectangle((x+7,y+17,x+24,y+28),fill='#604c41');d.ellipse((x+5,y+11,x+26,y+23),fill='#ae936b');d.ellipse((x+9,y+14,x+22,y+20),outline='#6e5c48')
x,y=obj(105)
for xx,yy in [(11,29),(23,28),(17,24)]:d.polygon([(x+xx,y+yy-21),(x+xx+5,y+yy-10),(x+xx+2,y+yy),(x+xx-5,y+yy-4)],fill='#8295b9');d.line((x+xx,y+yy-20,x+xx,y+yy-3),fill='#bccde4')
x,y=obj(106);d.rectangle((x+3,y+13,x+28,y+20),fill='#806f5b');d.rectangle((x+5,y+20,x+9,y+30),fill='#4b443e');d.rectangle((x+23,y+20,x+27,y+30),fill='#4b443e');d.rectangle((x+11,y+6,x+25,y+12),fill='#859399');d.rectangle((x+16,y+11,x+21,y+16),fill='#596877')
(ROOT/'tilesets/soluna').mkdir(parents=True,exist_ok=True);atlas.save(ROOT/'tilesets/soluna/atlas.png')
# Original small monsters, twelve-frame direction sheets.
for name,base,eye in [('wisp','#8296b9','#dbe0b7'),('thorn','#526e61','#e2c09a'),('cinder','#9c6868','#ffdc9e'),('guardian','#565773','#b6d9d3'),('merchant','#826851','#dbba90'),('keeper','#58677e','#d7c9a2')]:
 sheet=Image.new('RGBA',(96,128));s=ImageDraw.Draw(sheet)
 for row in range(4):
  for f in range(3):
   x=f*32;y=row*32;bob=1 if f!=1 else 0
   s.ellipse((x+5,y+25,x+27,y+30),fill=(10,18,29,120))
   s.polygon([(x+16,y+4+bob),(x+25,y+13+bob),(x+27,y+26),(x+19,y+24),(x+16,y+29),(x+12,y+25),(x+5,y+27),(x+7,y+13+bob)],fill='#1c2635')
   s.polygon([(x+16,y+6+bob),(x+23,y+14+bob),(x+24,y+24),(x+15,y+23),(x+8,y+25),(x+9,y+14+bob)],fill=base)
   s.line((x+16,y+8+bob,x+11,y+16+bob),fill='#a0afa8',width=2)
   if name=='guardian':
    s.polygon([(x+7,y+13),(x+3,y+3),(x+13,y+9)],fill='#aab1b8');s.polygon([(x+22,y+12),(x+29,y+2),(x+20,y+8)],fill='#aab1b8')
   if row!=3:
    for xx in ([11,19] if row==0 else [11] if row==1 else [19]):s.rectangle((x+xx,y+15+bob,x+xx+2,y+17+bob),fill=eye)
 sheet.save(ROOT/f'characters/echoes/{name}.png')
# Neutral paper-doll base used by creation and selection, no demographic selections.
folder=ROOT/'characters/echoes';blank=Image.new('RGBA',(96,128));b=ImageDraw.Draw(blank)
for row in range(4):
 for f in range(3):
  x=f*32;y=row*32;bob=f%2
  b.ellipse((x+9,y+3+bob,x+22,y+16+bob),fill='#777f94');b.ellipse((x+10,y+3+bob,x+21,y+14+bob),fill='#eaede7')
  b.rectangle((x+11,y+16+bob,x+20,y+25),fill='#d3d9d8');b.rectangle((x+8,y+17+bob,x+10,y+23+bob),fill='#c4cdce');b.rectangle((x+21,y+17+bob,x+23,y+23+bob),fill='#c4cdce')
  b.rectangle((x+11,y+25,x+14,y+29-(f==0)),fill='#a6b1b8');b.rectangle((x+17,y+25,x+20,y+29-(f==2)),fill='#a6b1b8')
  if row!=3:
   for xx in ([13,18] if row==0 else [12] if row==1 else [19]):b.point((x+xx,y+10+bob),fill='#8c9aa4')
blank.save(folder/'blank.png')
(folder/'catalog.json').write_text(json.dumps({'frame':32,'sheets':{'blank':{'file':'blank.png','z':0}}},indent=2))
(folder/'options.json').write_text(json.dumps({'body':'blank','bodies':[{'id':'blank','label':'Sem forma'}],'skins':[{'id':'neutral','label':'Neutro'}],'hair':[],'hairColors':[],'eyes':[],'races':[{'id':'unformed','label':'Sem origem'}],'beards':[]},indent=2))
print('Fallback atlas, six NPC sheets and neutral character generated; authored art pages unchanged.')
