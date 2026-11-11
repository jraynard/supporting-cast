"""Read and write Aseprite files (.aseprite) without Aseprite: RGBA sprites with layers, frames, tags
(and their user data), slices and a palette. Standard library only, so the exporter can use it too.

Follows the file format spec (aseprite/docs/ase-file-specs.md): a 128-byte header, then frames,
each a list of chunks. The first frame carries the palette, the layers (bottom first), the tags
with one user-data chunk per tag after them, and the slices; every frame carries its cels.
Cels are stored compressed (zlib) and cropped to their opaque pixels.
"""
import struct
import zlib

MAGIC = 0xA5E0
FRAME_MAGIC = 0xF1FA
CHUNK_LAYER, CHUNK_CEL, CHUNK_TAGS, CHUNK_PALETTE, CHUNK_USER_DATA, CHUNK_SLICE = 0x2004, 0x2005, 0x2018, 0x2019, 0x2020, 0x2022

LAYER_VISIBLE, LAYER_EDITABLE = 1, 2


class Layer:
    """One layer: a name, visibility, opacity, and a cel per frame (None, or (x, y, w, h, rgba bytes))."""

    def __init__(self, name, visible=True, opacity=255):
        self.name, self.visible, self.opacity = name, visible, opacity
        self.cels = {}


class Sprite:
    """An RGBA sprite: size, per-frame durations (ms), layers (bottom first), tags
    [(name, from, to, user_text)], slices [(name, x, y, w, h, pivot or None)] and a palette of RGBA tuples."""

    def __init__(self, width, height):
        self.width, self.height = width, height
        self.durations = []
        self.layers = []
        self.tags = []
        self.slices = []
        self.palette = []

    def layer(self, name, visible=True, opacity=255):
        layer = Layer(name, visible, opacity)
        self.layers.append(layer)
        return layer


def _string(s):
    b = s.encode("utf-8")
    return struct.pack("<H", len(b)) + b


def _chunk(kind, data):
    return struct.pack("<IH", len(data) + 6, kind) + data


def crop_rgba(width, height, rgba):
    """(x, y, w, h, bytes) of a full RGBA image cropped to its opaque pixels, or None if empty."""
    xs, ys = [], []
    for y in range(height):
        row = rgba[y * width * 4:(y + 1) * width * 4]
        for x in range(width):
            if row[x * 4 + 3]:
                xs.append(x)
                ys.append(y)
    if not xs:
        return None
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    w, h = x1 - x0 + 1, y1 - y0 + 1
    out = bytearray()
    for y in range(y0, y1 + 1):
        out += rgba[(y * width + x0) * 4:(y * width + x1 + 1) * 4]
    return x0, y0, w, h, bytes(out)


def write(sprite, path):
    frames = []
    for i, duration in enumerate(sprite.durations):
        chunks = []
        if i == 0:
            if sprite.palette:
                pal = struct.pack("<III8x", len(sprite.palette), 0, len(sprite.palette) - 1)
                for r, g, b, a in sprite.palette:
                    pal += struct.pack("<HBBBB", 0, r, g, b, a)
                chunks.append(_chunk(CHUNK_PALETTE, pal))
            for layer in sprite.layers:
                flags = (LAYER_VISIBLE if layer.visible else 0) | LAYER_EDITABLE
                data = struct.pack("<HHHHHHB3x", flags, 0, 0, 0, 0, 0, layer.opacity) + _string(layer.name)
                chunks.append(_chunk(CHUNK_LAYER, data))
            if sprite.tags:
                data = struct.pack("<H8x", len(sprite.tags))
                for name, a, b, _ in sprite.tags:
                    data += struct.pack("<HHBH6x3Bx", a, b, 0, 0, 0, 0, 0) + _string(name)
                chunks.append(_chunk(CHUNK_TAGS, data))
                for _, _, _, text in sprite.tags:   # one user-data chunk per tag, in order
                    chunks.append(_chunk(CHUNK_USER_DATA, struct.pack("<I", 1) + _string(text) if text else struct.pack("<I", 0)))
            for name, x, y, w, h, pivot in sprite.slices:
                flags = 2 if pivot else 0
                data = struct.pack("<III", 1, flags, 0) + _string(name) + struct.pack("<IiiII", 0, x, y, w, h)
                if pivot:
                    data += struct.pack("<ii", *pivot)
                chunks.append(_chunk(CHUNK_SLICE, data))
        for index, layer in enumerate(sprite.layers):
            cel = layer.cels.get(i)
            if cel is None:
                continue
            x, y, w, h, pixels = cel
            data = struct.pack("<HhhBHh5x", index, x, y, 255, 2, 0) + struct.pack("<HH", w, h) + zlib.compress(pixels, 9)
            chunks.append(_chunk(CHUNK_CEL, data))
        body = b"".join(chunks)
        header = struct.pack("<IHHH2xI", 16 + len(body), FRAME_MAGIC, min(len(chunks), 0xFFFF), duration, len(chunks))
        frames.append(header + body)
    body = b"".join(frames)
    header = struct.pack("<IHHHHHIHII B3x H BB hhHH 84x".replace(" ", ""),
                         128 + len(body), MAGIC, len(sprite.durations), sprite.width, sprite.height, 32, 1, 100, 0, 0,
                         0, len(sprite.palette) if 0 < len(sprite.palette) <= 256 else 0, 1, 1, 0, 0, 16, 16)
    assert len(header) == 128
    with open(path, "wb") as f:
        f.write(header + body)


