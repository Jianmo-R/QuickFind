from PIL import Image, ImageDraw

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Rounded-square background with a diagonal blue gradient.
bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
bd = ImageDraw.Draw(bg)
for y in range(S):
    t = y / (S - 1)
    r = int(32 + (20 - 32) * t)
    g = int(84 + (43 - 84) * t)
    b = int(190 + (112 - 190) * t)
    bd.line([(0, y), (S, y)], fill=(r, g, b, 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=230, fill=255)
img.paste(bg, (0, 0), mask)

# Soft highlight in the upper-left.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([-260, -330, 720, 520], fill=(255, 255, 255, 34))
img.alpha_composite(Image.composite(glow, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

# Magnifying glass: white ring and handle.
d = ImageDraw.Draw(img)
cx, cy, r = 432, 432, 228
ring = 78
d.ellipse([cx - r - ring // 2, cy - r - ring // 2, cx + r + ring // 2, cy + r + ring // 2],
          fill=(255, 255, 255, 255))
d.ellipse([cx - r + ring // 2, cy - r + ring // 2, cx + r - ring // 2, cy + r - ring // 2],
          fill=(37, 99, 192, 255))
# Tint the lens interior with a light sheen.
lens = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ld = ImageDraw.Draw(lens)
ld.ellipse([cx - r + ring // 2, cy - r + ring // 2, cx + r - ring // 2, cy + r - ring // 2],
           fill=(130, 200, 255, 66))
ld.ellipse([cx - r + 70, cy - r + 55, cx + 40, cy + 120], fill=(255, 255, 255, 80))
img.alpha_composite(lens)

# Handle.
d = ImageDraw.Draw(img)
d.line([cx + 172, cy + 172, cx + 390, cy + 390], fill=(255, 255, 255, 255), width=112)
d.ellipse([cx + 338, cy + 338, cx + 442, cy + 442], fill=(255, 255, 255, 255))

# A little location pin / spark accent at lower-left.
accent = (120, 224, 255, 255)
d.ellipse([208, 666, 328, 786], fill=accent)
d.polygon([(268, 792), (268, 698), (330, 742)], fill=accent)

img = img.resize((512, 512), Image.LANCZOS)
img.save("app.png")
img.save("app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("wrote app.ico")
