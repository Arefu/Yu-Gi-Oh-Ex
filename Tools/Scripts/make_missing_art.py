"""Writes Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Cards/MissingArt.h: the art a custom card without a picture shows (MoreCards
Hook_Get_IllustrationData), the 'missing texture' checkerboard - magenta and black, 8 x 8 squares - as a 304 x 304 JPEG like the
game's own card art, kept as raw bytes in the DLL (no file to ship). Run again to change it."""
import io, os
from PIL import Image, ImageDraw

SIZE, SQUARES = 304, 8
image = Image.new('RGB', (SIZE, SIZE), (0, 0, 0))
draw = ImageDraw.Draw(image)
step = SIZE / SQUARES
for y in range(SQUARES):
    for x in range(SQUARES):
        if (x + y) % 2 == 0:
            draw.rectangle([round(x * step), round(y * step), round((x + 1) * step) - 1, round((y + 1) * step) - 1], fill=(255, 0, 220))
buffer = io.BytesIO()
image.save(buffer, 'JPEG', quality=90, subsampling=0)
data = buffer.getvalue()

here = os.path.dirname(os.path.abspath(__file__))
target = os.path.join(here, '..', '..', 'Yu-Gi-Oh_Loader Plugins', 'Yu-Gi-Oh-Cards', 'MissingArt.h')
lines = ['#pragma once', '', '#include <cstddef>', '#include <cstdint>', '',
         '// The art a custom card without a picture shows: the "missing texture" checkerboard (magenta and black, 8 x 8), a 304 x 304 JPEG like',
         '// the game\'s card art, as raw bytes so there is no file to ship. Made by Tools/Scripts/make_missing_art.py (run it again to change it).',
         'namespace MissingArt', '{',
         f'    inline constexpr std::size_t kSize = {len(data)};',
         '    inline constexpr std::uint8_t kJpeg[kSize] = {']
for i in range(0, len(data), 24):
    lines.append('        ' + ', '.join(f'0x{b:02X}' for b in data[i:i + 24]) + ',')
lines += ['    };', '}', '']
with open(target, 'w', newline='\r\n') as f:
    f.write('\n'.join(lines))
print(len(data), 'bytes ->', os.path.normpath(target))