def read(path):
    """Parse a file into a Sprite (RGBA only; layer groups, linked cels and tilemaps aren't supported)."""
    with open(path, "rb") as f:
        data = f.read()
    size, magic, nframes, width, height, depth = struct.unpack_from("<IHHHHH", data, 0)
    if magic != MAGIC:
        raise ValueError(f"{path}: not an Aseprite file")
    if depth != 32:
        raise ValueError(f"{path}: only RGBA sprites are supported (depth {depth})")
    sprite = Sprite(width, height)
    pos = 128
    tag_user = []
    for i in range(nframes):
        fsize, fmagic, old, duration, nchunks = struct.unpack_from("<IHHH2xI", data, pos)
        if fmagic != FRAME_MAGIC:
            raise ValueError(f"{path}: bad frame {i}")
        sprite.durations.append(duration)
        count = nchunks or old
        p = pos + 16
        expect_tag_user = 0
        for _ in range(count):
            csize, kind = struct.unpack_from("<IH", data, p)
            c = data[p + 6:p + csize]
            if kind == CHUNK_LAYER:
                flags = struct.unpack_from("<H", c, 0)[0]
                opacity = c[12]
                n = struct.unpack_from("<H", c, 16)[0]
                sprite.layer(c[18:18 + n].decode("utf-8"), bool(flags & LAYER_VISIBLE), opacity)
            elif kind == CHUNK_CEL:
                index, x, y, _, ctype = struct.unpack_from("<HhhBH", c, 0)
                if ctype == 2:
                    w, h = struct.unpack_from("<HH", c, 16)
                    sprite.layers[index].cels[i] = (x, y, w, h, zlib.decompress(c[20:]))
                elif ctype == 0:
                    w, h = struct.unpack_from("<HH", c, 16)
                    sprite.layers[index].cels[i] = (x, y, w, h, bytes(c[20:20 + w * h * 4]))
            elif kind == CHUNK_TAGS:
                ntags = struct.unpack_from("<H", c, 0)[0]
                q = 10
                for _ in range(ntags):
                    a, b = struct.unpack_from("<HH", c, q)
                    q += 17
                    n = struct.unpack_from("<H", c, q)[0]
                    sprite.tags.append([c[q + 2:q + 2 + n].decode("utf-8"), a, b, ""])
                    q += 2 + n
                expect_tag_user = ntags
                tag_user = []
            elif kind == CHUNK_USER_DATA and expect_tag_user:
                flags = struct.unpack_from("<I", c, 0)[0]
                text = ""
                if flags & 1:
                    n = struct.unpack_from("<H", c, 4)[0]
                    text = c[6:6 + n].decode("utf-8")
                tag_user.append(text)
                sprite.tags[len(sprite.tags) - expect_tag_user + len(tag_user) - 1][3] = text
                if len(tag_user) == expect_tag_user:
                    expect_tag_user = 0
            elif kind == CHUNK_SLICE:
                nkeys, flags = struct.unpack_from("<II", c, 0)
                n = struct.unpack_from("<H", c, 12)[0]
                name = c[14:14 + n].decode("utf-8")
                q = 14 + n
                _, x, y, w, h = struct.unpack_from("<IiiII", c, q)
                pivot = struct.unpack_from("<ii", c, q + 20 + (16 if flags & 1 else 0)) if flags & 2 else None
                sprite.slices.append((name, x, y, w, h, pivot))
            p += csize
        pos += fsize
    sprite.tags = [tuple(t) for t in sprite.tags]
    return sprite


def layer_names(path):
    """Just the layer names (bottom first): what the exporter and the art generator need to know."""
    return [layer.name for layer in read(path).layers]
