#!/usr/bin/env python3
"""辞書アプリ・IME 本体のアイコン (mac/Resources/Meltype.icns) を元絵から作り直す。

元絵: 氷の M の横長画像 (1536x1024、リョウさんが用意)。リポジトリには正方形に整えた icon-1024.png を置いている。
使い方: python3 tools/make_icon.py <元絵.png> (Pillow が要る。実行場所は mac/Resources に出力する)
やること: 四隅の色で正方形に広げる → 中央 1100px を切り出して 1024px に → macOS 風の角丸 (半径 22.37%) → iconset → iconutil で .icns。
"""
import os, subprocess, sys
from PIL import Image, ImageDraw

src = Image.open(sys.argv[1]).convert('RGBA')
out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'mac', 'Resources')
W, H = src.size
bg = tuple(sum(src.getpixel(p)[i] for p in [(2,2),(W-3,2),(2,H-3),(W-3,H-3)])//4 for i in range(3)) + (255,)
S = max(W, H)
canvas = Image.new('RGBA', (S, S), bg)
canvas.paste(src, ((S - W)//2, (S - H)//2), src)
side = int(S * 1100 / 1536)
dy = int(S * 30 / 1536)  # 下に重心を寄せる
box = ((S-side)//2, (S-side)//2 + dy, (S+side)//2, (S+side)//2 + dy)
sq = canvas.crop(box).resize((1024, 1024), Image.LANCZOS)
mask = Image.new('L', (1024, 1024), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, 1023, 1023), radius=int(1024*0.2237), fill=255)
out = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
out.paste(sq, (0, 0), mask)
out.save(os.path.join(out_dir, 'icon-1024.png'))
iconset = os.path.join(out_dir, 'Meltype.iconset')
os.makedirs(iconset, exist_ok=True)
for n in [16, 32, 128, 256, 512]:
    out.resize((n, n), Image.LANCZOS).save(f'{iconset}/icon_{n}x{n}.png')
    out.resize((n*2, n*2), Image.LANCZOS).save(f'{iconset}/icon_{n}x{n}@2x.png')
subprocess.run(['iconutil', '-c', 'icns', iconset, '-o', os.path.join(out_dir, 'Meltype.icns')], check=True)
subprocess.run(['rm', '-rf', iconset], check=True)
print('wrote mac/Resources/icon-1024.png and Meltype.icns')
